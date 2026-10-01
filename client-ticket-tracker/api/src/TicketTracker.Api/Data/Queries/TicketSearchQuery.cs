using System.Data;
using Microsoft.Data.SqlClient;
using TicketTracker.Api.Domain;
using TicketTracker.Api.Dtos;

namespace TicketTracker.Api.Data.Queries;

/// <summary>
/// The hand-written search query: plain ADO.NET (SqlConnection / SqlCommand /
/// SqlDataReader) with explicit, typed parameters. No ORM involved.
///
/// Why not EF Core here?
///   - EF Core has no support for full-text CONTAINS on SQL Server beyond a basic
///     EF.Functions.Contains, and none for the UNION-then-rank shape used below.
///   - Search is the query most worth tuning, so the SQL should be exactly what
///     runs, readable in one place, and copy-pasteable into SSMS to look at the plan.
///
/// How one search box covers three kinds of search:
///   1. Input is a whole number ("123" or "#123")  ->  ticket ID lookup (ByIdSql).
///   2. Otherwise, one query with two UNION ALL branches:
///        a. client name starts with the text      (index seek on IX_Clients_Name)
///        b. every word appears in title/description (full-text index)
///      A ticket can match both branches; the lowest MatchRank wins, so
///      client-name matches are listed first, then newest first.
/// </summary>
public sealed class TicketSearchQuery(IConfiguration config, FullTextStatus fullText)
{
    public const int MaxResults = 50;

    private const string ByIdSql = """
        SELECT t.Id, t.Title, t.Status, t.Priority, t.Category, c.Name AS ClientName, t.CreatedAt,
               0 AS MatchRank
        FROM dbo.Tickets AS t
        INNER JOIN dbo.Clients AS c ON c.Id = t.ClientId
        WHERE t.Id = @Id;               -- clustered primary key: a single-row seek
        """;

    // Why UNION ALL instead of  WHERE c.Name LIKE ... OR CONTAINS(...)?
    // The two conditions are on different tables and served by different indexes.
    // Joined with OR, the optimiser usually can't use either index for a seek and
    // falls back to scanning. As separate branches, each one uses its own index,
    // and the small result sets are combined afterwards.
    private const string FullTextSql = """
        WITH Matches AS (
            -- Branch a: client name starts with the search text.
            -- 'prefix%' is sargable: SQL Server can seek IX_Clients_Name to the
            -- first matching name and read forward. '%text%' could not use it.
            SELECT t.Id, 1 AS MatchRank
            FROM dbo.Clients AS c
            INNER JOIN dbo.Tickets AS t ON t.ClientId = c.Id
            WHERE c.Name LIKE @NamePrefix ESCAPE '\'

            UNION ALL

            -- Branch b: full-text index on SearchText (Title + ' ' + Description,
            -- a computed column created in db/fulltext.sql). Using one combined column
            -- is deliberate: with CONTAINS((Title, Description), 'a AND b') SQL Server
            -- requires every term to be in the SAME column, so "login" in the title plus
            -- "timeout" in the description would not match.
            SELECT t.Id, 2 AS MatchRank
            FROM dbo.Tickets AS t
            WHERE CONTAINS(SearchText, @FullTextTerm)
        ),
        BestMatch AS (
            SELECT Id, MIN(MatchRank) AS MatchRank
            FROM Matches
            GROUP BY Id
        )
        SELECT TOP (@MaxResults)
               t.Id, t.Title, t.Status, t.Priority, t.Category, c.Name AS ClientName, t.CreatedAt,
               b.MatchRank
        FROM BestMatch AS b
        INNER JOIN dbo.Tickets AS t ON t.Id = b.Id
        INNER JOIN dbo.Clients AS c ON c.Id = t.ClientId
        ORDER BY b.MatchRank, t.CreatedAt DESC;
        """;

    // Used when the SQL Server instance has no full-text index (e.g. Full-Text
    // Search isn't installed). Same shape, but branch b is  LIKE '%text%'.
    // The leading wildcard means no index can help: SQL Server reads every
    // ticket's title and description (a scan). Fine for hundreds of rows, not for
    // millions. That contrast with branch b above is the point of keeping both.
    private const string LikeFallbackSql = """
        WITH Matches AS (
            SELECT t.Id, 1 AS MatchRank
            FROM dbo.Clients AS c
            INNER JOIN dbo.Tickets AS t ON t.ClientId = c.Id
            WHERE c.Name LIKE @NamePrefix ESCAPE '\'

            UNION ALL

            SELECT t.Id, 2 AS MatchRank
            FROM dbo.Tickets AS t
            WHERE t.Title LIKE @ContainsPattern ESCAPE '\'
               OR t.Description LIKE @ContainsPattern ESCAPE '\'
        ),
        BestMatch AS (
            SELECT Id, MIN(MatchRank) AS MatchRank
            FROM Matches
            GROUP BY Id
        )
        SELECT TOP (@MaxResults)
               t.Id, t.Title, t.Status, t.Priority, t.Category, c.Name AS ClientName, t.CreatedAt,
               b.MatchRank
        FROM BestMatch AS b
        INNER JOIN dbo.Tickets AS t ON t.Id = b.Id
        INNER JOIN dbo.Clients AS c ON c.Id = t.ClientId
        ORDER BY b.MatchRank, t.CreatedAt DESC;
        """;

    public async Task<SearchResultDto> SearchAsync(string rawQuery, CancellationToken ct)
    {
        var query = rawQuery.Trim();

        // A new SqlConnection per call is cheap: SqlClient keeps a pool of open
        // physical connections per connection string, and "opening" usually just
        // borrows one. Disposing returns it to the pool.
        await using var conn = new SqlConnection(ConnectionString);
        await conn.OpenAsync(ct);

        if (SearchInput.TryParseTicketId(query, out var id))
        {
            await using var cmd = new SqlCommand(ByIdSql, conn);
            cmd.Parameters.Add(new SqlParameter("@Id", SqlDbType.Int) { Value = id });
            return new SearchResultDto(query, "id", await ReadHitsAsync(cmd, ct));
        }

        var namePrefix = SearchInput.EscapeLike(query) + "%";
        var fullTextTerm = SearchInput.BuildFullTextTerm(query);
        var useFullText = fullTextTerm is not null && await fullText.IsAvailableAsync(conn, ct);

        await using var search = new SqlCommand(useFullText ? FullTextSql : LikeFallbackSql, conn);
        search.Parameters.Add(NVarChar("@NamePrefix", namePrefix));
        search.Parameters.Add(new SqlParameter("@MaxResults", SqlDbType.Int) { Value = MaxResults });

        if (useFullText)
            search.Parameters.Add(NVarChar("@FullTextTerm", fullTextTerm!));
        else
            search.Parameters.Add(NVarChar("@ContainsPattern", "%" + SearchInput.EscapeLike(query) + "%"));

        var hits = await ReadHitsAsync(search, ct);
        return new SearchResultDto(query, useFullText ? "fulltext" : "like-fallback", hits);
    }

    /// <summary>
    /// Why explicit SqlDbType and Size instead of cmd.Parameters.AddWithValue(name, value)?
    ///   - AddWithValue infers the type from the .NET value (string -> nvarchar), so
    ///     against a varchar column it forces an implicit conversion that can
    ///     turn an index seek into a scan. Stating the type avoids depending on the guess.
    ///   - AddWithValue sets Size to the length of *this* value, so searching "acme"
    ///     sends nvarchar(5) and "acme corp" sends nvarchar(10). SQL Server caches a
    ///     separate plan for each distinct parameter declaration, which fills the
    ///     plan cache with near-duplicates. One fixed size gives one reusable plan.
    ///   - The size must be large enough never to truncate: SqlClient silently cuts
    ///     values longer than Size. 4000 (the nvarchar maximum before MAX) is safely
    ///     above anything SearchInput can produce from a 200-character query.
    /// </summary>
    private static SqlParameter NVarChar(string name, string value) =>
        new(name, SqlDbType.NVarChar, 4000) { Value = value };

    private static async Task<IReadOnlyList<SearchHitDto>> ReadHitsAsync(SqlCommand cmd, CancellationToken ct)
    {
        var hits = new List<SearchHitDto>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);

        // Ordinals match the SELECT lists above, which are identical in all three queries.
        while (await reader.ReadAsync(ct))
        {
            hits.Add(new SearchHitDto(
                Id: reader.GetInt32(0),
                Title: reader.GetString(1),
                Status: (TicketStatus)reader.GetInt32(2),
                Priority: (TicketPriority)reader.GetInt32(3),
                Category: (TicketCategory)reader.GetInt32(4),
                ClientName: reader.GetString(5),
                CreatedAt: reader.GetDateTimeOffset(6),
                MatchedOn: reader.GetInt32(7) switch
                {
                    0 => "id",
                    1 => "client",
                    _ => "keyword",
                }));
        }

        return hits;
    }

    private string ConnectionString =>
        config.GetConnectionString("TicketTracker")
        ?? throw new InvalidOperationException("Connection string 'TicketTracker' is not configured.");
}

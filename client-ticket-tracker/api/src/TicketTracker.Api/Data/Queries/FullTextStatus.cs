using Microsoft.Data.SqlClient;

namespace TicketTracker.Api.Data.Queries;

/// <summary>
/// Answers "does dbo.Tickets have an active full-text index?" once per process.
///
/// A query that uses CONTAINS fails at compile time (error 7601) if the table
/// has no full-text index, and full-text search is an optional SQL Server
/// component that often isn't installed (the standard Docker image doesn't
/// include it). So instead of assuming, the app checks once and picks the
/// full-text query or the LIKE fallback.
///
/// Registered as a singleton so the answer is cached for the app's lifetime.
/// Trade-off: after running db/fulltext.sql, restart the API to pick it up.
/// </summary>
public sealed class FullTextStatus(ILogger<FullTextStatus> logger)
{
    // 0 = not checked yet, 1 = available, 2 = not available.
    // A plain int (read/written with Volatile) rather than a bool?, because
    // reads and writes of an int are atomic and a Nullable<bool> isn't
    // guaranteed to be. Two requests racing to do the first check is harmless:
    // they both compute the same answer.
    private int _state;

    public async Task<bool> IsAvailableAsync(SqlConnection openConnection, CancellationToken ct)
    {
        var state = Volatile.Read(ref _state);
        if (state != 0)
            return state == 1;

        await using var cmd = new SqlCommand(
            "SELECT CAST(OBJECTPROPERTYEX(OBJECT_ID(N'dbo.Tickets'), 'TableHasActiveFulltextIndex') AS int);",
            openConnection);
        var result = await cmd.ExecuteScalarAsync(ct);
        var available = result is int flag && flag == 1;

        Volatile.Write(ref _state, available ? 1 : 2);
        logger.LogInformation(
            "Keyword search mode: {Mode}",
            available ? "full-text index" : "LIKE fallback (run db/fulltext.sql, then restart, to enable full-text)");
        return available;
    }
}

/*
    See for yourself why the search query is written the way it is.

    1. Run db/bulk-data.sql first; on 10 rows every plan looks the same.
    2. In SSMS: Query > Include Actual Execution Plan (Ctrl+M).
       In VS Code (mssql extension): use "Run Query with Actual Plan", or read the
       STATISTICS IO output only.
    3. Run the whole script. For each query, compare:
         - the plan's operators: Index SEEK (jumps to matching rows) vs SCAN (reads everything)
         - "logical reads" in the Messages tab (8 KB pages read; lower is better)

    Each query runs through sp_executesql with typed parameters, exactly like the
    API does: when SqlClient sends a parameterised command, it goes over the wire
    as an sp_executesql call. You'll see the same thing in a SQL trace.
*/
USE TicketTracker;
GO
SET STATISTICS IO ON;
GO

-- A. Client name, prefix match: 'Cedar%'
--    Expect: Index Seek on IX_Clients_Name. The index is sorted by Name, so SQL
--    Server jumps to the first "Cedar..." entry and stops after the last one.
EXEC sp_executesql
    N'SELECT c.Id, c.Name FROM dbo.Clients AS c WHERE c.Name LIKE @NamePrefix ESCAPE ''\'';',
    N'@NamePrefix nvarchar(4000)',
    @NamePrefix = N'Cedar%';

-- B. Same column, leading wildcard: '%edar%'
--    Expect: Index Scan. With a leading wildcard the matching names could be
--    anywhere in the sorted index, so every entry must be read.
EXEC sp_executesql
    N'SELECT c.Id, c.Name FROM dbo.Clients AS c WHERE c.Name LIKE @Pattern ESCAPE ''\'';',
    N'@Pattern nvarchar(4000)',
    @Pattern = N'%edar%';

-- C. Keyword via LIKE (the API's fallback when full-text isn't available).
--    Expect: Clustered Index Scan of Tickets; logical reads ~ every page of the table.
EXEC sp_executesql
    N'SELECT t.Id, t.Title FROM dbo.Tickets AS t
      WHERE t.Title LIKE @Pattern ESCAPE ''\'' OR t.Description LIKE @Pattern ESCAPE ''\'';',
    N'@Pattern nvarchar(4000)',
    @Pattern = N'%timeout%';

-- D. Keyword via the full-text index (skip if db/fulltext.sql reported FTS isn't installed).
--    Expect: a "Table Valued Function [FulltextMatch]" operator that returns
--    matching keys from the full-text index, then Clustered Index Seeks on Tickets
--    for just those rows. Compare logical reads with C.
EXEC sp_executesql
    N'SELECT t.Id, t.Title FROM dbo.Tickets AS t WHERE CONTAINS(SearchText, @Term);',
    N'@Term nvarchar(4000)',
    @Term = N'"timeout*"';

-- E. Why the API uses UNION ALL instead of OR.
--    E1 is the OR version: one condition per table, joined with OR. You will likely see the
--    optimiser scan, because no single index can answer "A or B" here.
--    E2 splits it into two queries combined with UNION (the API does the same with
--    UNION ALL + GROUP BY, so it can keep the best MatchRank). Each branch uses its own index.
EXEC sp_executesql
    N'SELECT t.Id FROM dbo.Tickets AS t
      INNER JOIN dbo.Clients AS c ON c.Id = t.ClientId
      WHERE c.Name LIKE @NamePrefix ESCAPE ''\''
         OR CONTAINS(SearchText, @Term);',
    N'@NamePrefix nvarchar(4000), @Term nvarchar(4000)',
    @NamePrefix = N'Cedar%', @Term = N'"timeout*"';

EXEC sp_executesql
    N'SELECT t.Id FROM dbo.Clients AS c
      INNER JOIN dbo.Tickets AS t ON t.ClientId = c.Id
      WHERE c.Name LIKE @NamePrefix ESCAPE ''\''
      UNION
      SELECT t.Id FROM dbo.Tickets AS t
      WHERE CONTAINS(SearchText, @Term);',
    N'@NamePrefix nvarchar(4000), @Term nvarchar(4000)',
    @NamePrefix = N'Cedar%', @Term = N'"timeout*"';

-- F. A simplified version of the queue query (EF Core's real one also joins
--    Clients and uses OFFSET/FETCH; you can see it in the API's console log).
--    Expect an Index Seek on IX_Tickets_Queue and NO Sort operator: the index is
--    already in (Status, Priority DESC, CreatedAt, Id) order. Title isn't in the
--    index, so you'll also see a Key Lookup per row to fetch it: cheap for 25 rows.
EXEC sp_executesql
    N'SELECT TOP (25) t.Id, t.Title, t.Priority, t.CreatedAt
      FROM dbo.Tickets AS t
      WHERE t.Status = @Status
      ORDER BY t.Priority DESC, t.CreatedAt, t.Id;',
    N'@Status int',
    @Status = 0;
GO
SET STATISTICS IO OFF;

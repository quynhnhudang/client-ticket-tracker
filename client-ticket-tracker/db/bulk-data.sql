/*
    OPTIONAL: adds ~2,000 clients and 50,000 synthetic tickets so that execution
    plans mean something.

    With the 10 sample tickets, SQL Server will often scan anyway: reading one
    8 KB page is cheaper than navigating an index. Index seeks versus scans only
    show a real difference once tables span thousands of pages. Run this, then
    db/explain-search.sql, to see the difference.

    Honest limits of this data:
      - Text is generated from templates, so it's repetitive.
      - Each synthetic ticket has only its "created" history row, even when its
        status isn't Open. Fine for performance testing; not a realistic audit trail.

    Takes about 10-30 seconds. The full-text index then catches up in the
    background (see PopulateStatus at the end of db/fulltext.sql).
    To remove it later, drop and recreate the database (dotnet ef database drop,
    then dotnet ef database update).
*/
USE TicketTracker;
GO

SET NOCOUNT ON;
SET XACT_ABORT ON;

DECLARE @AgentId int = (SELECT TOP (1) Id FROM dbo.Agents ORDER BY Id);
IF @AgentId IS NULL
BEGIN
    RAISERROR(N'No agent exists yet. Start the API once first.', 16, 1);
    RETURN;
END;

IF EXISTS (SELECT 1 FROM dbo.Clients WHERE Email LIKE N'%@synthetic.example')
BEGIN
    PRINT N'Synthetic data already present; nothing inserted.';
    RETURN;
END;

DECLARE @Now datetimeoffset = TODATETIMEOFFSET(SYSUTCDATETIME(), '+00:00');

BEGIN TRANSACTION;

-- A "numbers table" built on the fly: cross-joining a system view with itself
-- gives far more than 50,000 rows, and ROW_NUMBER numbers them 1..N.
-- Set-based inserts like this are much faster than a WHILE loop of single INSERTs.
WITH Numbers AS (
    SELECT TOP (2000) CAST(ROW_NUMBER() OVER (ORDER BY (SELECT NULL)) AS int) AS n
    FROM sys.all_objects AS a CROSS JOIN sys.all_objects AS b
)
INSERT INTO dbo.Clients (Name, Email, CreatedAt)
SELECT
    CONCAT(
        CHOOSE(n % 10 + 1, N'Cedar', N'Harbor', N'Summit', N'Pioneer', N'Granite',
                           N'Maple', N'Beacon', N'Orchard', N'Falcon', N'Riverside'),
        N' ',
        CHOOSE((n / 10) % 8 + 1, N'Logistics', N'Health', N'Credit Union', N'Retail',
                                 N'Schools', N'Manufacturing', N'Insurance', N'Foods'),
        N' ', n),
    CONCAT(N'client', n, N'@synthetic.example'),
    DATEADD(day, -(n % 365), @Now)
FROM Numbers;

WITH Numbers AS (
    SELECT TOP (50000) CAST(ROW_NUMBER() OVER (ORDER BY (SELECT NULL)) AS int) AS n
    FROM sys.all_objects AS a CROSS JOIN sys.all_objects AS b
),
SyntheticClients AS (
    SELECT Id, ROW_NUMBER() OVER (ORDER BY Id) AS k
    FROM dbo.Clients
    WHERE Email LIKE N'%@synthetic.example'
)
INSERT INTO dbo.Tickets
    (Title, Description, Category, Priority, Status, ResolutionNotes,
     CreatedAt, UpdatedAt, ResolvedAt, ClientId, CreatedByAgentId)
SELECT
    CONCAT(
        CHOOSE(n % 12 + 1, N'Cannot log in', N'Export fails', N'Page loads slowly', N'Wrong total on invoice',
                           N'Email notifications delayed', N'Duplicate records', N'Question about permissions',
                           N'Upload rejected', N'Report shows old data', N'Sync error', N'Password reset issue',
                           N'Search returns nothing'),
        N' (#', n, N')'),
    CHOOSE(n % 7 + 1,
        N'Customer reports an error message after the latest update. Screenshot attached by the client.',
        N'The request never completes and the browser eventually shows a gateway timeout.',
        N'Numbers in the monthly summary do not match the detailed export for the same period.',
        N'Several users are affected. Workaround: refreshing the page twice sometimes helps.',
        N'Started after the data import last week. Needs checking against the source file.',
        N'Client asks how to configure this for their team and whether admins can restrict it.',
        N'Intermittent: roughly one in ten attempts fails with a server error.'),
    n % 3,                                        -- Category 0..2
    n % 4,                                        -- Priority 0..3
    (n / 3) % 4,                                  -- Status 0..3 (not aligned with Category)
    CASE WHEN (n / 3) % 4 = 3 THEN N'Synthetic ticket resolved.' END,
    DATEADD(minute, -n * 7, @Now),
    DATEADD(minute, -n * 7, @Now),
    CASE WHEN (n / 3) % 4 = 3 THEN DATEADD(minute, -n * 7 + 60, @Now) END,
    c.Id,
    @AgentId
FROM Numbers
INNER JOIN SyntheticClients AS c ON c.k = (Numbers.n % 2000) + 1;

INSERT INTO dbo.TicketStatusHistory (TicketId, FromStatus, ToStatus, ChangedByAgentId, ChangedAt, Note)
SELECT t.Id, NULL, 0, @AgentId, t.CreatedAt, N'Ticket created (synthetic)'
FROM dbo.Tickets AS t
INNER JOIN dbo.Clients AS c ON c.Id = t.ClientId
WHERE c.Email LIKE N'%@synthetic.example';

COMMIT;

-- Refresh statistics so the optimiser's row estimates reflect the new data
-- straight away, instead of waiting for automatic statistics updates.
EXEC sp_updatestats;

SELECT (SELECT COUNT(*) FROM dbo.Clients) AS Clients, (SELECT COUNT(*) FROM dbo.Tickets) AS Tickets;

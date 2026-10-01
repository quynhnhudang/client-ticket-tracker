/*
    Full-text search setup for dbo.Tickets.

    Run AFTER `dotnet ef database update` has created the tables. Safe to re-run.
    After running it, restart the API: it checks for the index once at startup
    (see FullTextStatus.cs) and otherwise keeps using the LIKE fallback.

    Why this is a script and not an EF migration:
      - EF Core can't express full-text catalogs or indexes.
      - CREATE FULLTEXT INDEX can't run inside a transaction, and EF runs each
        migration inside one.
      - Full-text Search is an optional SQL Server component. This script checks
        for it and exits cleanly if it's missing; the app still works without it.

    What it creates:
      1. SearchText: a computed column  Title + ' ' + Description.
         Why not index Title and Description separately? With a column list,
         CONTAINS((Title, Description), '"login*" AND "timeout*"') requires ALL
         terms to be in the SAME column. A ticket titled "Login fails" whose
         description mentions "timeout" would not match. Indexing one combined
         column fixes that. It's PERSISTED (stored, not computed on every read),
         which costs extra storage: roughly a second copy of each ticket's text.
      2. A full-text catalog (a container for full-text indexes).
      3. The full-text index on SearchText, keyed on the primary key PK_Tickets
         (full-text requires a unique, non-null, single-column key; an int is ideal).
         CHANGE_TRACKING = AUTO: SQL Server updates the index in the background
           when tickets are inserted or edited. Note "background": a new ticket
           can take a few seconds to become searchable by keyword.
         STOPLIST = OFF: no "noise words" are ignored. With the default stoplist,
           words like "the" are dropped from the index, and a search containing
           one could return nothing at all. Small cost: a slightly larger index.

    Each DDL statement runs through EXEC(N'...') (dynamic SQL) on purpose.
    SQL Server compiles a whole batch before running any of it; a statement that
    mentions the SearchText column would fail to compile on the first run,
    because the column doesn't exist yet at compile time. Dynamic SQL is only
    compiled when it's reached.
*/
USE TicketTracker;
GO

IF FULLTEXTSERVICEPROPERTY('IsFullTextInstalled') = 0
BEGIN
    PRINT N'Full-Text Search is NOT installed on this SQL Server instance.';
    PRINT N'Nothing was changed. The API will use its LIKE fallback for keyword search.';
    RETURN;
END;

IF COL_LENGTH(N'dbo.Tickets', N'SearchText') IS NULL
BEGIN
    EXEC (N'ALTER TABLE dbo.Tickets ADD SearchText AS (Title + N'' '' + Description) PERSISTED;');
    PRINT N'Added computed column dbo.Tickets.SearchText.';
END;

IF NOT EXISTS (SELECT 1 FROM sys.fulltext_catalogs WHERE name = N'TicketTrackerCatalog')
BEGIN
    EXEC (N'CREATE FULLTEXT CATALOG TicketTrackerCatalog;');
    PRINT N'Created full-text catalog TicketTrackerCatalog.';
END;

IF NOT EXISTS (SELECT 1 FROM sys.fulltext_indexes WHERE object_id = OBJECT_ID(N'dbo.Tickets'))
BEGIN
    EXEC (N'CREATE FULLTEXT INDEX ON dbo.Tickets (SearchText LANGUAGE 1033)
            KEY INDEX PK_Tickets ON TicketTrackerCatalog
            WITH (CHANGE_TRACKING = AUTO, STOPLIST = OFF);');
    PRINT N'Created full-text index on dbo.Tickets(SearchText).';
END;

-- Check the result. Expect: FullTextInstalled = 1, TicketsHasActiveFullTextIndex = 1.
-- PopulateStatus 0 = idle (population finished), 1 = still populating existing rows.
SELECT
    FULLTEXTSERVICEPROPERTY('IsFullTextInstalled')                                   AS FullTextInstalled,
    OBJECTPROPERTYEX(OBJECT_ID(N'dbo.Tickets'), 'TableHasActiveFulltextIndex')        AS TicketsHasActiveFullTextIndex,
    FULLTEXTCATALOGPROPERTY(N'TicketTrackerCatalog', 'PopulateStatus')                 AS PopulateStatus;

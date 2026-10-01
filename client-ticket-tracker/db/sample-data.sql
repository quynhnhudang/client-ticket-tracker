/*
    Sample data: 6 clients and 10 tickets, spread across every status, with
    status history that matches each ticket's current status.

    Run AFTER the API has started once (it creates the first agent; every
    ticket needs an agent). Does nothing if tickets already exist.

    Some of the data is chosen to exercise search:
      - Two clients start with "Acme", so searching "acme" returns tickets for both.
      - "timeout" appears in two tickets' descriptions. Searching "login timeout"
        should find the SSO ticket: "login" is in its title and "timeout" only in
        its description (this is what the combined SearchText column is for).
      - One title contains "50%", to test that LIKE wildcards are escaped.

    Enum values (stored as ints; see Domain/Enums.cs):
      Status:   0 Open, 1 InProgress, 2 Escalated, 3 Resolved
      Priority: 0 Low,  1 Medium,     2 High,      3 Urgent
      Category: 0 Bug,  1 Question,   2 DataFix
*/
USE TicketTracker;
GO

SET NOCOUNT ON;
SET XACT_ABORT ON; -- any error rolls back the whole transaction, not just the failed statement

BEGIN TRANSACTION;

DECLARE @AgentId int = (SELECT TOP (1) Id FROM dbo.Agents ORDER BY Id);
IF @AgentId IS NULL
BEGIN
    ROLLBACK;
    RAISERROR(N'No agent exists yet. Start the API once with SeedAgent settings, then re-run.', 16, 1);
    RETURN;
END;

IF EXISTS (SELECT 1 FROM dbo.Tickets)
BEGIN
    ROLLBACK;
    PRINT N'Tickets already exist; sample data not inserted.';
    RETURN;
END;

-- All times in UTC (offset +00:00), matching what the API writes.
DECLARE @Now datetimeoffset = TODATETIMEOFFSET(SYSUTCDATETIME(), '+00:00');

INSERT INTO dbo.Clients (Name, Email, CreatedAt)
VALUES
    (N'Acme Logistics',            N'ops@acmelogistics.example',       DATEADD(day, -60, @Now)),
    (N'Acme Health Partners',      N'it@acmehealth.example',           DATEADD(day, -45, @Now)),
    (N'Brightwater Credit Union',  N'support@brightwater.example',     DATEADD(day, -30, @Now)),
    (N'Northwind Traders',         N'admin@northwind.example',         DATEADD(day, -90, @Now)),
    (N'Juniper School District',   N'helpdesk@juniperschools.example', DATEADD(day, -20, @Now)),
    (N'Contoso Retail',            N'ecommerce@contoso.example',       DATEADD(day, -75, @Now));

-- One row per ticket, including the final status and the text its history needs.
DECLARE @Seed TABLE (
    Title           nvarchar(200)  NOT NULL PRIMARY KEY, -- unique here, so we can join back on it
    Description     nvarchar(4000) NOT NULL,
    Category        int            NOT NULL,
    Priority        int            NOT NULL,
    Status          int            NOT NULL,
    ClientEmail     nvarchar(256)  NOT NULL,
    DaysAgo         int            NOT NULL,
    EscalationNote  nvarchar(1000) NULL,
    ResolutionNotes nvarchar(4000) NULL
);

INSERT INTO @Seed VALUES
(N'Login times out after SSO redirect',
 N'Users are sent back from the identity provider and then see a timeout error after about 30 seconds. Started after Monday''s release. Affects about half of the warehouse staff.',
 0, 2, 0, N'ops@acmelogistics.example', 1, NULL, NULL),

(N'Mobile app crashes on photo upload',
 N'Drivers on Android report the app closes when uploading delivery photos larger than 10 MB. Smaller photos upload fine.',
 0, 2, 0, N'ops@acmelogistics.example', 2, NULL, NULL),

(N'Weekly report dashboard loads slowly',
 N'The analytics team says the weekly dashboard takes over a minute to load and sometimes shows a gateway timeout. Other pages are fine.',
 0, 1, 0, N'it@acmehealth.example', 3, NULL, NULL),

(N'Invoice export CSV missing tax column',
 N'The monthly invoice export no longer includes the tax amount column. Finance needs it for month-end reconciliation.',
 0, 1, 1, N'admin@northwind.example', 4, NULL, NULL),

(N'Wrong student enrolled in course section',
 N'Student record 88213 was enrolled in Algebra II section 4 instead of section 2 during the bulk schedule import. Needs correcting before Friday.',
 2, 2, 1, N'helpdesk@juniperschools.example', 2, NULL, NULL),

(N'Password reset email never arrives',
 N'Members report that password reset emails are not delivered. They have checked spam folders. Affects all members since this morning.',
 0, 3, 2, N'support@brightwater.example', 1,
 N'Reproduced with a test account: no email is sent and there is no error in the UI. Looks like the mail queue is stuck. Needs someone with access to the mail service.', NULL),

(N'Duplicate customer records after import',
 N'The bulk import on 3 September created about 140 duplicate customer records with the same email address. They need merging without losing order history.',
 2, 2, 2, N'ecommerce@contoso.example', 5,
 N'Merging needs a script against production data; support does not have write access. List of duplicate emails attached in comments.', NULL),

(N'Discount shows 50% instead of 5%',
 N'A promo code configured as 5 percent off is applying 50% at checkout on the web store only.',
 0, 3, 0, N'ecommerce@contoso.example', 0, NULL, NULL),

(N'How do I add a second admin user?',
 N'The district office wants a second administrator for the account. Where is this configured?',
 1, 0, 3, N'helpdesk@juniperschools.example', 6, NULL,
 N'Walked the client through Settings > Users > Invite, with the Administrator role. Confirmed the new admin can log in.'),

(N'Change billing contact address',
 N'Please update the billing contact from the old finance manager to accounts@northwind.example.',
 2, 0, 3, N'admin@northwind.example', 8, NULL,
 N'Updated billing contact on the account record. Client confirmed the next invoice went to the new address.');

-- Tickets. Timeline for each ticket (relative to CreatedAt):
--   +0h created (Open), +2h InProgress, +6h Escalated, +1 day Resolved.
-- UpdatedAt is the time of the ticket's last status change.
INSERT INTO dbo.Tickets
    (Title, Description, Category, Priority, Status, ResolutionNotes,
     CreatedAt, UpdatedAt, ResolvedAt, ClientId, CreatedByAgentId)
SELECT
    s.Title, s.Description, s.Category, s.Priority, s.Status, s.ResolutionNotes,
    c.CreatedAtTicket,
    CASE s.Status
        WHEN 0 THEN c.CreatedAtTicket
        WHEN 1 THEN DATEADD(hour, 2, c.CreatedAtTicket)
        WHEN 2 THEN DATEADD(hour, 6, c.CreatedAtTicket)
        WHEN 3 THEN DATEADD(day, 1, c.CreatedAtTicket)
    END,
    CASE WHEN s.Status = 3 THEN DATEADD(day, 1, c.CreatedAtTicket) END,
    cl.Id,
    @AgentId
FROM @Seed AS s
INNER JOIN dbo.Clients AS cl ON cl.Email = s.ClientEmail
-- CROSS APPLY computes CreatedAt once per row so the expressions above can reuse it.
CROSS APPLY (SELECT DATEADD(hour, -3, DATEADD(day, -s.DaysAgo, @Now)) AS CreatedAtTicket) AS c;

-- History: every ticket was created as Open...
INSERT INTO dbo.TicketStatusHistory (TicketId, FromStatus, ToStatus, ChangedByAgentId, ChangedAt, Note)
SELECT t.Id, NULL, 0, @AgentId, t.CreatedAt, N'Ticket created'
FROM @Seed AS s INNER JOIN dbo.Tickets AS t ON t.Title = s.Title;

-- ...everything past Open was first picked up (Open -> InProgress)...
INSERT INTO dbo.TicketStatusHistory (TicketId, FromStatus, ToStatus, ChangedByAgentId, ChangedAt, Note)
SELECT t.Id, 0, 1, @AgentId, DATEADD(hour, 2, t.CreatedAt), NULL
FROM @Seed AS s INNER JOIN dbo.Tickets AS t ON t.Title = s.Title
WHERE s.Status IN (1, 2, 3);

-- ...escalated tickets then went InProgress -> Escalated, with the required note...
INSERT INTO dbo.TicketStatusHistory (TicketId, FromStatus, ToStatus, ChangedByAgentId, ChangedAt, Note)
SELECT t.Id, 1, 2, @AgentId, DATEADD(hour, 6, t.CreatedAt), s.EscalationNote
FROM @Seed AS s INNER JOIN dbo.Tickets AS t ON t.Title = s.Title
WHERE s.Status = 2;

-- ...and resolved tickets went InProgress -> Resolved.
INSERT INTO dbo.TicketStatusHistory (TicketId, FromStatus, ToStatus, ChangedByAgentId, ChangedAt, Note)
SELECT t.Id, 1, 3, @AgentId, t.ResolvedAt, NULL
FROM @Seed AS s INNER JOIN dbo.Tickets AS t ON t.Title = s.Title
WHERE s.Status = 3;

-- A few internal comments.
INSERT INTO dbo.TicketComments (TicketId, AuthorAgentId, Body, CreatedAt)
SELECT t.Id, @AgentId, v.Body, DATEADD(minute, v.MinutesAfter, t.CreatedAt)
FROM (VALUES
    (N'Duplicate customer records after import', N'Duplicate emails exported to a spreadsheet: 141 addresses, 283 records.', 200),
    (N'Duplicate customer records after import', N'Client confirmed no new orders should be placed on the duplicate accounts until merged.', 260),
    (N'Password reset email never arrives',      N'Status page for the mail provider shows no incident.', 150),
    (N'Invoice export CSV missing tax column',   N'Column was removed in the export template change on the 1st. Checking whether that was intentional.', 130)
) AS v(Title, Body, MinutesAfter)
INNER JOIN dbo.Tickets AS t ON t.Title = v.Title;

COMMIT;

PRINT N'Sample data inserted.';
SELECT Status, COUNT(*) AS Tickets FROM dbo.Tickets GROUP BY Status ORDER BY Status;

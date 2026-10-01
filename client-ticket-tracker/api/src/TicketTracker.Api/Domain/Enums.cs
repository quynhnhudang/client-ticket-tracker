namespace TicketTracker.Api.Domain;

// Enums are stored as integers (EF Core's default), not strings.
// Reason: the queue sorts by priority, and ORDER BY on an int column gives
// Urgent > High > Medium > Low. Stored as strings, it would sort alphabetically
// ("High" < "Low" < "Medium" < "Urgent"), which is wrong.
// The explicit numbers matter: they are what is in the database, so they must
// never be renumbered. CHECK constraints in AppDbContext stop out-of-range values.

public enum TicketStatus
{
    Open = 0,
    InProgress = 1,
    Escalated = 2,
    Resolved = 3,
}

public enum TicketPriority
{
    Low = 0,
    Medium = 1,
    High = 2,
    Urgent = 3,
}

public enum TicketCategory
{
    Bug = 0,
    Question = 1,
    DataFix = 2,
}

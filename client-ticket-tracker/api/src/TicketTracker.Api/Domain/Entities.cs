namespace TicketTracker.Api.Domain;

// Entities are plain classes. Column types, lengths and indexes are configured
// in AppDbContext (Fluent API) rather than with attributes here, so all schema
// decisions are in one place.
//
// Timestamps are DateTimeOffset (SQL type datetimeoffset), not DateTime.
// A DateTime read back from a datetime2 column has Kind = Unspecified, so it
// serialises to JSON without a "Z" and the browser treats it as local time.
// DateTimeOffset always carries its offset, so that bug cannot happen.

/// <summary>A support agent who can log in. No roles: every agent can do everything.</summary>
public class Agent
{
    public int Id { get; set; }
    public string Email { get; set; } = "";
    public string DisplayName { get; set; } = "";
    /// <summary>Output of ASP.NET Core's PasswordHasher (PBKDF2, salted). Never the password itself.</summary>
    public string PasswordHash { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>The customer who reported the issue. Identified by email.</summary>
public class Client
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    /// <summary>
    /// Stored trimmed and lower-cased. SQL Server's default collation is case-insensitive,
    /// so the unique index would already treat "A@x.com" and "a@x.com" as equal; normalising
    /// in code as well means that doesn't silently depend on the database's collation.
    /// </summary>
    public string Email { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; }

    public List<Ticket> Tickets { get; set; } = [];
}

public class Ticket
{
    public int Id { get; set; }
    public string Title { get; set; } = "";
    public string Description { get; set; } = "";
    public TicketCategory Category { get; set; }
    public TicketPriority Priority { get; set; }
    public TicketStatus Status { get; set; }

    /// <summary>Required before a ticket can move to Resolved.</summary>
    public string? ResolutionNotes { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public DateTimeOffset? ResolvedAt { get; set; }

    public int ClientId { get; set; }
    public Client Client { get; set; } = null!;

    public int CreatedByAgentId { get; set; }
    public Agent CreatedByAgent { get; set; } = null!;

    /// <summary>
    /// SQL Server rowversion: changes automatically on every UPDATE of the row.
    /// EF adds "WHERE RowVersion = @original" to its UPDATE statement, so if another
    /// request changed the ticket after we loaded it, zero rows match and EF throws
    /// DbUpdateConcurrencyException instead of silently overwriting their change.
    /// </summary>
    public byte[] RowVersion { get; set; } = [];

    public List<TicketComment> Comments { get; set; } = [];
    public List<TicketStatusHistory> StatusHistory { get; set; } = [];
}

/// <summary>An internal note by an agent. Clients never see these.</summary>
public class TicketComment
{
    public int Id { get; set; }
    public int TicketId { get; set; }
    public Ticket Ticket { get; set; } = null!;
    public int AuthorAgentId { get; set; }
    public Agent AuthorAgent { get; set; } = null!;
    public string Body { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>
/// Append-only audit trail. One row per status change, written in the same
/// SaveChanges (and therefore the same transaction) as the change itself.
/// </summary>
public class TicketStatusHistory
{
    public int Id { get; set; }
    public int TicketId { get; set; }
    public Ticket Ticket { get; set; } = null!;
    /// <summary>Null for the "ticket created" row.</summary>
    public TicketStatus? FromStatus { get; set; }
    public TicketStatus ToStatus { get; set; }
    public int ChangedByAgentId { get; set; }
    public Agent ChangedByAgent { get; set; } = null!;
    public DateTimeOffset ChangedAt { get; set; }
    /// <summary>Why the status changed. Required when escalating (it goes in the engineering email).</summary>
    public string? Note { get; set; }
}

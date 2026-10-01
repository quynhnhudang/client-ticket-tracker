using System.ComponentModel.DataAnnotations;
using TicketTracker.Api.Domain;

namespace TicketTracker.Api.Dtos;

// Request DTOs are separate from the entities so a client can only set the
// fields we intend. If the controller bound directly to Ticket, a caller could
// post "status": "Resolved" or "createdByAgentId": 7 (the "over-posting" or
// mass-assignment problem).
//
// Validation attributes run automatically because the controllers use
// [ApiController]; an invalid body gets a 400 with per-field errors before
// the action method runs.
//
// [EnumDataType] rejects numbers that aren't a defined value. Without it,
// "priority": 99 would bind happily to an undefined TicketPriority.
//
// Enum fields are nullable (TicketPriority?) on purpose. A non-nullable enum
// that is missing from the JSON silently becomes 0 (Low / Bug / Open), and
// [Required] can't tell that apart from a real choice. Nullable + [Required]
// turns "field missing" into a proper 400.

public sealed record LoginRequest
{
    [Required, EmailAddress, StringLength(256)]
    public string Email { get; init; } = "";

    [Required, StringLength(200)]
    public string Password { get; init; } = "";
}

public sealed record CreateTicketRequest
{
    [Required, StringLength(200, MinimumLength = 5)]
    public string Title { get; init; } = "";

    [Required, StringLength(4000, MinimumLength = 10)]
    public string Description { get; init; } = "";

    [Required, EnumDataType(typeof(TicketCategory))]
    public TicketCategory? Category { get; init; }

    [Required, EnumDataType(typeof(TicketPriority))]
    public TicketPriority? Priority { get; init; }

    [Required, StringLength(200, MinimumLength = 2)]
    public string ClientName { get; init; } = "";

    [Required, EmailAddress, StringLength(256)]
    public string ClientEmail { get; init; } = "";
}

public sealed record ChangeStatusRequest
{
    [Required, EnumDataType(typeof(TicketStatus))]
    public TicketStatus? NewStatus { get; init; }

    /// <summary>Why the status is changing. Required for Escalated (checked in TicketStatusRules).</summary>
    [StringLength(1000)]
    public string? Note { get; init; }

    /// <summary>Required for Resolved (checked in TicketStatusRules).</summary>
    [StringLength(4000)]
    public string? ResolutionNotes { get; init; }
}

public sealed record AddCommentRequest
{
    [Required, StringLength(4000, MinimumLength = 1)]
    public string Body { get; init; } = "";
}

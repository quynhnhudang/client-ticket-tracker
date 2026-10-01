using TicketTracker.Api.Domain;

namespace TicketTracker.Api.Dtos;

// Response DTOs: entities never leave the API. That keeps the JSON shape
// independent of the table shape, never leaks PasswordHash or RowVersion,
// and avoids serialising navigation-property cycles (Ticket -> Client -> Tickets -> ...).

public sealed record AgentDto(int Id, string Email, string DisplayName);

public sealed record ClientDto(int Id, string Name, string Email);

public sealed record TicketSummaryDto(
    int Id,
    string Title,
    TicketStatus Status,
    TicketPriority Priority,
    TicketCategory Category,
    string ClientName,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record CommentDto(int Id, string Body, string AuthorName, DateTimeOffset CreatedAt);

public sealed record StatusHistoryDto(
    int Id,
    TicketStatus? FromStatus,
    TicketStatus ToStatus,
    string ChangedBy,
    DateTimeOffset ChangedAt,
    string? Note);

public sealed record TicketDetailDto(
    int Id,
    string Title,
    string Description,
    TicketCategory Category,
    TicketPriority Priority,
    TicketStatus Status,
    string? ResolutionNotes,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? ResolvedAt,
    ClientDto Client,
    string CreatedBy,
    // Computed from TicketStatusRules so the UI only offers valid buttons.
    IReadOnlyList<TicketStatus> AllowedNextStatuses,
    IReadOnlyList<CommentDto> Comments,
    IReadOnlyList<StatusHistoryDto> History);

public sealed record PagedResult<T>(IReadOnlyList<T> Items, int Page, int PageSize, int TotalCount);

/// <summary>MatchedOn is "id", "client" or "keyword", so the UI can show why a row matched.</summary>
public sealed record SearchHitDto(
    int Id,
    string Title,
    TicketStatus Status,
    TicketPriority Priority,
    TicketCategory Category,
    string ClientName,
    DateTimeOffset CreatedAt,
    string MatchedOn);

/// <summary>Mode is "fulltext" or "like-fallback", so you can see which keyword query actually ran.</summary>
public sealed record SearchResultDto(string Query, string Mode, IReadOnlyList<SearchHitDto> Items);

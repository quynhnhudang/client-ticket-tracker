namespace TicketTracker.Api.Domain;

/// <summary>
/// The ticket lifecycle as a table of allowed transitions.
///
///   Open ──────► InProgress ──► Escalated ──► Resolved
///     │  └──────────────────────►   ▲  │         │
///     │                             │  └► InProgress (engineering hands it back)
///     └──────────────────────────────────────────►│
///   Resolved ──► Open  (reopen)
///
/// It is a pure static class (no database, no DI) so the rules can be unit
/// tested directly, and so the API can send the allowed next states to the UI.
/// That way the UI never has its own copy of the rules to get out of sync.
/// </summary>
public static class TicketStatusRules
{
    private static readonly Dictionary<TicketStatus, TicketStatus[]> Allowed = new()
    {
        [TicketStatus.Open] = [TicketStatus.InProgress, TicketStatus.Escalated, TicketStatus.Resolved],
        [TicketStatus.InProgress] = [TicketStatus.Escalated, TicketStatus.Resolved],
        [TicketStatus.Escalated] = [TicketStatus.InProgress, TicketStatus.Resolved],
        [TicketStatus.Resolved] = [TicketStatus.Open],
    };

    public static IReadOnlyList<TicketStatus> AllowedNext(TicketStatus from) =>
        Allowed.TryGetValue(from, out var next) ? next : [];

    /// <summary>Returns null if the change is valid, otherwise a message suitable for the API response.</summary>
    public static string? Validate(TicketStatus from, TicketStatus to, string? note, string? resolutionNotes)
    {
        if (from == to)
            return $"Ticket is already {to}.";

        if (!AllowedNext(from).Contains(to))
            return $"Cannot move a ticket from {from} to {to}. Allowed: {string.Join(", ", AllowedNext(from))}.";

        if (to == TicketStatus.Escalated && string.IsNullOrWhiteSpace(note))
            return "A note explaining the escalation is required; it is sent to engineering.";

        if (to == TicketStatus.Resolved && string.IsNullOrWhiteSpace(resolutionNotes))
            return "Resolution notes are required to resolve a ticket.";

        return null;
    }
}

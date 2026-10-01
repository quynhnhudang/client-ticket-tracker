namespace TicketTracker.Api.Services;

/// <summary>
/// Called after a ticket has been escalated and the change is committed.
///
/// Two implementations, chosen in Program.cs:
///   - EmailEscalationNotifier (Services/Email) when Email:Host is configured:
///     builds and sends the HTML email to engineering.
///   - LoggingEscalationNotifier otherwise: writes a log line only, so the app
///     runs without an SMTP account.
/// TicketService only knows about this interface, so it didn't change when email was added.
/// </summary>
public interface IEscalationNotifier
{
    Task NotifyEscalatedAsync(int ticketId, string escalationNote, CancellationToken ct);
}

public sealed class LoggingEscalationNotifier(ILogger<LoggingEscalationNotifier> logger) : IEscalationNotifier
{
    public Task NotifyEscalatedAsync(int ticketId, string escalationNote, CancellationToken ct)
    {
        logger.LogWarning(
            "Ticket {TicketId} escalated to engineering (email is off: Email:Host not configured). Note: {Note}",
            ticketId, escalationNote);
        return Task.CompletedTask;
    }
}

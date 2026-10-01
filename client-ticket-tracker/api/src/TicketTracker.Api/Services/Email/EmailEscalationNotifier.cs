using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using TicketTracker.Api.Data;
using TicketTracker.Api.Domain;

namespace TicketTracker.Api.Services.Email;

/// <summary>
/// The Stage 3 implementation of IEscalationNotifier: loads what the email
/// needs, builds it, and sends it to engineering.
///
/// It reads AppDbContext directly rather than calling TicketService, because
/// TicketService depends on IEscalationNotifier: going through TicketService
/// would be a circular dependency the DI container can't construct.
/// </summary>
public sealed class EmailEscalationNotifier(
    AppDbContext db,
    IEmailSender sender,
    IOptions<EmailOptions> options) : IEscalationNotifier
{
    public async Task NotifyEscalatedAsync(int ticketId, string escalationNote, CancellationToken ct)
    {
        var t = await db.Tickets.AsNoTracking()
            .Where(x => x.Id == ticketId)
            .Select(x => new
            {
                x.Id, x.Title, x.Description, x.Priority, x.Category, x.CreatedAt,
                ClientName = x.Client.Name,
                ClientEmail = x.Client.Email,
                // The escalation that just happened is the newest "-> Escalated" history row.
                Escalation = x.StatusHistory
                    .Where(h => h.ToStatus == TicketStatus.Escalated)
                    .OrderByDescending(h => h.ChangedAt).ThenByDescending(h => h.Id)
                    .Select(h => new { h.ChangedAt, By = h.ChangedByAgent.DisplayName })
                    .FirstOrDefault(),
            })
            .SingleOrDefaultAsync(ct)
            ?? throw new InvalidOperationException($"Ticket {ticketId} not found for escalation email.");

        var o = options.Value;
        var model = new EscalationEmailModel(
            TicketId: t.Id,
            Title: t.Title,
            Description: t.Description,
            ClientName: t.ClientName,
            ClientEmail: t.ClientEmail,
            Priority: t.Priority.ToString(),
            Category: t.Category == TicketCategory.DataFix ? "Data fix" : t.Category.ToString(),
            EscalatedBy: t.Escalation?.By ?? "Support",
            EscalatedAt: t.Escalation?.ChangedAt ?? DateTimeOffset.UtcNow,
            CreatedAt: t.CreatedAt,
            EscalationNote: escalationNote,
            TicketUrl: $"{o.AppBaseUrl.TrimEnd('/')}/tickets/{t.Id.ToString(CultureInfo.InvariantCulture)}");

        await sender.SendAsync(o.EngineeringAddressList, EscalationEmailBuilder.Build(model), ct);
    }
}

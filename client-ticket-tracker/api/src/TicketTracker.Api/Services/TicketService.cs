using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using TicketTracker.Api.Data;
using TicketTracker.Api.Domain;
using TicketTracker.Api.Dtos;

namespace TicketTracker.Api.Services;

/// <summary>
/// All ticket reads and writes except search. Every status change goes through
/// ChangeStatusAsync, so there is exactly one place that enforces the rules,
/// writes the history row and triggers the escalation notification.
/// </summary>
public sealed class TicketService(
    AppDbContext db,
    IEscalationNotifier escalationNotifier,
    ILogger<TicketService> logger)
{
    public const int MaxPageSize = 100;

    public async Task<PagedResult<TicketSummaryDto>> ListAsync(
        TicketStatus? status, TicketPriority? priority, int page, int pageSize, CancellationToken ct)
    {
        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, MaxPageSize);

        // AsNoTracking: read-only query, so skip EF's change-tracking bookkeeping.
        var query = db.Tickets.AsNoTracking();
        if (status is not null)
            query = query.Where(t => t.Status == status.Value);
        if (priority is not null)
            query = query.Where(t => t.Priority == priority.Value);

        var total = await query.CountAsync(ct);

        // ORDER BY matches IX_Tickets_Queue (Status, Priority DESC, CreatedAt), so
        // with a status filter SQL Server reads the index in order with no sort step.
        // ThenBy(Id) makes the order unique; without a tie-breaker, two tickets
        // created in the same instant could swap places between pages.
        // (Id is free here: SQL Server appends the clustered key to every
        // non-unique nonclustered index, so the index is already sorted by it.)
        var items = await query
            .OrderByDescending(t => t.Priority)
            .ThenBy(t => t.CreatedAt)
            .ThenBy(t => t.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            // Projecting straight into the DTO means EF selects only these columns
            // (plus a JOIN for the client name) instead of loading full entities.
            .Select(t => new TicketSummaryDto(
                t.Id, t.Title, t.Status, t.Priority, t.Category, t.Client.Name, t.CreatedAt, t.UpdatedAt))
            .ToListAsync(ct);

        return new PagedResult<TicketSummaryDto>(items, page, pageSize, total);
    }

    public async Task<TicketDetailDto?> GetDetailAsync(int id, CancellationToken ct)
    {
        var row = await db.Tickets.AsNoTracking()
            .Where(t => t.Id == id)
            .Select(t => new
            {
                t.Id, t.Title, t.Description, t.Category, t.Priority, t.Status, t.ResolutionNotes,
                t.CreatedAt, t.UpdatedAt, t.ResolvedAt,
                Client = new ClientDto(t.Client.Id, t.Client.Name, t.Client.Email),
                CreatedBy = t.CreatedByAgent.DisplayName,
                Comments = t.Comments
                    .OrderBy(c => c.CreatedAt).ThenBy(c => c.Id)
                    .Select(c => new CommentDto(c.Id, c.Body, c.AuthorAgent.DisplayName, c.CreatedAt))
                    .ToList(),
                History = t.StatusHistory
                    .OrderBy(h => h.ChangedAt).ThenBy(h => h.Id)
                    .Select(h => new StatusHistoryDto(
                        h.Id, h.FromStatus, h.ToStatus, h.ChangedByAgent.DisplayName, h.ChangedAt, h.Note))
                    .ToList(),
            })
            // Two child collections in one SQL query would JOIN both onto the ticket,
            // returning comments × history rows (10 comments and 8 history rows = 80
            // rows for one ticket). A split query runs one SELECT per collection instead.
            .AsSplitQuery()
            .SingleOrDefaultAsync(ct);

        if (row is null)
            return null;

        return new TicketDetailDto(
            row.Id, row.Title, row.Description, row.Category, row.Priority, row.Status, row.ResolutionNotes,
            row.CreatedAt, row.UpdatedAt, row.ResolvedAt, row.Client, row.CreatedBy,
            TicketStatusRules.AllowedNext(row.Status), row.Comments, row.History);
    }

    public async Task<int> CreateAsync(CreateTicketRequest request, int agentId, CancellationToken ct)
    {
        var email = request.ClientEmail.Trim().ToLowerInvariant();

        // Find-or-create the client, then insert the ticket and its first history row.
        // Everything is saved by ONE SaveChangesAsync call, and EF wraps a single
        // SaveChanges in a transaction automatically: either all rows are written or none.
        //
        // The retry handles a race: two requests for the same *new* client can both
        // find no existing row, and both try to INSERT it. The unique index on
        // Clients.Email rejects the second insert; that request then starts over,
        // finds the client the first request created, and succeeds.
        for (var attempt = 1; ; attempt++)
        {
            var now = DateTimeOffset.UtcNow;
            var client = await db.Clients.SingleOrDefaultAsync(c => c.Email == email, ct)
                         ?? new Client { Name = request.ClientName.Trim(), Email = email, CreatedAt = now };

            var ticket = new Ticket
            {
                Title = request.Title.Trim(),
                Description = request.Description.Trim(),
                Category = request.Category!.Value,
                Priority = request.Priority!.Value,
                Status = TicketStatus.Open,
                Client = client,
                CreatedByAgentId = agentId,
                CreatedAt = now,
                UpdatedAt = now,
                StatusHistory =
                {
                    new TicketStatusHistory
                    {
                        FromStatus = null,
                        ToStatus = TicketStatus.Open,
                        ChangedByAgentId = agentId,
                        ChangedAt = now,
                        Note = "Ticket created",
                    },
                },
            };
            db.Tickets.Add(ticket);

            try
            {
                await db.SaveChangesAsync(ct);
                return ticket.Id;
            }
            catch (DbUpdateException ex) when (attempt == 1 && IsUniqueKeyViolation(ex))
            {
                logger.LogInformation("Client {Email} was created concurrently; retrying with the existing row.", email);
                db.ChangeTracker.Clear(); // forget the failed inserts before trying again
            }
        }
    }

    public async Task<ServiceResult<TicketDetailDto>> ChangeStatusAsync(
        int id, ChangeStatusRequest request, int agentId, CancellationToken ct)
    {
        // Tracked (no AsNoTracking) because we're going to modify it.
        var ticket = await db.Tickets.SingleOrDefaultAsync(t => t.Id == id, ct);
        if (ticket is null)
            return ServiceResult<TicketDetailDto>.Fail(ServiceError.NotFound, $"Ticket {id} not found.");

        var from = ticket.Status;
        var to = request.NewStatus!.Value;
        var note = string.IsNullOrWhiteSpace(request.Note) ? null : request.Note.Trim();

        var error = TicketStatusRules.Validate(from, to, note, request.ResolutionNotes);
        if (error is not null)
            return ServiceResult<TicketDetailDto>.Fail(ServiceError.Invalid, error);

        var now = DateTimeOffset.UtcNow;
        ticket.Status = to;
        ticket.UpdatedAt = now;

        if (to == TicketStatus.Resolved)
        {
            ticket.ResolutionNotes = request.ResolutionNotes!.Trim();
            ticket.ResolvedAt = now;
        }
        else if (from == TicketStatus.Resolved)
        {
            // Reopened. ResolutionNotes are kept so the failed fix stays visible;
            // they are overwritten when the ticket is resolved again.
            ticket.ResolvedAt = null;
        }

        db.TicketStatusHistory.Add(new TicketStatusHistory
        {
            TicketId = ticket.Id,
            FromStatus = from,
            ToStatus = to,
            ChangedByAgentId = agentId,
            ChangedAt = now,
            Note = note,
        });

        try
        {
            // One SaveChanges = one transaction: the ticket UPDATE and the history
            // INSERT both commit, or neither does. History can't drift from reality.
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            // The RowVersion check failed: another request changed this ticket
            // between our SELECT and our UPDATE. Rather than overwrite their change
            // (the "lost update" problem), refuse and let the agent reload.
            return ServiceResult<TicketDetailDto>.Fail(
                ServiceError.Conflict,
                "This ticket was changed by someone else just now. Reload it and try again.");
        }

        if (to == TicketStatus.Escalated)
        {
            // Runs only AFTER the commit, so engineering is never emailed about an
            // escalation that was rolled back.
            // CancellationToken.None: the escalation already happened; if the browser
            // disconnects now, the notification should still go out.
            // A failure here is logged, not returned: the ticket IS escalated, and
            // reporting an error would make the agent think it wasn't.
            // (Known gap: no automatic retry. See docs/DECISIONS.md, "outbox".)
            try
            {
                await escalationNotifier.NotifyEscalatedAsync(ticket.Id, note!, CancellationToken.None);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Ticket {TicketId} was escalated but the notification failed.", ticket.Id);
            }
        }

        var detail = await GetDetailAsync(id, ct);
        return ServiceResult<TicketDetailDto>.Ok(detail!);
    }

    public async Task<ServiceResult<CommentDto>> AddCommentAsync(
        int ticketId, AddCommentRequest request, int agentId, CancellationToken ct)
    {
        if (!await db.Tickets.AnyAsync(t => t.Id == ticketId, ct))
            return ServiceResult<CommentDto>.Fail(ServiceError.NotFound, $"Ticket {ticketId} not found.");

        var comment = new TicketComment
        {
            TicketId = ticketId,
            AuthorAgentId = agentId,
            Body = request.Body.Trim(),
            CreatedAt = DateTimeOffset.UtcNow,
        };
        db.TicketComments.Add(comment);
        await db.SaveChangesAsync(ct);

        var author = await db.Agents.Where(a => a.Id == agentId).Select(a => a.DisplayName).SingleAsync(ct);
        return ServiceResult<CommentDto>.Ok(new CommentDto(comment.Id, comment.Body, author, comment.CreatedAt));
    }

    // SQL Server error 2601: duplicate key in a unique index.
    // SQL Server error 2627: violation of a UNIQUE or PRIMARY KEY constraint.
    private static bool IsUniqueKeyViolation(DbUpdateException ex) =>
        ex.InnerException is SqlException { Number: 2601 or 2627 };
}

using Microsoft.AspNetCore.Mvc;
using TicketTracker.Api.Data.Queries;
using TicketTracker.Api.Domain;
using TicketTracker.Api.Dtos;
using TicketTracker.Api.Services;

namespace TicketTracker.Api.Controllers;

// No [Authorize] needed: Program.cs sets a fallback policy that requires a
// logged-in agent for every endpoint unless it's marked [AllowAnonymous].
// Secure by default: a new endpoint can't be left unprotected by accident.
[Route("api/tickets")]
public sealed class TicketsController(TicketService tickets, TicketSearchQuery search) : ApiControllerBase
{
    /// <summary>GET /api/tickets?status=Open&amp;priority=High&amp;page=1&amp;pageSize=25: the queue.</summary>
    [HttpGet]
    public Task<PagedResult<TicketSummaryDto>> List(
        [FromQuery] TicketStatus? status,
        [FromQuery] TicketPriority? priority,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 25,
        CancellationToken ct = default) =>
        tickets.ListAsync(status, priority, page, pageSize, ct);

    /// <summary>GET /api/tickets/search?q=acme: ticket ID, client-name prefix, or keywords.</summary>
    [HttpGet("search")]
    public async Task<ActionResult<SearchResultDto>> Search([FromQuery] string? q, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(q))
            return Problem(statusCode: StatusCodes.Status400BadRequest, title: "Query 'q' is required.");
        if (q.Length > SearchInput.MaxQueryLength)
            return Problem(statusCode: StatusCodes.Status400BadRequest,
                title: $"Query must be at most {SearchInput.MaxQueryLength} characters.");

        return await search.SearchAsync(q, ct);
    }

    /// <summary>GET /api/tickets/42: full detail with comments and status history.</summary>
    // {id:int} is a route constraint: "/api/tickets/search" can never be mistaken for an ID.
    [HttpGet("{id:int}")]
    public async Task<ActionResult<TicketDetailDto>> Get(int id, CancellationToken ct)
    {
        var detail = await tickets.GetDetailAsync(id, ct);
        return detail is null
            ? Problem(statusCode: StatusCodes.Status404NotFound, title: $"Ticket {id} not found.")
            : detail;
    }

    /// <summary>POST /api/tickets: intake. Returns 201 with a Location header pointing at the new ticket.</summary>
    [HttpPost]
    public async Task<ActionResult<TicketDetailDto>> Create(CreateTicketRequest request, CancellationToken ct)
    {
        var id = await tickets.CreateAsync(request, CurrentAgentId, ct);
        var detail = await tickets.GetDetailAsync(id, ct);
        return CreatedAtAction(nameof(Get), new { id }, detail);
    }

    /// <summary>
    /// POST /api/tickets/42/status  { "newStatus": "Escalated", "note": "..." }
    /// A POST to a sub-resource rather than PATCHing a "status" field, because a
    /// status change is an action with side effects (a history row, possibly an
    /// email), not just a field update. It also means there is no generic
    /// "update ticket" endpoint that could change status while skipping the rules.
    /// </summary>
    [HttpPost("{id:int}/status")]
    public async Task<ActionResult<TicketDetailDto>> ChangeStatus(int id, ChangeStatusRequest request, CancellationToken ct)
    {
        var result = await tickets.ChangeStatusAsync(id, request, CurrentAgentId, ct);
        return result.Error is { } error ? FromError(error, result.Message) : result.Value!;
    }

    /// <summary>POST /api/tickets/42/comments  { "body": "..." }: add an internal comment.</summary>
    [HttpPost("{id:int}/comments")]
    public async Task<ActionResult<CommentDto>> AddComment(int id, AddCommentRequest request, CancellationToken ct)
    {
        var result = await tickets.AddCommentAsync(id, request, CurrentAgentId, ct);
        return result.Error is { } error
            ? FromError(error, result.Message)
            : StatusCode(StatusCodes.Status201Created, result.Value);
    }
}

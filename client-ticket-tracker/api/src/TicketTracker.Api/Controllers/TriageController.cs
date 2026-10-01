using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using TicketTracker.Api.Services.Triage;

namespace TicketTracker.Api.Controllers;

public sealed record TriageRequest
{
    [Required, StringLength(200, MinimumLength = 5)]
    public string Title { get; init; } = "";

    [Required, StringLength(4000, MinimumLength = 10)]
    public string Description { get; init; } = "";
}

/// <summary>Optional LLM triage. See TriageService for the design rules.</summary>
[Route("api/triage")]
public sealed class TriageController(TriageService triage) : ApiControllerBase
{
    /// <summary>GET /api/triage/status: lets the UI hide the button when the feature is off.</summary>
    [HttpGet("status")]
    public object Status() => new { enabled = triage.Enabled };

    /// <summary>POST /api/triage/suggest { title, description } -> { category, priority, reason }</summary>
    [HttpPost("suggest")]
    [EnableRateLimiting(RateLimitPolicies.Triage)] // each call costs money; cap it per agent
    public async Task<ActionResult<TriageSuggestion>> Suggest(TriageRequest request, CancellationToken ct)
    {
        if (!triage.Enabled)
            return Problem(statusCode: StatusCodes.Status404NotFound, title: "Triage suggestions are not configured.");

        var suggestion = await triage.SuggestAsync(request.Title, request.Description, ct);

        // 502 Bad Gateway: our server is fine, but the upstream service it depends on failed.
        return suggestion is null
            ? Problem(statusCode: StatusCodes.Status502BadGateway, title: "The suggestion service didn't return a usable answer.")
            : suggestion;
    }
}

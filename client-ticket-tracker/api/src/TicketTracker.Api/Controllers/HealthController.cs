using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TicketTracker.Api.Data;

namespace TicketTracker.Api.Controllers;

/// <summary>
/// GET /api/health: anonymous "is the app up and can it reach the database?"
/// Used during deployment (curl it after each step) and by the deploy script.
/// Returns 503 if the database is unreachable, so a script can tell the difference.
/// </summary>
[Route("api/health")]
public sealed class HealthController(AppDbContext db) : ApiControllerBase
{
    [HttpGet]
    [AllowAnonymous]
    public async Task<IActionResult> Get(CancellationToken ct)
    {
        var dbOk = await db.Database.CanConnectAsync(ct);
        var body = new { status = dbOk ? "ok" : "degraded", database = dbOk ? "reachable" : "unreachable" };
        return dbOk ? Ok(body) : StatusCode(StatusCodes.Status503ServiceUnavailable, body);
    }
}

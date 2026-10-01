using System.Globalization;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using TicketTracker.Api.Data;
using TicketTracker.Api.Domain;
using TicketTracker.Api.Dtos;

namespace TicketTracker.Api.Controllers;

[Route("api/auth")]
public sealed class AuthController(AppDbContext db, IPasswordHasher<Agent> hasher) : ApiControllerBase
{
    // A real hash of a throwaway password, computed once. Used when the email
    // doesn't exist so that the request still does a full hash verification.
    private static readonly Lazy<string> DummyHash =
        new(() => new PasswordHasher<Agent>().HashPassword(new Agent(), Guid.NewGuid().ToString()));

    /// <summary>
    /// POST /api/auth/login  { "email": "...", "password": "..." }
    /// On success the response sets an HttpOnly cookie; the browser sends it on
    /// every later request automatically. JavaScript can't read it, so an XSS
    /// bug can't steal the session the way it could steal a token in localStorage.
    /// </summary>
    [HttpPost("login")]
    [AllowAnonymous]
    [EnableRateLimiting(RateLimitPolicies.Login)] // 5 attempts per minute per IP, see Program.cs
    public async Task<ActionResult<AgentDto>> Login(LoginRequest request, CancellationToken ct)
    {
        var email = request.Email.Trim().ToLowerInvariant();
        var agent = await db.Agents.AsNoTracking().SingleOrDefaultAsync(a => a.Email == email, ct);

        // Verify against a dummy hash when the email is unknown. PBKDF2 is slow on
        // purpose (~tens of ms); if we returned immediately for unknown emails, the
        // response time would reveal which emails have accounts.
        var result = hasher.VerifyHashedPassword(agent ?? new Agent(), agent?.PasswordHash ?? DummyHash.Value, request.Password);

        // Same message for "no such email" and "wrong password", for the same reason.
        if (agent is null || result == PasswordVerificationResult.Failed)
            return Problem(statusCode: StatusCodes.Status401Unauthorized, title: "Invalid email or password.");

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, agent.Id.ToString(CultureInfo.InvariantCulture)),
            new(ClaimTypes.Name, agent.DisplayName),
            new(ClaimTypes.Email, agent.Email),
        };
        var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
        await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(identity));

        return new AgentDto(agent.Id, agent.Email, agent.DisplayName);
    }

    /// <summary>POST /api/auth/logout: deletes the cookie.</summary>
    [HttpPost("logout")]
    public async Task<IActionResult> Logout()
    {
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return NoContent();
    }

    /// <summary>
    /// GET /api/auth/me: who am I? 401 if not logged in. The Angular app calls this
    /// on startup to decide whether to show the login page.
    /// </summary>
    [HttpGet("me")]
    public ActionResult<AgentDto> Me() =>
        new AgentDto(
            CurrentAgentId,
            User.FindFirstValue(ClaimTypes.Email) ?? "",
            User.FindFirstValue(ClaimTypes.Name) ?? "");
}

public static class RateLimitPolicies
{
    public const string Login = "login";
    public const string Triage = "triage";
}

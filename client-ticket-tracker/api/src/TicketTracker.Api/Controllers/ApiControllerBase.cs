using System.Globalization;
using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using TicketTracker.Api.Services;

namespace TicketTracker.Api.Controllers;

/// <summary>
/// [ApiController] turns on automatic 400s for invalid request bodies,
/// binds complex parameters from the JSON body, and returns errors as
/// RFC 9457 "problem details" JSON.
/// </summary>
[ApiController]
public abstract class ApiControllerBase : ControllerBase
{
    /// <summary>
    /// The logged-in agent's ID, read from the auth cookie's claims (set in AuthController.Login).
    /// Never taken from the request body or URL, so an agent can't act as someone else.
    /// </summary>
    protected int CurrentAgentId =>
        int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!, CultureInfo.InvariantCulture);

    /// <summary>Maps an expected service failure to its HTTP status code.</summary>
    protected ObjectResult FromError(ServiceError error, string? message) => error switch
    {
        ServiceError.NotFound => Problem(statusCode: StatusCodes.Status404NotFound, title: message),
        ServiceError.Invalid => Problem(statusCode: StatusCodes.Status400BadRequest, title: message),
        ServiceError.Conflict => Problem(statusCode: StatusCodes.Status409Conflict, title: message),
        _ => Problem(statusCode: StatusCodes.Status500InternalServerError),
    };
}

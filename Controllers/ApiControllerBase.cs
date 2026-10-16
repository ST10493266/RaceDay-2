using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using RaceDay.Api.Models;

namespace RaceDay.Api.Controllers;

/// <summary>Shared helpers for all RaceDay controllers.</summary>
public abstract class ApiControllerBase : ControllerBase
{
    /// <summary>Id of the logged-in user, taken from the session identity (only call from [Authorize] actions).</summary>
    protected int CurrentUserId => int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    /// <summary>Returns a JSON problem response with the given status code.</summary>
    protected ObjectResult Fail(int statusCode, string title) => Problem(statusCode: statusCode, title: title);

    /// <summary>
    /// Ownership rule: only the Organiser who created an event may manage it.
    /// Returns null when access is allowed, otherwise the 404/403 response to return.
    /// </summary>
    protected ActionResult? EnsureOwner(Event? ev)
    {
        if (ev is null) return Fail(StatusCodes.Status404NotFound, "Event not found.");
        if (ev.OrganiserId != CurrentUserId)
            return Fail(StatusCodes.Status403Forbidden, "You can only manage events that you created.");
        return null;
    }
}

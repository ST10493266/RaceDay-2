using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RaceDay.Api.Data;
using RaceDay.Api.Dtos;
using RaceDay.Api.Models;
using RaceDay.Api.Services;

namespace RaceDay.Api.Controllers;

[ApiController]
[Route("api/auth")]
public class AuthController : ApiControllerBase
{
    private readonly RaceDayDbContext _db;

    public AuthController(RaceDayDbContext db) => _db = db;

    /// <summary>Register a new account as an Organiser or a Participant.</summary>
    [HttpPost("register")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(UserResponse), StatusCodes.Status201Created)]
    public async Task<ActionResult<UserResponse>> Register(RegisterRequest request)
    {
        // Only roles that exist in the system can be chosen (no free-text roles, no "Admin").
        var roleName = RoleNames.All.FirstOrDefault(r =>
            string.Equals(r, request.Role?.Trim(), StringComparison.OrdinalIgnoreCase));
        if (roleName is null)
        {
            ModelState.AddModelError(nameof(request.Role), "Role must be either 'Organiser' or 'Participant'.");
            return ValidationProblem(ModelState);
        }

        if (request.DateOfBirth is { } dob && dob.Date > DateTime.UtcNow.Date)
        {
            ModelState.AddModelError(nameof(request.DateOfBirth), "Date of birth cannot be in the future.");
            return ValidationProblem(ModelState);
        }

        var role = await _db.Roles.SingleOrDefaultAsync(r => r.RoleName == roleName);
        if (role is null)
            return Fail(StatusCodes.Status500InternalServerError, "The requested role has not been set up in the database.");

        var email = request.Email.Trim().ToLowerInvariant();
        if (await _db.Users.AnyAsync(u => u.Email == email))
            return Fail(StatusCodes.Status409Conflict, "An account with this email address already exists.");

        var user = new AppUser
        {
            FirstName = request.FirstName.Trim(),
            LastName = request.LastName.Trim(),
            Email = email,
            PasswordHash = PasswordService.Hash(request.Password), // never store the plain password
            PhoneNumber = string.IsNullOrWhiteSpace(request.PhoneNumber) ? null : request.PhoneNumber.Trim(),
            DateOfBirth = request.DateOfBirth?.Date,
            Role = role
        };

        _db.Users.Add(user);
        try
        {
            await _db.SaveChangesAsync();
        }
        catch (DbUpdateException)
        {
            // Two simultaneous registrations with the same email: the unique index catches the second one.
            return Fail(StatusCodes.Status409Conflict, "An account with this email address already exists.");
        }

        return StatusCode(StatusCodes.Status201Created, UserResponse.From(user));
    }

    /// <summary>Log in. On success a session cookie is issued that carries the user's id and role.</summary>
    [HttpPost("login")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(UserResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<UserResponse>> Login(LoginRequest request)
    {
        var email = request.Email.Trim().ToLowerInvariant();
        var user = await _db.Users.Include(u => u.Role).SingleOrDefaultAsync(u => u.Email == email);

        // Same message for "unknown email" and "wrong password" so accounts cannot be probed.
        if (!PasswordService.Verify(request.Password, user?.PasswordHash) || user is null)
            return Fail(StatusCodes.Status401Unauthorized, "Invalid email or password.");

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, user.UserId.ToString()),
            new(ClaimTypes.Name, $"{user.FirstName} {user.LastName}"),
            new(ClaimTypes.Email, user.Email),
            new(ClaimTypes.Role, user.Role.RoleName)
        };
        var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);

        await HttpContext.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme,
            new ClaimsPrincipal(identity),
            new AuthenticationProperties { IsPersistent = false });

        return UserResponse.From(user);
    }

    /// <summary>Log out (ends the session).</summary>
    [HttpPost("logout")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Logout()
    {
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return NoContent();
    }
}

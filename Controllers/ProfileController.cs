using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RaceDay.Api.Data;
using RaceDay.Api.Dtos;

namespace RaceDay.Api.Controllers;

[ApiController]
[Route("api/profile")]
[Authorize] // both roles
public class ProfileController : ApiControllerBase
{
    private readonly RaceDayDbContext _db;

    public ProfileController(RaceDayDbContext db) => _db = db;

    /// <summary>View my own profile.</summary>
    [HttpGet]
    public async Task<ActionResult<UserResponse>> Get()
    {
        var user = await _db.Users.AsNoTracking().Include(u => u.Role)
            .SingleOrDefaultAsync(u => u.UserId == CurrentUserId);
        if (user is null) return Fail(StatusCodes.Status404NotFound, "User not found.");
        return UserResponse.From(user);
    }

    /// <summary>Update my own profile. Email and role cannot be changed here.</summary>
    [HttpPut]
    public async Task<ActionResult<UserResponse>> Update(UpdateProfileRequest request)
    {
        if (request.DateOfBirth is { } dob && dob.Date > DateTime.UtcNow.Date)
        {
            ModelState.AddModelError(nameof(request.DateOfBirth), "Date of birth cannot be in the future.");
            return ValidationProblem(ModelState);
        }

        var user = await _db.Users.Include(u => u.Role).SingleOrDefaultAsync(u => u.UserId == CurrentUserId);
        if (user is null) return Fail(StatusCodes.Status404NotFound, "User not found.");

        user.FirstName = request.FirstName.Trim();
        user.LastName = request.LastName.Trim();
        user.PhoneNumber = string.IsNullOrWhiteSpace(request.PhoneNumber) ? null : request.PhoneNumber.Trim();
        user.DateOfBirth = request.DateOfBirth?.Date;

        await _db.SaveChangesAsync();
        return UserResponse.From(user);
    }
}

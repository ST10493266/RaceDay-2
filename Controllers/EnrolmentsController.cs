using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RaceDay.Api.Data;
using RaceDay.Api.Dtos;
using RaceDay.Api.Models;

namespace RaceDay.Api.Controllers;

[ApiController]
[Route("api")]
[Authorize]
public class EnrolmentsController : ApiControllerBase
{
    private readonly RaceDayDbContext _db;

    public EnrolmentsController(RaceDayDbContext db) => _db = db;

    private IQueryable<Enrolment> EnrolmentsWithDetails() =>
        _db.Enrolments.Include(e => e.User).Include(e => e.Event).Include(e => e.Category);

    /// <summary>Enter an event by choosing one of its categories (Participant only).</summary>
    [HttpPost("enrolments")]
    [Authorize(Roles = RoleNames.Participant)]
    [ProducesResponseType(typeof(EnrolmentResponse), StatusCodes.Status201Created)]
    public async Task<ActionResult<EnrolmentResponse>> Enrol(EnrolmentRequest request)
    {
        var userId = CurrentUserId;

        var ev = await _db.Events.AsNoTracking().SingleOrDefaultAsync(e => e.EventId == request.EventId);
        if (ev is null) return Fail(StatusCodes.Status404NotFound, "Event not found.");

        var category = await _db.Categories.AsNoTracking().SingleOrDefaultAsync(c => c.CategoryId == request.CategoryId);
        if (category is null) return Fail(StatusCodes.Status404NotFound, "Category not found.");

        if (category.EventId != ev.EventId)
        {
            ModelState.AddModelError(nameof(request.CategoryId), "That category does not belong to the chosen event.");
            return ValidationProblem(ModelState);
        }

        if (await _db.Enrolments.AnyAsync(e => e.UserId == userId && e.EventId == request.EventId))
            return Fail(StatusCodes.Status409Conflict, "You are already enrolled in this event.");

        var enrolment = new Enrolment { UserId = userId, EventId = request.EventId, CategoryId = request.CategoryId };
        _db.Enrolments.Add(enrolment);
        try
        {
            await _db.SaveChangesAsync();
        }
        catch (DbUpdateException)
        {
            return Fail(StatusCodes.Status409Conflict, "You are already enrolled in this event.");
        }

        var saved = await EnrolmentsWithDetails().AsNoTracking().SingleAsync(e => e.EnrolmentId == enrolment.EnrolmentId);
        return StatusCode(StatusCodes.Status201Created, EnrolmentResponse.From(saved));
    }

    /// <summary>View my own enrolments (Participant only).</summary>
    [HttpGet("enrolments/mine")]
    [Authorize(Roles = RoleNames.Participant)]
    public async Task<ActionResult<List<EnrolmentResponse>>> GetMine()
    {
        var userId = CurrentUserId;
        var enrolments = await EnrolmentsWithDetails().AsNoTracking()
            .Where(e => e.UserId == userId)
            .OrderBy(e => e.Event.EventDate)
            .ToListAsync();
        return enrolments.Select(EnrolmentResponse.From).ToList();
    }

    /// <summary>Withdraw from an event (own enrolment only, and only before a result is recorded).</summary>
    [HttpDelete("enrolments/{id:int}")]
    [Authorize(Roles = RoleNames.Participant)]
    public async Task<IActionResult> Withdraw(int id)
    {
        var enrolment = await _db.Enrolments.SingleOrDefaultAsync(e => e.EnrolmentId == id);
        if (enrolment is null) return Fail(StatusCodes.Status404NotFound, "Enrolment not found.");
        if (enrolment.UserId != CurrentUserId)
            return Fail(StatusCodes.Status403Forbidden, "You can only withdraw your own enrolments.");

        if (await _db.Results.AnyAsync(r => r.EnrolmentId == id))
            return Fail(StatusCodes.Status409Conflict, "A result has already been recorded for this enrolment.");

        _db.Enrolments.Remove(enrolment);
        await _db.SaveChangesAsync();
        return NoContent();
    }

    /// <summary>View all enrolments for one of my events (owner Organiser only).</summary>
    [HttpGet("events/{eventId:int}/enrolments")]
    [Authorize(Roles = RoleNames.Organiser)]
    public async Task<ActionResult<List<EnrolmentResponse>>> GetForEvent(int eventId)
    {
        var ev = await _db.Events.AsNoTracking().SingleOrDefaultAsync(e => e.EventId == eventId);
        var denied = EnsureOwner(ev);
        if (denied is not null) return denied;

        var enrolments = await EnrolmentsWithDetails().AsNoTracking()
            .Where(e => e.EventId == eventId)
            .OrderBy(e => e.EnrolledAt)
            .ToListAsync();
        return enrolments.Select(EnrolmentResponse.From).ToList();
    }
}

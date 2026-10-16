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
public class ResultsController : ApiControllerBase
{
    private readonly RaceDayDbContext _db;

    public ResultsController(RaceDayDbContext db) => _db = db;

    private IQueryable<Result> ResultsWithDetails() =>
        _db.Results
            .Include(r => r.Enrolment).ThenInclude(e => e.User)
            .Include(r => r.Enrolment).ThenInclude(e => e.Event)
            .Include(r => r.Enrolment).ThenInclude(e => e.Category);

    /// <summary>Capture a finish time and position for an enrolled participant (owner Organiser only).</summary>
    [HttpPost("events/{eventId:int}/results")]
    [Authorize(Roles = RoleNames.Organiser)]
    [ProducesResponseType(typeof(ResultResponse), StatusCodes.Status201Created)]
    public async Task<ActionResult<ResultResponse>> Create(int eventId, CreateResultRequest request)
    {
        var ev = await _db.Events.AsNoTracking().SingleOrDefaultAsync(e => e.EventId == eventId);
        var denied = EnsureOwner(ev);
        if (denied is not null) return denied;

        var invalid = ValidateTime(request.FinishTime);
        if (invalid is not null) return invalid;

        var enrolment = await _db.Enrolments.AsNoTracking()
            .SingleOrDefaultAsync(e => e.EnrolmentId == request.EnrolmentId && e.EventId == eventId);
        if (enrolment is null)
        {
            ModelState.AddModelError(nameof(request.EnrolmentId), "That enrolment does not belong to this event.");
            return ValidationProblem(ModelState);
        }

        if (await _db.Results.AnyAsync(r => r.EnrolmentId == request.EnrolmentId))
            return Fail(StatusCodes.Status409Conflict, "A result has already been recorded for this enrolment.");

        if (await _db.Results.AnyAsync(r => r.Enrolment.EventId == eventId && r.Position == request.Position))
            return Fail(StatusCodes.Status409Conflict, "That finishing position has already been awarded in this event.");

        var result = new Result
        {
            EnrolmentId = request.EnrolmentId,
            FinishTime = request.FinishTime,
            Position = request.Position
        };
        _db.Results.Add(result);
        try
        {
            await _db.SaveChangesAsync();
        }
        catch (DbUpdateException)
        {
            return Fail(StatusCodes.Status409Conflict, "A result has already been recorded for this enrolment.");
        }

        var saved = await ResultsWithDetails().AsNoTracking().SingleAsync(r => r.ResultId == result.ResultId);
        return StatusCode(StatusCodes.Status201Created, ResultResponse.From(saved));
    }

    /// <summary>Correct a captured result (owner Organiser only).</summary>
    [HttpPut("events/{eventId:int}/results/{resultId:int}")]
    [Authorize(Roles = RoleNames.Organiser)]
    public async Task<ActionResult<ResultResponse>> Update(int eventId, int resultId, UpdateResultRequest request)
    {
        var ev = await _db.Events.AsNoTracking().SingleOrDefaultAsync(e => e.EventId == eventId);
        var denied = EnsureOwner(ev);
        if (denied is not null) return denied;

        var invalid = ValidateTime(request.FinishTime);
        if (invalid is not null) return invalid;

        var result = await ResultsWithDetails()
            .SingleOrDefaultAsync(r => r.ResultId == resultId && r.Enrolment.EventId == eventId);
        if (result is null) return Fail(StatusCodes.Status404NotFound, "Result not found.");

        if (await _db.Results.AnyAsync(r =>
                r.Enrolment.EventId == eventId && r.Position == request.Position && r.ResultId != resultId))
            return Fail(StatusCodes.Status409Conflict, "That finishing position has already been awarded in this event.");

        result.FinishTime = request.FinishTime;
        result.Position = request.Position;
        await _db.SaveChangesAsync();

        return ResultResponse.From(result);
    }

    /// <summary>View all results for one of my events, ordered by finishing position (owner Organiser only).</summary>
    [HttpGet("events/{eventId:int}/results")]
    [Authorize(Roles = RoleNames.Organiser)]
    public async Task<ActionResult<List<ResultResponse>>> GetForEvent(int eventId)
    {
        var ev = await _db.Events.AsNoTracking().SingleOrDefaultAsync(e => e.EventId == eventId);
        var denied = EnsureOwner(ev);
        if (denied is not null) return denied;

        var results = await ResultsWithDetails().AsNoTracking()
            .Where(r => r.Enrolment.EventId == eventId)
            .OrderBy(r => r.Position)
            .ToListAsync();
        return results.Select(ResultResponse.From).ToList();
    }

    /// <summary>View my own results across all events (Participant only).</summary>
    [HttpGet("results/mine")]
    [Authorize(Roles = RoleNames.Participant)]
    public async Task<ActionResult<List<ResultResponse>>> GetMine()
    {
        var userId = CurrentUserId;
        var results = await ResultsWithDetails().AsNoTracking()
            .Where(r => r.Enrolment.UserId == userId)
            .OrderByDescending(r => r.Enrolment.Event.EventDate)
            .ToListAsync();
        return results.Select(ResultResponse.From).ToList();
    }

    private ActionResult? ValidateTime(TimeSpan finishTime)
    {
        if (finishTime <= TimeSpan.Zero || finishTime >= TimeSpan.FromHours(24))
        {
            ModelState.AddModelError(nameof(finishTime), "FinishTime must be greater than 00:00:00 and less than 24 hours.");
            return ValidationProblem(ModelState);
        }
        return null;
    }
}

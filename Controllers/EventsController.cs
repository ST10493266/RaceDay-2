using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RaceDay.Api.Data;
using RaceDay.Api.Dtos;
using RaceDay.Api.Models;

namespace RaceDay.Api.Controllers;

[ApiController]
[Route("api/events")]
[Authorize] // must be logged in; individual actions add role rules
public class EventsController : ApiControllerBase
{
    private readonly RaceDayDbContext _db;

    public EventsController(RaceDayDbContext db) => _db = db;

    /// <summary>Browse all events (soonest first). Optional filter: ?eventType=Run|Walk|Cycle.</summary>
    [HttpGet]
    public async Task<ActionResult<List<EventResponse>>> GetAll([FromQuery] string? eventType)
    {
        var query = _db.Events.AsNoTracking().Include(e => e.Organiser).AsQueryable();

        if (!string.IsNullOrWhiteSpace(eventType))
        {
            if (!EventTypes.TryNormalise(eventType, out var type))
            {
                ModelState.AddModelError(nameof(eventType), "eventType must be Run, Walk or Cycle.");
                return ValidationProblem(ModelState);
            }
            query = query.Where(e => e.EventType == type);
        }

        var events = await query.OrderBy(e => e.EventDate).ToListAsync();
        return events.Select(EventResponse.From).ToList();
    }

    /// <summary>View a single event.</summary>
    [HttpGet("{id:int}")]
    public async Task<ActionResult<EventResponse>> GetById(int id)
    {
        var ev = await _db.Events.AsNoTracking().Include(e => e.Organiser).SingleOrDefaultAsync(e => e.EventId == id);
        if (ev is null) return Fail(StatusCodes.Status404NotFound, "Event not found.");
        return EventResponse.From(ev);
    }

    /// <summary>List the events created by the logged-in Organiser.</summary>
    [HttpGet("mine")]
    [Authorize(Roles = RoleNames.Organiser)]
    public async Task<ActionResult<List<EventResponse>>> GetMine()
    {
        var userId = CurrentUserId;
        var events = await _db.Events.AsNoTracking().Include(e => e.Organiser)
            .Where(e => e.OrganiserId == userId)
            .OrderBy(e => e.EventDate)
            .ToListAsync();
        return events.Select(EventResponse.From).ToList();
    }

    /// <summary>Create an event (Organiser only). The caller becomes the event's owner.</summary>
    [HttpPost]
    [Authorize(Roles = RoleNames.Organiser)]
    [ProducesResponseType(typeof(EventResponse), StatusCodes.Status201Created)]
    public async Task<ActionResult<EventResponse>> Create(EventRequest request)
    {
        var invalid = Validate(request, out var eventType);
        if (invalid is not null) return invalid;

        var ev = new Event
        {
            Name = request.Name.Trim(),
            Description = request.Description.Trim(),
            EventDate = request.EventDate,
            Location = request.Location.Trim(),
            DistanceKm = request.DistanceKm,
            EventType = eventType,
            OrganiserId = CurrentUserId
        };

        _db.Events.Add(ev);
        await _db.SaveChangesAsync();
        await _db.Entry(ev).Reference(e => e.Organiser).LoadAsync();

        return CreatedAtAction(nameof(GetById), new { id = ev.EventId }, EventResponse.From(ev));
    }

    /// <summary>Update an event (owner Organiser only).</summary>
    [HttpPut("{id:int}")]
    [Authorize(Roles = RoleNames.Organiser)]
    public async Task<ActionResult<EventResponse>> Update(int id, EventRequest request)
    {
        var ev = await _db.Events.Include(e => e.Organiser).SingleOrDefaultAsync(e => e.EventId == id);
        var denied = EnsureOwner(ev);
        if (denied is not null) return denied;

        var invalid = Validate(request, out var eventType);
        if (invalid is not null) return invalid;

        ev!.Name = request.Name.Trim();
        ev.Description = request.Description.Trim();
        ev.EventDate = request.EventDate;
        ev.Location = request.Location.Trim();
        ev.DistanceKm = request.DistanceKm;
        ev.EventType = eventType;

        await _db.SaveChangesAsync();
        return EventResponse.From(ev);
    }

    /// <summary>Delete an event and everything attached to it (owner Organiser only).</summary>
    [HttpDelete("{id:int}")]
    [Authorize(Roles = RoleNames.Organiser)]
    public async Task<IActionResult> Delete(int id)
    {
        var ev = await _db.Events.SingleOrDefaultAsync(e => e.EventId == id);
        var denied = EnsureOwner(ev);
        if (denied is not null) return denied;

        // Remove children explicitly (results -> enrolments -> categories) before the event itself.
        var results = await _db.Results.Where(r => r.Enrolment.EventId == id).ToListAsync();
        var enrolments = await _db.Enrolments.Where(e => e.EventId == id).ToListAsync();
        var categories = await _db.Categories.Where(c => c.EventId == id).ToListAsync();
        _db.Results.RemoveRange(results);
        _db.Enrolments.RemoveRange(enrolments);
        _db.Categories.RemoveRange(categories);
        _db.Events.Remove(ev!);

        await _db.SaveChangesAsync();
        return NoContent();
    }

    private ActionResult? Validate(EventRequest request, out string eventType)
    {
        if (!EventTypes.TryNormalise(request.EventType, out eventType))
        {
            ModelState.AddModelError(nameof(request.EventType), "EventType must be Run, Walk or Cycle.");
            return ValidationProblem(ModelState);
        }
        if (request.EventDate == default)
        {
            ModelState.AddModelError(nameof(request.EventDate), "EventDate is required.");
            return ValidationProblem(ModelState);
        }
        return null;
    }
}

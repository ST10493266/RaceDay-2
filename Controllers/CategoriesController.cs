using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RaceDay.Api.Data;
using RaceDay.Api.Dtos;
using RaceDay.Api.Models;

namespace RaceDay.Api.Controllers;

[ApiController]
[Route("api/events/{eventId:int}/categories")]
[Authorize]
public class CategoriesController : ApiControllerBase
{
    private readonly RaceDayDbContext _db;

    public CategoriesController(RaceDayDbContext db) => _db = db;

    /// <summary>View the categories offered by an event (both roles).</summary>
    [HttpGet]
    public async Task<ActionResult<List<CategoryResponse>>> GetAll(int eventId)
    {
        if (!await _db.Events.AnyAsync(e => e.EventId == eventId))
            return Fail(StatusCodes.Status404NotFound, "Event not found.");

        var categories = await _db.Categories.AsNoTracking()
            .Where(c => c.EventId == eventId)
            .OrderBy(c => c.Name)
            .ToListAsync();
        return categories.Select(CategoryResponse.From).ToList();
    }

    /// <summary>Add a category to an event (owner Organiser only).</summary>
    [HttpPost]
    [Authorize(Roles = RoleNames.Organiser)]
    [ProducesResponseType(typeof(CategoryResponse), StatusCodes.Status201Created)]
    public async Task<ActionResult<CategoryResponse>> Create(int eventId, CategoryRequest request)
    {
        var ev = await _db.Events.AsNoTracking().SingleOrDefaultAsync(e => e.EventId == eventId);
        var denied = EnsureOwner(ev);
        if (denied is not null) return denied;

        var invalid = Validate(request);
        if (invalid is not null) return invalid;

        var name = request.Name.Trim();
        if (await _db.Categories.AnyAsync(c => c.EventId == eventId && c.Name == name))
            return Fail(StatusCodes.Status409Conflict, "This event already has a category with that name.");

        var category = new Category
        {
            EventId = eventId,
            Name = name,
            MinAge = request.MinAge,
            MaxAge = request.MaxAge,
            DistanceKm = request.DistanceKm
        };
        _db.Categories.Add(category);
        await _db.SaveChangesAsync();

        return StatusCode(StatusCodes.Status201Created, CategoryResponse.From(category));
    }

    /// <summary>Update a category (owner Organiser only).</summary>
    [HttpPut("{categoryId:int}")]
    [Authorize(Roles = RoleNames.Organiser)]
    public async Task<ActionResult<CategoryResponse>> Update(int eventId, int categoryId, CategoryRequest request)
    {
        var ev = await _db.Events.AsNoTracking().SingleOrDefaultAsync(e => e.EventId == eventId);
        var denied = EnsureOwner(ev);
        if (denied is not null) return denied;

        var category = await _db.Categories.SingleOrDefaultAsync(c => c.CategoryId == categoryId && c.EventId == eventId);
        if (category is null) return Fail(StatusCodes.Status404NotFound, "Category not found.");

        var invalid = Validate(request);
        if (invalid is not null) return invalid;

        var name = request.Name.Trim();
        if (await _db.Categories.AnyAsync(c => c.EventId == eventId && c.Name == name && c.CategoryId != categoryId))
            return Fail(StatusCodes.Status409Conflict, "This event already has a category with that name.");

        category.Name = name;
        category.MinAge = request.MinAge;
        category.MaxAge = request.MaxAge;
        category.DistanceKm = request.DistanceKm;
        await _db.SaveChangesAsync();

        return CategoryResponse.From(category);
    }

    /// <summary>Delete a category that nobody has enrolled in (owner Organiser only).</summary>
    [HttpDelete("{categoryId:int}")]
    [Authorize(Roles = RoleNames.Organiser)]
    public async Task<IActionResult> Delete(int eventId, int categoryId)
    {
        var ev = await _db.Events.AsNoTracking().SingleOrDefaultAsync(e => e.EventId == eventId);
        var denied = EnsureOwner(ev);
        if (denied is not null) return denied;

        var category = await _db.Categories.SingleOrDefaultAsync(c => c.CategoryId == categoryId && c.EventId == eventId);
        if (category is null) return Fail(StatusCodes.Status404NotFound, "Category not found.");

        if (await _db.Enrolments.AnyAsync(e => e.CategoryId == categoryId))
            return Fail(StatusCodes.Status409Conflict, "This category has enrolments and cannot be deleted.");

        _db.Categories.Remove(category);
        await _db.SaveChangesAsync();
        return NoContent();
    }

    private ActionResult? Validate(CategoryRequest request)
    {
        if (request.MinAge is { } min && request.MaxAge is { } max && min > max)
        {
            ModelState.AddModelError(nameof(request.MinAge), "MinAge cannot be greater than MaxAge.");
            return ValidationProblem(ModelState);
        }
        return null;
    }
}

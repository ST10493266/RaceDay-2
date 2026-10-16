using System.ComponentModel.DataAnnotations;
using RaceDay.Api.Models;

namespace RaceDay.Api.Dtos;

public class EventRequest
{
    [Required, StringLength(150)]
    public string Name { get; set; } = string.Empty;

    [Required, StringLength(1000)]
    public string Description { get; set; } = string.Empty;

    [Required]
    public DateTime EventDate { get; set; }

    [Required, StringLength(200)]
    public string Location { get; set; } = string.Empty;

    [Range(0.1, 1000)]
    public decimal DistanceKm { get; set; }

    /// <summary>"Run", "Walk" or "Cycle".</summary>
    [Required]
    public string EventType { get; set; } = string.Empty;
}

public record EventResponse(
    int EventId,
    string Name,
    string Description,
    DateTime EventDate,
    string Location,
    decimal DistanceKm,
    string EventType,
    int OrganiserId,
    string OrganiserName)
{
    public static EventResponse From(Event e) =>
        new(e.EventId, e.Name, e.Description, e.EventDate, e.Location, e.DistanceKm, e.EventType,
            e.OrganiserId, $"{e.Organiser.FirstName} {e.Organiser.LastName}");
}

public class CategoryRequest
{
    [Required, StringLength(100)]
    public string Name { get; set; } = string.Empty;

    [Range(0, 120)]
    public int? MinAge { get; set; }

    [Range(0, 120)]
    public int? MaxAge { get; set; }

    [Range(0.1, 1000)]
    public decimal? DistanceKm { get; set; }
}

public record CategoryResponse(
    int CategoryId,
    int EventId,
    string Name,
    int? MinAge,
    int? MaxAge,
    decimal? DistanceKm)
{
    public static CategoryResponse From(Category c) =>
        new(c.CategoryId, c.EventId, c.Name, c.MinAge, c.MaxAge, c.DistanceKm);
}

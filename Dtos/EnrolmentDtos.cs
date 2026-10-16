using System.ComponentModel.DataAnnotations;
using RaceDay.Api.Models;

namespace RaceDay.Api.Dtos;

public class EnrolmentRequest
{
    [Range(1, int.MaxValue)]
    public int EventId { get; set; }

    [Range(1, int.MaxValue)]
    public int CategoryId { get; set; }
}

public record EnrolmentResponse(
    int EnrolmentId,
    int EventId,
    string EventName,
    DateTime EventDate,
    int CategoryId,
    string CategoryName,
    int ParticipantId,
    string ParticipantName,
    DateTime EnrolledAt)
{
    /// <summary>Requires User, Event and Category to be loaded.</summary>
    public static EnrolmentResponse From(Enrolment e) =>
        new(e.EnrolmentId, e.EventId, e.Event.Name, e.Event.EventDate, e.CategoryId, e.Category.Name,
            e.UserId, $"{e.User.FirstName} {e.User.LastName}", e.EnrolledAt);
}

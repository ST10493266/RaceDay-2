using System.ComponentModel.DataAnnotations;
using RaceDay.Api.Models;

namespace RaceDay.Api.Dtos;

public class CreateResultRequest
{
    [Range(1, int.MaxValue)]
    public int EnrolmentId { get; set; }

    /// <summary>Finish time as "hh:mm:ss", e.g. "00:44:30".</summary>
    public TimeSpan FinishTime { get; set; }

    [Range(1, int.MaxValue)]
    public int Position { get; set; }
}

public class UpdateResultRequest
{
    /// <summary>Finish time as "hh:mm:ss", e.g. "00:44:30".</summary>
    public TimeSpan FinishTime { get; set; }

    [Range(1, int.MaxValue)]
    public int Position { get; set; }
}

public record ResultResponse(
    int ResultId,
    int EnrolmentId,
    int EventId,
    string EventName,
    int ParticipantId,
    string ParticipantName,
    string CategoryName,
    TimeSpan FinishTime,
    int Position)
{
    /// <summary>Requires Enrolment, Enrolment.User, Enrolment.Event and Enrolment.Category to be loaded.</summary>
    public static ResultResponse From(Result r) =>
        new(r.ResultId, r.EnrolmentId, r.Enrolment.EventId, r.Enrolment.Event.Name, r.Enrolment.UserId,
            $"{r.Enrolment.User.FirstName} {r.Enrolment.User.LastName}", r.Enrolment.Category.Name,
            r.FinishTime, r.Position);
}

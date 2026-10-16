namespace RaceDay.Api.Models;

/// <summary>Links a Participant (User) to an Event and the Category they chose.</summary>
public class Enrolment
{
    public int EnrolmentId { get; set; }
    public int UserId { get; set; }
    public int EventId { get; set; }
    public int CategoryId { get; set; }
    public DateTime EnrolledAt { get; set; } = DateTime.UtcNow;

    public AppUser User { get; set; } = null!;
    public Event Event { get; set; } = null!;
    public Category Category { get; set; } = null!;
    public Result? Result { get; set; }
}

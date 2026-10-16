using Microsoft.EntityFrameworkCore;

namespace RaceDay.Api.Tests;

/// <summary>Participants entering events, and the enrolment being correctly recorded.</summary>
public class EnrolmentTests : IClassFixture<RaceDayApiFactory>
{
    private readonly RaceDayApiFactory _factory;

    public EnrolmentTests(RaceDayApiFactory factory) => _factory = factory;

    [Fact]
    public async Task Participant_CanEnrol_AndTheEnrolmentIsCorrectlyRecorded()
    {
        var s = await TestHelpers.CreateScenarioAsync(_factory);
        var (participant, user) = await TestHelpers.RegisterAndLoginAsync(_factory, "Participant");

        var response = await participant.PostAsJsonAsync("/api/enrolments",
            new { eventId = s.Event.EventId, categoryId = s.Category.CategoryId });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var enrolment = (await response.Content.ReadFromJsonAsync<EnrolmentResponse>())!;
        Assert.Equal(user.UserId, enrolment.ParticipantId);
        Assert.Equal(s.Event.EventId, enrolment.EventId);
        Assert.Equal(s.Category.CategoryId, enrolment.CategoryId);
        Assert.Equal(s.Category.Name, enrolment.CategoryName);

        // The link between participant, event and category is really stored in the database.
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RaceDayDbContext>();
        var stored = await db.Enrolments.SingleAsync(e => e.EnrolmentId == enrolment.EnrolmentId);
        Assert.Equal(user.UserId, stored.UserId);
        Assert.Equal(s.Event.EventId, stored.EventId);
        Assert.Equal(s.Category.CategoryId, stored.CategoryId);
    }

    [Fact]
    public async Task EnrolledEvent_AppearsInTheParticipantsOwnEnrolments()
    {
        var s = await TestHelpers.CreateScenarioAsync(_factory);
        var (participant, _, enrolment) = await TestHelpers.EnrolNewParticipantAsync(_factory, s);

        var mine = await participant.GetFromJsonAsync<List<EnrolmentResponse>>("/api/enrolments/mine");

        Assert.Single(mine!);
        Assert.Equal(enrolment.EnrolmentId, mine![0].EnrolmentId);
    }

    [Fact]
    public async Task Participant_OnlySeesTheirOwnEnrolments()
    {
        var s = await TestHelpers.CreateScenarioAsync(_factory);
        var (first, _, firstEnrolment) = await TestHelpers.EnrolNewParticipantAsync(_factory, s);
        var (second, _, secondEnrolment) = await TestHelpers.EnrolNewParticipantAsync(_factory, s);

        var firstList = await first.GetFromJsonAsync<List<EnrolmentResponse>>("/api/enrolments/mine");
        var secondList = await second.GetFromJsonAsync<List<EnrolmentResponse>>("/api/enrolments/mine");

        Assert.Equal(new[] { firstEnrolment.EnrolmentId }, firstList!.Select(e => e.EnrolmentId));
        Assert.Equal(new[] { secondEnrolment.EnrolmentId }, secondList!.Select(e => e.EnrolmentId));
    }

    [Fact]
    public async Task Enrolling_InTheSameEventTwice_Returns409()
    {
        var s = await TestHelpers.CreateScenarioAsync(_factory);
        var (participant, _, _) = await TestHelpers.EnrolNewParticipantAsync(_factory, s);

        var again = await participant.PostAsJsonAsync("/api/enrolments",
            new { eventId = s.Event.EventId, categoryId = s.Category.CategoryId });

        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
    }

    [Fact]
    public async Task Enrolling_WithACategoryFromAnotherEvent_Returns400()
    {
        var s = await TestHelpers.CreateScenarioAsync(_factory);
        var otherEvent = await TestHelpers.CreateEventAsync(s.Organiser, "Other event");
        var otherCategory = await TestHelpers.CreateCategoryAsync(s.Organiser, otherEvent.EventId, "Other category");
        var (participant, _) = await TestHelpers.RegisterAndLoginAsync(_factory, "Participant");

        var response = await participant.PostAsJsonAsync("/api/enrolments",
            new { eventId = s.Event.EventId, categoryId = otherCategory.CategoryId });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Enrolling_InAnEventThatDoesNotExist_Returns404()
    {
        var s = await TestHelpers.CreateScenarioAsync(_factory);
        var (participant, _) = await TestHelpers.RegisterAndLoginAsync(_factory, "Participant");

        var response = await participant.PostAsJsonAsync("/api/enrolments",
            new { eventId = 999999, categoryId = s.Category.CategoryId });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Enrolling_WithACategoryThatDoesNotExist_Returns404()
    {
        var s = await TestHelpers.CreateScenarioAsync(_factory);
        var (participant, _) = await TestHelpers.RegisterAndLoginAsync(_factory, "Participant");

        var response = await participant.PostAsJsonAsync("/api/enrolments",
            new { eventId = s.Event.EventId, categoryId = 999999 });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Enrolling_WithoutLoggingIn_Returns401()
    {
        var s = await TestHelpers.CreateScenarioAsync(_factory);
        var anonymous = _factory.CreateClient();

        var response = await anonymous.PostAsJsonAsync("/api/enrolments",
            new { eventId = s.Event.EventId, categoryId = s.Category.CategoryId });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Organiser_CanViewAllEnrolmentsForTheirEvent()
    {
        var s = await TestHelpers.CreateScenarioAsync(_factory);
        var (_, _, first) = await TestHelpers.EnrolNewParticipantAsync(_factory, s);
        var (_, _, second) = await TestHelpers.EnrolNewParticipantAsync(_factory, s);

        var enrolments = await s.Organiser.GetFromJsonAsync<List<EnrolmentResponse>>($"/api/events/{s.Event.EventId}/enrolments");

        Assert.Equal(2, enrolments!.Count);
        Assert.Contains(enrolments, e => e.EnrolmentId == first.EnrolmentId);
        Assert.Contains(enrolments, e => e.EnrolmentId == second.EnrolmentId);
    }

    [Fact]
    public async Task Organiser_CannotViewEnrolmentsForSomeoneElsesEvent_Returns403()
    {
        var s = await TestHelpers.CreateScenarioAsync(_factory);
        var (otherOrganiser, _) = await TestHelpers.RegisterAndLoginAsync(_factory, "Organiser");

        var response = await otherOrganiser.GetAsync($"/api/events/{s.Event.EventId}/enrolments");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Participant_CanWithdrawTheirOwnEnrolment()
    {
        var s = await TestHelpers.CreateScenarioAsync(_factory);
        var (participant, _, enrolment) = await TestHelpers.EnrolNewParticipantAsync(_factory, s);

        var response = await participant.DeleteAsync($"/api/enrolments/{enrolment.EnrolmentId}");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var mine = await participant.GetFromJsonAsync<List<EnrolmentResponse>>("/api/enrolments/mine");
        Assert.Empty(mine!);
    }

    [Fact]
    public async Task Participant_CannotWithdrawSomeoneElsesEnrolment_Returns403()
    {
        var s = await TestHelpers.CreateScenarioAsync(_factory);
        var (_, _, victim) = await TestHelpers.EnrolNewParticipantAsync(_factory, s);
        var (attacker, _) = await TestHelpers.RegisterAndLoginAsync(_factory, "Participant");

        var response = await attacker.DeleteAsync($"/api/enrolments/{victim.EnrolmentId}");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Category_WithEnrolments_CannotBeDeleted_Returns409()
    {
        var s = await TestHelpers.CreateScenarioAsync(_factory);
        await TestHelpers.EnrolNewParticipantAsync(_factory, s);

        var response = await s.Organiser.DeleteAsync($"/api/events/{s.Event.EventId}/categories/{s.Category.CategoryId}");

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }
}

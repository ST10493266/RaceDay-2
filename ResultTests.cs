namespace RaceDay.Api.Tests;

/// <summary>Organisers capturing results; participants viewing only their own.</summary>
public class ResultTests : IClassFixture<RaceDayApiFactory>
{
    private readonly RaceDayApiFactory _factory;

    public ResultTests(RaceDayApiFactory factory) => _factory = factory;

    private static object ResultBody(int enrolmentId, string finishTime = "00:45:10", int position = 1) =>
        new { enrolmentId, finishTime, position };

    [Fact]
    public async Task Organiser_CanCaptureAResult_AndTheParticipantCanSeeIt()
    {
        var s = await TestHelpers.CreateScenarioAsync(_factory);
        var (participant, _, enrolment) = await TestHelpers.EnrolNewParticipantAsync(_factory, s);

        var create = await s.Organiser.PostAsJsonAsync($"/api/events/{s.Event.EventId}/results",
            ResultBody(enrolment.EnrolmentId, "00:45:10", 1));

        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        var created = (await create.Content.ReadFromJsonAsync<ResultResponse>())!;
        Assert.Equal(TimeSpan.Parse("00:45:10"), created.FinishTime);
        Assert.Equal(1, created.Position);

        var mine = await participant.GetFromJsonAsync<List<ResultResponse>>("/api/results/mine");
        Assert.Single(mine!);
        Assert.Equal(created.ResultId, mine![0].ResultId);
    }

    [Fact]
    public async Task Participant_OnlySeesTheirOwnResults()
    {
        var s = await TestHelpers.CreateScenarioAsync(_factory);
        var (first, _, firstEnrolment) = await TestHelpers.EnrolNewParticipantAsync(_factory, s);
        var (second, _, secondEnrolment) = await TestHelpers.EnrolNewParticipantAsync(_factory, s);
        await s.Organiser.PostAsJsonAsync($"/api/events/{s.Event.EventId}/results", ResultBody(firstEnrolment.EnrolmentId, "00:40:00", 1));
        await s.Organiser.PostAsJsonAsync($"/api/events/{s.Event.EventId}/results", ResultBody(secondEnrolment.EnrolmentId, "00:41:00", 2));

        var firstResults = await first.GetFromJsonAsync<List<ResultResponse>>("/api/results/mine");
        var secondResults = await second.GetFromJsonAsync<List<ResultResponse>>("/api/results/mine");

        Assert.Equal(new[] { 1 }, firstResults!.Select(r => r.Position));
        Assert.Equal(new[] { 2 }, secondResults!.Select(r => r.Position));
    }

    [Fact]
    public async Task Organiser_CanListEventResults_OrderedByPosition()
    {
        var s = await TestHelpers.CreateScenarioAsync(_factory);
        var (_, _, a) = await TestHelpers.EnrolNewParticipantAsync(_factory, s);
        var (_, _, b) = await TestHelpers.EnrolNewParticipantAsync(_factory, s);
        await s.Organiser.PostAsJsonAsync($"/api/events/{s.Event.EventId}/results", ResultBody(a.EnrolmentId, "00:50:00", 2));
        await s.Organiser.PostAsJsonAsync($"/api/events/{s.Event.EventId}/results", ResultBody(b.EnrolmentId, "00:48:00", 1));

        var results = await s.Organiser.GetFromJsonAsync<List<ResultResponse>>($"/api/events/{s.Event.EventId}/results");

        Assert.Equal(new[] { 1, 2 }, results!.Select(r => r.Position));
    }

    [Fact]
    public async Task Organiser_CanCorrectAResult()
    {
        var s = await TestHelpers.CreateScenarioAsync(_factory);
        var (_, _, enrolment) = await TestHelpers.EnrolNewParticipantAsync(_factory, s);
        var create = await s.Organiser.PostAsJsonAsync($"/api/events/{s.Event.EventId}/results", ResultBody(enrolment.EnrolmentId));
        var created = (await create.Content.ReadFromJsonAsync<ResultResponse>())!;

        var update = await s.Organiser.PutAsJsonAsync($"/api/events/{s.Event.EventId}/results/{created.ResultId}",
            new { finishTime = "00:44:00", position = 3 });

        Assert.Equal(HttpStatusCode.OK, update.StatusCode);
        var updated = (await update.Content.ReadFromJsonAsync<ResultResponse>())!;
        Assert.Equal(TimeSpan.Parse("00:44:00"), updated.FinishTime);
        Assert.Equal(3, updated.Position);
    }

    [Fact]
    public async Task CapturingASecondResultForTheSameEnrolment_Returns409()
    {
        var s = await TestHelpers.CreateScenarioAsync(_factory);
        var (_, _, enrolment) = await TestHelpers.EnrolNewParticipantAsync(_factory, s);
        await s.Organiser.PostAsJsonAsync($"/api/events/{s.Event.EventId}/results", ResultBody(enrolment.EnrolmentId, position: 1));

        var again = await s.Organiser.PostAsJsonAsync($"/api/events/{s.Event.EventId}/results", ResultBody(enrolment.EnrolmentId, position: 2));

        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
    }

    [Fact]
    public async Task AwardingTheSamePositionTwice_Returns409()
    {
        var s = await TestHelpers.CreateScenarioAsync(_factory);
        var (_, _, a) = await TestHelpers.EnrolNewParticipantAsync(_factory, s);
        var (_, _, b) = await TestHelpers.EnrolNewParticipantAsync(_factory, s);
        await s.Organiser.PostAsJsonAsync($"/api/events/{s.Event.EventId}/results", ResultBody(a.EnrolmentId, position: 1));

        var clash = await s.Organiser.PostAsJsonAsync($"/api/events/{s.Event.EventId}/results", ResultBody(b.EnrolmentId, position: 1));

        Assert.Equal(HttpStatusCode.Conflict, clash.StatusCode);
    }

    [Fact]
    public async Task CapturingAResultForAnEnrolmentInADifferentEvent_Returns400()
    {
        var s = await TestHelpers.CreateScenarioAsync(_factory);
        var other = await TestHelpers.CreateScenarioAsync(_factory);
        var (_, _, foreignEnrolment) = await TestHelpers.EnrolNewParticipantAsync(_factory, other);

        var response = await s.Organiser.PostAsJsonAsync($"/api/events/{s.Event.EventId}/results", ResultBody(foreignEnrolment.EnrolmentId));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Theory]
    [InlineData("00:00:00")]
    [InlineData("1.00:00:00")]
    public async Task CapturingAnImpossibleFinishTime_Returns400(string finishTime)
    {
        var s = await TestHelpers.CreateScenarioAsync(_factory);
        var (_, _, enrolment) = await TestHelpers.EnrolNewParticipantAsync(_factory, s);

        var response = await s.Organiser.PostAsJsonAsync($"/api/events/{s.Event.EventId}/results", ResultBody(enrolment.EnrolmentId, finishTime));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Organiser_CannotCaptureResultsForAnotherOrganisersEvent_Returns403()
    {
        var s = await TestHelpers.CreateScenarioAsync(_factory);
        var (_, _, enrolment) = await TestHelpers.EnrolNewParticipantAsync(_factory, s);
        var (otherOrganiser, _) = await TestHelpers.RegisterAndLoginAsync(_factory, "Organiser");

        var response = await otherOrganiser.PostAsJsonAsync($"/api/events/{s.Event.EventId}/results", ResultBody(enrolment.EnrolmentId));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Participant_CannotWithdrawOnceAResultIsRecorded_Returns409()
    {
        var s = await TestHelpers.CreateScenarioAsync(_factory);
        var (participant, _, enrolment) = await TestHelpers.EnrolNewParticipantAsync(_factory, s);
        await s.Organiser.PostAsJsonAsync($"/api/events/{s.Event.EventId}/results", ResultBody(enrolment.EnrolmentId));

        var response = await participant.DeleteAsync($"/api/enrolments/{enrolment.EnrolmentId}");

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }
}

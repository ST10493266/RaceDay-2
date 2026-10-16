namespace RaceDay.Api.Tests;

/// <summary>
/// Role-based access: anonymous users get 401, and the wrong role gets 403,
/// on every protected endpoint. (The ids in the URLs do not need to exist: authorisation runs first.)
/// </summary>
public class RoleAccessTests : IClassFixture<RaceDayApiFactory>
{
    private readonly RaceDayApiFactory _factory;

    public RoleAccessTests(RaceDayApiFactory factory) => _factory = factory;

    public static IEnumerable<object[]> OrganiserOnlyEndpoints => new[]
    {
        new object[] { "GET",    "/api/events/mine" },
        new object[] { "POST",   "/api/events" },
        new object[] { "PUT",    "/api/events/1" },
        new object[] { "DELETE", "/api/events/1" },
        new object[] { "POST",   "/api/events/1/categories" },
        new object[] { "PUT",    "/api/events/1/categories/1" },
        new object[] { "DELETE", "/api/events/1/categories/1" },
        new object[] { "GET",    "/api/events/1/enrolments" },
        new object[] { "POST",   "/api/events/1/results" },
        new object[] { "PUT",    "/api/events/1/results/1" },
        new object[] { "GET",    "/api/events/1/results" },
    };

    public static IEnumerable<object[]> ParticipantOnlyEndpoints => new[]
    {
        new object[] { "POST",   "/api/enrolments" },
        new object[] { "GET",    "/api/enrolments/mine" },
        new object[] { "DELETE", "/api/enrolments/1" },
        new object[] { "GET",    "/api/results/mine" },
    };

    public static IEnumerable<object[]> AnyLoggedInUserEndpoints => new[]
    {
        new object[] { "POST", "/api/auth/logout" },
        new object[] { "GET",  "/api/profile" },
        new object[] { "PUT",  "/api/profile" },
        new object[] { "GET",  "/api/events" },
        new object[] { "GET",  "/api/events/1" },
        new object[] { "GET",  "/api/events/1/categories" },
    };

    [Theory]
    [MemberData(nameof(OrganiserOnlyEndpoints))]
    public async Task Participant_IsRejectedFromOrganiserFunctionality_With403(string method, string url)
    {
        var (participant, _) = await TestHelpers.RegisterAndLoginAsync(_factory, "Participant");

        var response = await TestHelpers.SendAsync(participant, method, url);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Theory]
    [MemberData(nameof(ParticipantOnlyEndpoints))]
    public async Task Organiser_IsRejectedFromParticipantFunctionality_With403(string method, string url)
    {
        var (organiser, _) = await TestHelpers.RegisterAndLoginAsync(_factory, "Organiser");

        var response = await TestHelpers.SendAsync(organiser, method, url);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Theory]
    [MemberData(nameof(OrganiserOnlyEndpoints))]
    [MemberData(nameof(ParticipantOnlyEndpoints))]
    [MemberData(nameof(AnyLoggedInUserEndpoints))]
    public async Task UnauthenticatedRequests_AreRejectedFromAllNonPublicEndpoints_With401(string method, string url)
    {
        var anonymous = _factory.CreateClient();

        var response = await TestHelpers.SendAsync(anonymous, method, url);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task RegisterAndLogin_ArePublic()
    {
        var anonymous = _factory.CreateClient();
        var email = TestHelpers.UniqueEmail("public");

        var register = await anonymous.PostAsJsonAsync("/api/auth/register", TestHelpers.RegistrationBody(email, "Participant"));
        var login = await anonymous.PostAsJsonAsync("/api/auth/login", new { email, password = TestHelpers.Password });

        Assert.Equal(HttpStatusCode.Created, register.StatusCode);
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
    }

    [Fact]
    public async Task Participant_CannotCreateEvent_AndNothingIsCreated()
    {
        var (participant, _) = await TestHelpers.RegisterAndLoginAsync(_factory, "Participant");

        var response = await participant.PostAsJsonAsync("/api/events", TestHelpers.EventBody("Should not exist"));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        var events = await participant.GetFromJsonAsync<List<EventResponse>>("/api/events");
        Assert.DoesNotContain(events!, e => e.Name == "Should not exist");
    }

    [Fact]
    public async Task Organiser_CannotEnrolInAnEvent_Returns403()
    {
        var s = await TestHelpers.CreateScenarioAsync(_factory);

        var response = await s.Organiser.PostAsJsonAsync("/api/enrolments",
            new { eventId = s.Event.EventId, categoryId = s.Category.CategoryId });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Participant_CannotCaptureResults_Returns403()
    {
        var s = await TestHelpers.CreateScenarioAsync(_factory);
        var (participant, _, enrolment) = await TestHelpers.EnrolNewParticipantAsync(_factory, s);

        var response = await participant.PostAsJsonAsync($"/api/events/{s.Event.EventId}/results",
            new { enrolmentId = enrolment.EnrolmentId, finishTime = "00:30:00", position = 1 });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }
}

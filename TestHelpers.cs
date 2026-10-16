using System.Text;

namespace RaceDay.Api.Tests;

/// <summary>An organiser with one event and one category, ready for participants to enrol in.</summary>
public sealed record Scenario(HttpClient Organiser, UserResponse OrganiserUser, EventResponse Event, CategoryResponse Category);

public static class TestHelpers
{
    public const string Password = "Passw0rd!Test";

    public static string UniqueEmail(string prefix) => $"{prefix}-{Guid.NewGuid():N}@test.raceday.co.za";

    public static object RegistrationBody(string email, string role, string password = Password) => new
    {
        firstName = "Test",
        lastName = role,
        email,
        password,
        role
    };

    /// <summary>Registers a new user, logs in, and returns a client that now carries the session cookie.</summary>
    public static async Task<(HttpClient Client, UserResponse User)> RegisterAndLoginAsync(
        RaceDayApiFactory factory, string role)
    {
        var client = factory.CreateClient(); // each client has its own cookie jar = its own session
        var email = UniqueEmail(role.ToLowerInvariant());

        var register = await client.PostAsJsonAsync("/api/auth/register", RegistrationBody(email, role));
        register.EnsureSuccessStatusCode();

        var login = await client.PostAsJsonAsync("/api/auth/login", new { email, password = Password });
        login.EnsureSuccessStatusCode();

        var user = (await login.Content.ReadFromJsonAsync<UserResponse>())!;
        return (client, user);
    }

    public static object EventBody(string name = "Test 10km Run", string eventType = "Run") => new
    {
        name,
        description = "A test event.",
        eventDate = DateTime.UtcNow.AddMonths(2),
        location = "Cape Town",
        distanceKm = 10,
        eventType
    };

    public static async Task<EventResponse> CreateEventAsync(HttpClient organiser, string name = "Test 10km Run", string eventType = "Run")
    {
        var response = await organiser.PostAsJsonAsync("/api/events", EventBody(name, eventType));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<EventResponse>())!;
    }

    public static async Task<CategoryResponse> CreateCategoryAsync(HttpClient organiser, int eventId, string name = "Senior")
    {
        var response = await organiser.PostAsJsonAsync($"/api/events/{eventId}/categories",
            new { name, minAge = 20, maxAge = 59, distanceKm = 10 });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<CategoryResponse>())!;
    }

    public static async Task<Scenario> CreateScenarioAsync(RaceDayApiFactory factory)
    {
        var (organiser, user) = await RegisterAndLoginAsync(factory, "Organiser");
        var ev = await CreateEventAsync(organiser);
        var category = await CreateCategoryAsync(organiser, ev.EventId);
        return new Scenario(organiser, user, ev, category);
    }

    /// <summary>Registers a new participant and enrols them in the scenario's event.</summary>
    public static async Task<(HttpClient Client, UserResponse User, EnrolmentResponse Enrolment)> EnrolNewParticipantAsync(
        RaceDayApiFactory factory, Scenario scenario)
    {
        var (client, user) = await RegisterAndLoginAsync(factory, "Participant");
        var response = await client.PostAsJsonAsync("/api/enrolments",
            new { eventId = scenario.Event.EventId, categoryId = scenario.Category.CategoryId });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var enrolment = (await response.Content.ReadFromJsonAsync<EnrolmentResponse>())!;
        return (client, user, enrolment);
    }

    /// <summary>Sends a request with an empty JSON body (enough to reach the authorisation checks).</summary>
    public static Task<HttpResponseMessage> SendAsync(HttpClient client, string method, string url)
    {
        var request = new HttpRequestMessage(new HttpMethod(method), url);
        if (method is "POST" or "PUT")
            request.Content = new StringContent("{}", Encoding.UTF8, "application/json");
        return client.SendAsync(request);
    }
}

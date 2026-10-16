using Microsoft.EntityFrameworkCore;

namespace RaceDay.Api.Tests;

/// <summary>Organisers managing events and categories; both roles viewing them.</summary>
public class EventManagementTests : IClassFixture<RaceDayApiFactory>
{
    private readonly RaceDayApiFactory _factory;

    public EventManagementTests(RaceDayApiFactory factory) => _factory = factory;

    // ---------- Create ----------

    [Fact]
    public async Task Organiser_CanCreateEvent_Returns201WithLocationHeader()
    {
        var (organiser, user) = await TestHelpers.RegisterAndLoginAsync(_factory, "Organiser");

        var response = await organiser.PostAsJsonAsync("/api/events", TestHelpers.EventBody("Two Oceans Test", "Run"));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.NotNull(response.Headers.Location);
        var created = await response.Content.ReadFromJsonAsync<EventResponse>();
        Assert.Equal("Two Oceans Test", created!.Name);
        Assert.Equal("Run", created.EventType);
        Assert.Equal(user.UserId, created.OrganiserId); // the creator becomes the owner
    }

    [Fact]
    public async Task CreateEvent_EventTypeIsNormalised()
    {
        var (organiser, _) = await TestHelpers.RegisterAndLoginAsync(_factory, "Organiser");

        var created = await TestHelpers.CreateEventAsync(organiser, "Lower case type", "cycle");

        Assert.Equal("Cycle", created.EventType);
    }

    [Fact]
    public async Task CreateEvent_WithInvalidEventType_Returns400()
    {
        var (organiser, _) = await TestHelpers.RegisterAndLoginAsync(_factory, "Organiser");

        var response = await organiser.PostAsJsonAsync("/api/events", TestHelpers.EventBody(eventType: "Swim"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task CreateEvent_WithMissingName_Returns400()
    {
        var (organiser, _) = await TestHelpers.RegisterAndLoginAsync(_factory, "Organiser");

        var response = await organiser.PostAsJsonAsync("/api/events", TestHelpers.EventBody(name: ""));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task CreateEvent_WithNegativeDistance_Returns400()
    {
        var (organiser, _) = await TestHelpers.RegisterAndLoginAsync(_factory, "Organiser");

        var response = await organiser.PostAsJsonAsync("/api/events", new
        {
            name = "Bad distance",
            description = "x",
            eventDate = DateTime.UtcNow.AddDays(10),
            location = "Durban",
            distanceKm = -5,
            eventType = "Run"
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // ---------- Update ----------

    [Fact]
    public async Task Organiser_CanUpdateOwnEvent()
    {
        var (organiser, _) = await TestHelpers.RegisterAndLoginAsync(_factory, "Organiser");
        var ev = await TestHelpers.CreateEventAsync(organiser);

        var update = await organiser.PutAsJsonAsync($"/api/events/{ev.EventId}", TestHelpers.EventBody("Renamed Event", "Walk"));

        Assert.Equal(HttpStatusCode.OK, update.StatusCode);
        var fetched = await organiser.GetFromJsonAsync<EventResponse>($"/api/events/{ev.EventId}");
        Assert.Equal("Renamed Event", fetched!.Name);
        Assert.Equal("Walk", fetched.EventType);
    }

    [Fact]
    public async Task Organiser_CannotUpdateAnotherOrganisersEvent_Returns403()
    {
        var (owner, _) = await TestHelpers.RegisterAndLoginAsync(_factory, "Organiser");
        var (other, _) = await TestHelpers.RegisterAndLoginAsync(_factory, "Organiser");
        var ev = await TestHelpers.CreateEventAsync(owner);

        var response = await other.PutAsJsonAsync($"/api/events/{ev.EventId}", TestHelpers.EventBody("Hijacked"));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        var unchanged = await owner.GetFromJsonAsync<EventResponse>($"/api/events/{ev.EventId}");
        Assert.NotEqual("Hijacked", unchanged!.Name);
    }

    [Fact]
    public async Task UpdateEvent_ThatDoesNotExist_Returns404()
    {
        var (organiser, _) = await TestHelpers.RegisterAndLoginAsync(_factory, "Organiser");

        var response = await organiser.PutAsJsonAsync("/api/events/999999", TestHelpers.EventBody());

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // ---------- Delete ----------

    [Fact]
    public async Task Organiser_CanDeleteOwnEvent_AndItsCategoriesAreRemoved()
    {
        var s = await TestHelpers.CreateScenarioAsync(_factory);

        var delete = await s.Organiser.DeleteAsync($"/api/events/{s.Event.EventId}");

        Assert.Equal(HttpStatusCode.NoContent, delete.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await s.Organiser.GetAsync($"/api/events/{s.Event.EventId}")).StatusCode);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RaceDayDbContext>();
        Assert.False(await db.Categories.AnyAsync(c => c.EventId == s.Event.EventId));
    }

    [Fact]
    public async Task Organiser_CannotDeleteAnotherOrganisersEvent_Returns403()
    {
        var (owner, _) = await TestHelpers.RegisterAndLoginAsync(_factory, "Organiser");
        var (other, _) = await TestHelpers.RegisterAndLoginAsync(_factory, "Organiser");
        var ev = await TestHelpers.CreateEventAsync(owner);

        var response = await other.DeleteAsync($"/api/events/{ev.EventId}");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await owner.GetAsync($"/api/events/{ev.EventId}")).StatusCode);
    }

    [Fact]
    public async Task DeleteEvent_RemovesItsEnrolmentsAndResults()
    {
        var s = await TestHelpers.CreateScenarioAsync(_factory);
        var (_, _, enrolment) = await TestHelpers.EnrolNewParticipantAsync(_factory, s);
        var result = await s.Organiser.PostAsJsonAsync($"/api/events/{s.Event.EventId}/results",
            new { enrolmentId = enrolment.EnrolmentId, finishTime = "00:50:00", position = 1 });
        Assert.Equal(HttpStatusCode.Created, result.StatusCode);

        var delete = await s.Organiser.DeleteAsync($"/api/events/{s.Event.EventId}");

        Assert.Equal(HttpStatusCode.NoContent, delete.StatusCode);
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RaceDayDbContext>();
        Assert.False(await db.Enrolments.AnyAsync(e => e.EventId == s.Event.EventId));
        Assert.False(await db.Results.AnyAsync(r => r.EnrolmentId == enrolment.EnrolmentId));
    }

    // ---------- Viewing (both roles) ----------

    [Fact]
    public async Task Participant_CanBrowseEvents()
    {
        var s = await TestHelpers.CreateScenarioAsync(_factory);
        var (participant, _) = await TestHelpers.RegisterAndLoginAsync(_factory, "Participant");

        var events = await participant.GetFromJsonAsync<List<EventResponse>>("/api/events");

        Assert.Contains(events!, e => e.EventId == s.Event.EventId);
    }

    [Fact]
    public async Task Organiser_CanBrowseEvents()
    {
        var s = await TestHelpers.CreateScenarioAsync(_factory);

        var events = await s.Organiser.GetFromJsonAsync<List<EventResponse>>("/api/events");

        Assert.Contains(events!, e => e.EventId == s.Event.EventId);
    }

    [Fact]
    public async Task Events_CanBeFilteredByType()
    {
        var (organiser, _) = await TestHelpers.RegisterAndLoginAsync(_factory, "Organiser");
        var cycle = await TestHelpers.CreateEventAsync(organiser, "Filter cycle", "Cycle");
        var walk = await TestHelpers.CreateEventAsync(organiser, "Filter walk", "Walk");

        var cycles = await organiser.GetFromJsonAsync<List<EventResponse>>("/api/events?eventType=Cycle");

        Assert.Contains(cycles!, e => e.EventId == cycle.EventId);
        Assert.DoesNotContain(cycles!, e => e.EventId == walk.EventId);
        Assert.All(cycles!, e => Assert.Equal("Cycle", e.EventType));
    }

    [Fact]
    public async Task Events_FilterWithInvalidType_Returns400()
    {
        var (organiser, _) = await TestHelpers.RegisterAndLoginAsync(_factory, "Organiser");

        var response = await organiser.GetAsync("/api/events?eventType=Swim");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task GetEvent_ThatDoesNotExist_Returns404()
    {
        var (participant, _) = await TestHelpers.RegisterAndLoginAsync(_factory, "Participant");

        var response = await participant.GetAsync("/api/events/999999");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Organiser_MyEvents_OnlyListsOwnEvents()
    {
        var (a, _) = await TestHelpers.RegisterAndLoginAsync(_factory, "Organiser");
        var (b, _) = await TestHelpers.RegisterAndLoginAsync(_factory, "Organiser");
        var aEvent = await TestHelpers.CreateEventAsync(a, "A's event");
        var bEvent = await TestHelpers.CreateEventAsync(b, "B's event");

        var mine = await a.GetFromJsonAsync<List<EventResponse>>("/api/events/mine");

        Assert.Contains(mine!, e => e.EventId == aEvent.EventId);
        Assert.DoesNotContain(mine!, e => e.EventId == bEvent.EventId);
    }

    // ---------- Categories ----------

    [Fact]
    public async Task Organiser_CanAddCategory_AndBothRolesCanViewIt()
    {
        var s = await TestHelpers.CreateScenarioAsync(_factory);
        var (participant, _) = await TestHelpers.RegisterAndLoginAsync(_factory, "Participant");

        var extra = await TestHelpers.CreateCategoryAsync(s.Organiser, s.Event.EventId, "Under 20");

        var asParticipant = await participant.GetFromJsonAsync<List<CategoryResponse>>($"/api/events/{s.Event.EventId}/categories");
        var asOrganiser = await s.Organiser.GetFromJsonAsync<List<CategoryResponse>>($"/api/events/{s.Event.EventId}/categories");
        Assert.Contains(asParticipant!, c => c.CategoryId == extra.CategoryId);
        Assert.Contains(asOrganiser!, c => c.CategoryId == extra.CategoryId);
    }

    [Fact]
    public async Task AddCategory_WithDuplicateName_Returns409()
    {
        var s = await TestHelpers.CreateScenarioAsync(_factory); // already has "Senior"

        var response = await s.Organiser.PostAsJsonAsync($"/api/events/{s.Event.EventId}/categories", new { name = "Senior" });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task AddCategory_WithMinAgeAboveMaxAge_Returns400()
    {
        var s = await TestHelpers.CreateScenarioAsync(_factory);

        var response = await s.Organiser.PostAsJsonAsync($"/api/events/{s.Event.EventId}/categories",
            new { name = "Impossible", minAge = 50, maxAge = 20 });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Organiser_CannotAddCategoryToAnotherOrganisersEvent_Returns403()
    {
        var s = await TestHelpers.CreateScenarioAsync(_factory);
        var (other, _) = await TestHelpers.RegisterAndLoginAsync(_factory, "Organiser");

        var response = await other.PostAsJsonAsync($"/api/events/{s.Event.EventId}/categories", new { name = "Sneaky" });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Organiser_CanUpdateAndDeleteAnUnusedCategory()
    {
        var s = await TestHelpers.CreateScenarioAsync(_factory);
        var category = await TestHelpers.CreateCategoryAsync(s.Organiser, s.Event.EventId, "Temporary");

        var update = await s.Organiser.PutAsJsonAsync(
            $"/api/events/{s.Event.EventId}/categories/{category.CategoryId}", new { name = "Renamed", minAge = 10, maxAge = 15 });
        Assert.Equal(HttpStatusCode.OK, update.StatusCode);
        Assert.Equal("Renamed", (await update.Content.ReadFromJsonAsync<CategoryResponse>())!.Name);

        var delete = await s.Organiser.DeleteAsync($"/api/events/{s.Event.EventId}/categories/{category.CategoryId}");
        Assert.Equal(HttpStatusCode.NoContent, delete.StatusCode);
    }
}

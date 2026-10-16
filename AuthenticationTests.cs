using Microsoft.EntityFrameworkCore;

namespace RaceDay.Api.Tests;

/// <summary>User registration, login, logout and profile (both roles).</summary>
public class AuthenticationTests : IClassFixture<RaceDayApiFactory>
{
    private readonly RaceDayApiFactory _factory;

    public AuthenticationTests(RaceDayApiFactory factory) => _factory = factory;

    // ---------- Registration ----------

    [Theory]
    [InlineData("Participant")]
    [InlineData("Organiser")]
    public async Task Register_WithValidDetails_Returns201AndTheChosenRole(string role)
    {
        var client = _factory.CreateClient();
        var email = TestHelpers.UniqueEmail("reg");

        var response = await client.PostAsJsonAsync("/api/auth/register", TestHelpers.RegistrationBody(email, role));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var user = await response.Content.ReadFromJsonAsync<UserResponse>();
        Assert.Equal(role, user!.Role);
        Assert.Equal(email, user.Email);
    }

    [Fact]
    public async Task Register_ResponseNeverContainsPasswordOrHash()
    {
        var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/auth/register",
            TestHelpers.RegistrationBody(TestHelpers.UniqueEmail("nopw"), "Participant"));

        var body = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("password", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(TestHelpers.Password, body);
    }

    [Fact]
    public async Task Register_StoresAHashedPassword_NotThePlainText()
    {
        var client = _factory.CreateClient();
        var email = TestHelpers.UniqueEmail("hash");

        var response = await client.PostAsJsonAsync("/api/auth/register", TestHelpers.RegistrationBody(email, "Participant"));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RaceDayDbContext>();
        var stored = await db.Users.SingleAsync(u => u.Email == email);

        Assert.NotEqual(TestHelpers.Password, stored.PasswordHash);
        Assert.StartsWith("$2", stored.PasswordHash); // BCrypt format
    }

    [Fact]
    public async Task Register_WithUnknownRole_Returns400()
    {
        var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/auth/register",
            TestHelpers.RegistrationBody(TestHelpers.UniqueEmail("admin"), "Admin"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Register_WithDuplicateEmail_Returns409()
    {
        var client = _factory.CreateClient();
        var email = TestHelpers.UniqueEmail("dup");
        await client.PostAsJsonAsync("/api/auth/register", TestHelpers.RegistrationBody(email, "Participant"));

        var second = await client.PostAsJsonAsync("/api/auth/register", TestHelpers.RegistrationBody(email, "Organiser"));

        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
    }

    [Fact]
    public async Task Register_WithShortPassword_Returns400()
    {
        var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/auth/register",
            TestHelpers.RegistrationBody(TestHelpers.UniqueEmail("short"), "Participant", password: "abc"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Register_WithInvalidEmail_Returns400()
    {
        var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/auth/register",
            TestHelpers.RegistrationBody("not-an-email", "Participant"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // ---------- Login / session ----------

    [Fact]
    public async Task Login_WithValidCredentials_Returns200AndStartsASession()
    {
        var client = _factory.CreateClient();
        var email = TestHelpers.UniqueEmail("login");
        await client.PostAsJsonAsync("/api/auth/register", TestHelpers.RegistrationBody(email, "Participant"));

        var login = await client.PostAsJsonAsync("/api/auth/login", new { email, password = TestHelpers.Password });

        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        Assert.True(login.Headers.TryGetValues("Set-Cookie", out var cookies));
        Assert.Contains(cookies!, c => c.StartsWith("RaceDay.Session="));

        // The session cookie now authenticates later requests.
        var profile = await client.GetAsync("/api/profile");
        Assert.Equal(HttpStatusCode.OK, profile.StatusCode);
    }

    [Fact]
    public async Task Login_EmailIsNotCaseSensitive()
    {
        var client = _factory.CreateClient();
        var email = TestHelpers.UniqueEmail("case");
        await client.PostAsJsonAsync("/api/auth/register", TestHelpers.RegistrationBody(email, "Participant"));

        var login = await client.PostAsJsonAsync("/api/auth/login", new { email = email.ToUpperInvariant(), password = TestHelpers.Password });

        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
    }

    [Fact]
    public async Task Login_WithWrongPassword_Returns401()
    {
        var client = _factory.CreateClient();
        var email = TestHelpers.UniqueEmail("wrongpw");
        await client.PostAsJsonAsync("/api/auth/register", TestHelpers.RegistrationBody(email, "Participant"));

        var login = await client.PostAsJsonAsync("/api/auth/login", new { email, password = "WrongPassword1!" });

        Assert.Equal(HttpStatusCode.Unauthorized, login.StatusCode);
    }

    [Fact]
    public async Task Login_WithUnknownEmail_Returns401()
    {
        var client = _factory.CreateClient();

        var login = await client.PostAsJsonAsync("/api/auth/login",
            new { email = TestHelpers.UniqueEmail("ghost"), password = TestHelpers.Password });

        Assert.Equal(HttpStatusCode.Unauthorized, login.StatusCode);
    }

    [Fact]
    public async Task ProtectedEndpoint_WithoutLoggingIn_Returns401()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/profile");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Logout_EndsTheSession()
    {
        var (client, _) = await TestHelpers.RegisterAndLoginAsync(_factory, "Participant");
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/profile")).StatusCode);

        var logout = await client.PostAsync("/api/auth/logout", null);
        Assert.Equal(HttpStatusCode.NoContent, logout.StatusCode);

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/profile")).StatusCode);
    }

    [Fact]
    public async Task SeparateClients_HaveSeparateSessions()
    {
        var (loggedIn, _) = await TestHelpers.RegisterAndLoginAsync(_factory, "Participant");
        var anonymous = _factory.CreateClient();

        Assert.Equal(HttpStatusCode.OK, (await loggedIn.GetAsync("/api/profile")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/profile")).StatusCode);
    }

    // ---------- Profile (both roles) ----------

    [Theory]
    [InlineData("Participant")]
    [InlineData("Organiser")]
    public async Task Profile_CanBeViewedAndUpdated_ByBothRoles(string role)
    {
        var (client, user) = await TestHelpers.RegisterAndLoginAsync(_factory, role);

        var view = await client.GetFromJsonAsync<UserResponse>("/api/profile");
        Assert.Equal(user.UserId, view!.UserId);
        Assert.Equal(role, view.Role);

        var update = await client.PutAsJsonAsync("/api/profile",
            new { firstName = "Updated", lastName = "Name", phoneNumber = "0821234567" });
        Assert.Equal(HttpStatusCode.OK, update.StatusCode);

        var after = await client.GetFromJsonAsync<UserResponse>("/api/profile");
        Assert.Equal("Updated", after!.FirstName);
        Assert.Equal("0821234567", after.PhoneNumber);
        Assert.Equal(user.Email, after.Email); // email is not changed by a profile update
    }

    [Fact]
    public async Task Profile_UpdateWithMissingFirstName_Returns400()
    {
        var (client, _) = await TestHelpers.RegisterAndLoginAsync(_factory, "Participant");

        var response = await client.PutAsJsonAsync("/api/profile", new { firstName = "", lastName = "Name" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}

using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;

namespace RaceDay.Api.Tests;

/// <summary>
/// Starts the real API in memory for each test class, but swaps SQL Server for an isolated
/// in-memory database so the tests run anywhere (including GitHub Actions) with no setup.
/// </summary>
public class RaceDayApiFactory : WebApplicationFactory<Program>
{
    private readonly string _databaseName = $"RaceDayTests-{Guid.NewGuid()}";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        builder.ConfigureServices(services =>
        {
            var existing = services.Where(d => d.ServiceType == typeof(DbContextOptions<RaceDayDbContext>)).ToList();
            foreach (var descriptor in existing)
                services.Remove(descriptor);

            services.AddDbContext<RaceDayDbContext>(options => options.UseInMemoryDatabase(_databaseName));
        });
    }

    protected override IHost CreateHost(IHostBuilder builder)
    {
        var host = base.CreateHost(builder);

        // Creates the schema and the seeded Organiser / Participant roles.
        using var scope = host.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<RaceDayDbContext>().Database.EnsureCreated();

        return host;
    }
}

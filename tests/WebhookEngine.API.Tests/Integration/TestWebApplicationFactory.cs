using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using WebhookEngine.Infrastructure.Data;

namespace WebhookEngine.API.Tests.Integration;

/// <summary>
/// Custom WebApplicationFactory that replaces PostgreSQL with InMemory database
/// and disables background workers for fast integration tests.
/// </summary>
public class TestWebApplicationFactory : WebApplicationFactory<Program>
{
    private readonly string _dbName = $"TestDb_{Guid.NewGuid()}";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        builder.ConfigureServices(services =>
        {
            // Remove all DbContext-related registrations (Npgsql provider)
            RemoveServices(services, typeof(DbContextOptions<WebhookDbContext>));
            RemoveServices(services, typeof(DbContextOptions));

            // Remove hosted services (workers) to speed up tests
            RemoveServices(services, typeof(Microsoft.Extensions.Hosting.IHostedService));

            // Register InMemory database
            services.AddDbContext<WebhookDbContext>(options =>
                options.UseInMemoryDatabase(_dbName));
        });
    }

    private static void RemoveServices(IServiceCollection services, Type serviceType)
    {
        var descriptors = services.Where(d => d.ServiceType == serviceType).ToList();
        foreach (var descriptor in descriptors)
            services.Remove(descriptor);
    }
}

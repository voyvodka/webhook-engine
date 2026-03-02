using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
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
            services.RemoveAll<DbContextOptions<WebhookDbContext>>();
            services.RemoveAll<DbContextOptions>();
            services.RemoveAll<WebhookDbContext>();
            services.RemoveAll(typeof(IDbContextOptionsConfiguration<WebhookDbContext>));

            // Remove hosted services (workers) to speed up tests
            services.RemoveAll<Microsoft.Extensions.Hosting.IHostedService>();

            // Register InMemory database
            services.AddDbContext<WebhookDbContext>(options =>
                options.UseInMemoryDatabase(_dbName));
        });
    }
}

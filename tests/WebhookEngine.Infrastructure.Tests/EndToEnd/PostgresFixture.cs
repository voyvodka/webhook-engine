using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;
using WebhookEngine.Infrastructure.Data;

namespace WebhookEngine.Infrastructure.Tests.EndToEnd;

public sealed class PostgresFixture : IAsyncLifetime
{
#pragma warning disable CS0618 // PostgreSqlBuilder() constructor is marked obsolete in 4.11; tracked upstream — chained .WithImage(...) keeps the call equivalent.
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder()
        .WithImage("postgres:17-alpine")
        .WithDatabase("webhookengine_e2e")
        .WithUsername("test")
        .WithPassword("test")
        .Build();
#pragma warning restore CS0618

    public string ConnectionString => _container.GetConnectionString();

    public async Task InitializeAsync()
    {
        await _container.StartAsync();

        var options = new DbContextOptionsBuilder<WebhookDbContext>()
            .UseNpgsql(ConnectionString)
            .Options;

        await using var db = new WebhookDbContext(options);
        await db.Database.MigrateAsync();
    }

    public async Task DisposeAsync()
    {
        await _container.DisposeAsync();
    }

    public async Task ResetAsync()
    {
        var options = new DbContextOptionsBuilder<WebhookDbContext>()
            .UseNpgsql(ConnectionString)
            .Options;

        await using var db = new WebhookDbContext(options);
        await db.Database.ExecuteSqlRawAsync("""
            TRUNCATE TABLE
                message_attempts,
                messages,
                endpoint_event_types,
                endpoint_health,
                endpoints,
                event_types,
                applications,
                dashboard_users
            RESTART IDENTITY CASCADE;
        """);
    }
}

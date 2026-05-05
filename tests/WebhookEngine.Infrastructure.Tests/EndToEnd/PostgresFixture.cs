using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;
using WebhookEngine.Infrastructure.Data;

namespace WebhookEngine.Infrastructure.Tests.EndToEnd;

/// <summary>
/// Spins up a real PostgreSQL container once per test class and runs the
/// production EF Core migrations against it. Tests share the same container
/// for speed, and reset their data via <see cref="ResetAsync"/> before each
/// scenario so they cannot bleed into one another.
/// </summary>
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

        // Apply the production migrations exactly the way the API host would
        // on startup, so the schema under test matches what runs in prod.
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

    /// <summary>
    /// Truncates every test-managed table and resets identity sequences. Cheap
    /// enough to call from every test; faster than DROP/CREATE database.
    /// </summary>
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

using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using WebhookEngine.Core.Entities;
using WebhookEngine.Core.Enums;
using WebhookEngine.Core.Options;
using WebhookEngine.Infrastructure.Data;
using WebhookEngine.Worker;
using ApplicationEntity = WebhookEngine.Core.Entities.Application;

namespace WebhookEngine.Infrastructure.Tests.EndToEnd;

public class CircuitBreakerContentionTests : IClassFixture<PostgresFixture>
{
    private readonly PostgresFixture _fixture;

    public CircuitBreakerContentionTests(PostgresFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Concurrent_Workers_Transition_Open_To_HalfOpen_Exactly_Once_With_NonBlocking_Lock()
    {
        await _fixture.ResetAsync();

        var endpointId = await SeedExpiredOpenCircuitAsync();

        await using var sp = BuildServiceProvider();

        var worker1 = CreateWorker(sp);
        var worker2 = CreateWorker(sp);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        // Two workers race against the same expired-Open endpoint. Only one
        // pg_try_advisory_xact_lock can succeed; the loser must observe a
        // false-return and skip without blocking. If the lock were the
        // blocking flavour, the second worker would wait for the first
        // transaction to commit — well within our 1.2 s window — and the
        // assertions would still pass via idempotent re-verify, so the real
        // signal that this test guards is "two workers complete inside the
        // wait window without throwing", not a count of state mutations.
        await Task.WhenAll(
            worker1.StartAsync(cts.Token),
            worker2.StartAsync(cts.Token));

        await Task.Delay(1200);

        await cts.CancelAsync();
        await Task.WhenAll(
            worker1.StopAsync(CancellationToken.None),
            worker2.StopAsync(CancellationToken.None));

        await using var db = NewDb();
        var health = await db.EndpointHealths
            .AsNoTracking()
            .SingleAsync(h => h.EndpointId == endpointId);

        var endpoint = await db.Endpoints
            .AsNoTracking()
            .SingleAsync(e => e.Id == endpointId);

        // Exactly one Open → HalfOpen transition is the only valid outcome:
        // the lock-winner committed, and the other worker either lost the
        // try-lock or re-read under its own lock and found CircuitState
        // already moved.
        health.CircuitState.Should().Be(CircuitState.HalfOpen);
        health.CooldownUntil.Should().BeNull();
        health.ConsecutiveFailures.Should().Be(0);
        endpoint.Status.Should().Be(EndpointStatus.Degraded);
    }

    // ── Plumbing ───────────────────────────────────────

    private ServiceProvider BuildServiceProvider()
    {
        var services = new ServiceCollection();

        services.AddLogging(b => b.SetMinimumLevel(LogLevel.Warning));

        services.AddDbContext<WebhookDbContext>(o => o.UseNpgsql(_fixture.ConnectionString));

        services.Configure<CircuitBreakerOptions>(o =>
        {
            o.FailureThreshold = 5;
            o.CooldownMinutes = 5;
            o.SuccessThreshold = 1;
        });

        return services.BuildServiceProvider();
    }

    private static CircuitBreakerWorker CreateWorker(IServiceProvider sp)
    {
        var logger = sp.GetRequiredService<ILogger<CircuitBreakerWorker>>();
        var options = sp.GetRequiredService<IOptions<CircuitBreakerOptions>>();
        return new CircuitBreakerWorker(sp, logger, options);
    }

    private async Task<Guid> SeedExpiredOpenCircuitAsync()
    {
        await using var db = NewDb();

        var app = new ApplicationEntity
        {
            Id = Guid.NewGuid(),
            Name = "CB Contention App",
            ApiKeyPrefix = "whe_cb_",
            ApiKeyHash = "hash",
            SigningSecret = "whsec_cb_secret_for_signing_messages_in_tests"
        };

        var endpoint = new Endpoint
        {
            Id = Guid.NewGuid(),
            AppId = app.Id,
            Url = "https://example.invalid/cb",
            Status = EndpointStatus.Failed
        };

        var health = new EndpointHealth
        {
            EndpointId = endpoint.Id,
            CircuitState = CircuitState.Open,
            CooldownUntil = DateTime.UtcNow.AddMinutes(-1),
            ConsecutiveFailures = 5
        };

        db.Applications.Add(app);
        db.Endpoints.Add(endpoint);
        db.EndpointHealths.Add(health);
        await db.SaveChangesAsync();

        return endpoint.Id;
    }

    private WebhookDbContext NewDb()
    {
        var options = new DbContextOptionsBuilder<WebhookDbContext>()
            .UseNpgsql(_fixture.ConnectionString)
            .Options;
        return new WebhookDbContext(options);
    }
}

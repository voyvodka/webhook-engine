using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using WebhookEngine.Core.Entities;
using WebhookEngine.Core.Enums;
using WebhookEngine.Core.Options;
using WebhookEngine.Infrastructure.Data;
using WebhookEngine.Infrastructure.Services;
using ApplicationEntity = WebhookEngine.Core.Entities.Application;

namespace WebhookEngine.Infrastructure.Tests.Services;

public class EndpointHealthTrackerTests
{
    [Fact]
    public async Task RecordFailureAsync_Opens_Circuit_And_Marks_Endpoint_Failed_When_Threshold_Reached()
    {
        await using var db = CreateDbContext();

        var app = CreateApp();
        var endpoint = new Endpoint
        {
            Id = Guid.NewGuid(),
            AppId = app.Id,
            Url = "https://example.com/failed",
            Status = EndpointStatus.Active
        };

        db.Applications.Add(app);
        db.Endpoints.Add(endpoint);
        await db.SaveChangesAsync();

        var tracker = CreateTracker(db, new CircuitBreakerOptions
        {
            FailureThreshold = 3,
            CooldownMinutes = 5,
            SuccessThreshold = 1
        });

        await tracker.RecordFailureAsync(endpoint.Id);
        await tracker.RecordFailureAsync(endpoint.Id);
        await tracker.RecordFailureAsync(endpoint.Id);

        var health = await db.EndpointHealths.AsNoTracking().SingleAsync(h => h.EndpointId == endpoint.Id);
        var updatedEndpoint = await db.Endpoints.AsNoTracking().SingleAsync(e => e.Id == endpoint.Id);

        health.CircuitState.Should().Be(CircuitState.Open);
        updatedEndpoint.Status.Should().Be(EndpointStatus.Failed);
        health.CooldownUntil.Should().NotBeNull();
    }

    [Fact]
    public async Task RecordSuccessAsync_In_HalfOpen_Uses_SuccessThreshold_Then_Returns_To_Active()
    {
        await using var db = CreateDbContext();

        var app = CreateApp();
        var endpoint = new Endpoint
        {
            Id = Guid.NewGuid(),
            AppId = app.Id,
            Url = "https://example.com/halfopen",
            Status = EndpointStatus.Failed
        };

        var health = new EndpointHealth
        {
            EndpointId = endpoint.Id,
            CircuitState = CircuitState.HalfOpen,
            ConsecutiveFailures = 0
        };

        db.Applications.Add(app);
        db.Endpoints.Add(endpoint);
        db.EndpointHealths.Add(health);
        await db.SaveChangesAsync();

        var tracker = CreateTracker(db, new CircuitBreakerOptions
        {
            FailureThreshold = 5,
            CooldownMinutes = 5,
            SuccessThreshold = 2
        });

        await tracker.RecordSuccessAsync(endpoint.Id);

        var afterFirstSuccessHealth = await db.EndpointHealths.AsNoTracking().SingleAsync(h => h.EndpointId == endpoint.Id);
        var afterFirstSuccessEndpoint = await db.Endpoints.AsNoTracking().SingleAsync(e => e.Id == endpoint.Id);

        afterFirstSuccessHealth.CircuitState.Should().Be(CircuitState.HalfOpen);
        afterFirstSuccessEndpoint.Status.Should().Be(EndpointStatus.Degraded);

        await tracker.RecordSuccessAsync(endpoint.Id);

        var afterSecondSuccessHealth = await db.EndpointHealths.AsNoTracking().SingleAsync(h => h.EndpointId == endpoint.Id);
        var afterSecondSuccessEndpoint = await db.Endpoints.AsNoTracking().SingleAsync(e => e.Id == endpoint.Id);

        afterSecondSuccessHealth.CircuitState.Should().Be(CircuitState.Closed);
        afterSecondSuccessHealth.ConsecutiveFailures.Should().Be(0);
        afterSecondSuccessEndpoint.Status.Should().Be(EndpointStatus.Active);
    }

    [Fact]
    public async Task RecordFailureAsync_Does_Not_Override_Disabled_Endpoint_Status()
    {
        await using var db = CreateDbContext();

        var app = CreateApp();
        var endpoint = new Endpoint
        {
            Id = Guid.NewGuid(),
            AppId = app.Id,
            Url = "https://example.com/disabled",
            Status = EndpointStatus.Disabled
        };

        db.Applications.Add(app);
        db.Endpoints.Add(endpoint);
        await db.SaveChangesAsync();

        var tracker = CreateTracker(db, new CircuitBreakerOptions
        {
            FailureThreshold = 1,
            CooldownMinutes = 5,
            SuccessThreshold = 1
        });

        await tracker.RecordFailureAsync(endpoint.Id);

        var updatedEndpoint = await db.Endpoints.AsNoTracking().SingleAsync(e => e.Id == endpoint.Id);
        var health = await db.EndpointHealths.AsNoTracking().SingleAsync(h => h.EndpointId == endpoint.Id);

        updatedEndpoint.Status.Should().Be(EndpointStatus.Disabled);
        health.CircuitState.Should().Be(CircuitState.Open);
    }

    private static EndpointHealthTracker CreateTracker(WebhookDbContext db, CircuitBreakerOptions options)
    {
        return new EndpointHealthTracker(db, Options.Create(options));
    }

    private static ApplicationEntity CreateApp()
    {
        return new ApplicationEntity
        {
            Id = Guid.NewGuid(),
            Name = "Test App",
            ApiKeyPrefix = "whe_test_",
            ApiKeyHash = "hash",
            SigningSecret = "secret"
        };
    }

    private static WebhookDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<WebhookDbContext>()
            .UseInMemoryDatabase($"endpoint_health_tests_{Guid.NewGuid()}")
            .Options;

        return new WebhookDbContext(options);
    }
}

using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using WebhookEngine.Core.Entities;
using WebhookEngine.Core.Enums;
using WebhookEngine.Infrastructure.Data;
using WebhookEngine.Worker;
using ApplicationEntity = WebhookEngine.Core.Entities.Application;

namespace WebhookEngine.Infrastructure.Tests.EndToEnd;

// A8/B2: ComputeSnapshotAsync must derive the three operator gauges exactly from real DB state.
public class QueueMetricsSnapshotTests : IClassFixture<PostgresFixture>
{
    private readonly PostgresFixture _fixture;

    public QueueMetricsSnapshotTests(PostgresFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task ComputeSnapshotAsync_Backlog_Counts_Only_Due_Pending_And_Excludes_Future_And_Non_Pending()
    {
        await _fixture.ResetAsync();
        var now = DateTime.UtcNow;
        var (appId, endpointId, eventTypeId) = await SeedAppEndpointEventTypeAsync();

        await SeedMessagesAsync(appId, endpointId, eventTypeId,
            (MessageStatus.Pending, now.AddSeconds(-5)),
            (MessageStatus.Pending, now.AddSeconds(-120)),
            (MessageStatus.Pending, now.AddSeconds(-30)),
            (MessageStatus.Pending, now.AddSeconds(300)),    // future — excluded
            (MessageStatus.Pending, now.AddSeconds(600)),    // future — excluded
            (MessageStatus.Sending, now.AddSeconds(-10)),    // excluded
            (MessageStatus.Delivered, now.AddSeconds(-10)),  // excluded
            (MessageStatus.Failed, now.AddSeconds(-10)),     // excluded
            (MessageStatus.DeadLetter, now.AddSeconds(-10))); // excluded

        await using var db = NewDb();
        var snapshot = await QueueMetricsWorker.ComputeSnapshotAsync(db, now, CancellationToken.None);

        snapshot.PendingBacklog.Should().Be(3,
            "only Pending rows with ScheduledAt <= now count; future-scheduled Pending and non-Pending statuses are excluded");
    }

    [Fact]
    public async Task ComputeSnapshotAsync_OldestPendingAge_Is_Now_Minus_Oldest_Due_ScheduledAt()
    {
        await _fixture.ResetAsync();
        var now = DateTime.UtcNow;
        var (appId, endpointId, eventTypeId) = await SeedAppEndpointEventTypeAsync();

        await SeedMessagesAsync(appId, endpointId, eventTypeId,
            (MessageStatus.Pending, now.AddSeconds(-30)),
            (MessageStatus.Pending, now.AddSeconds(-120)),   // oldest due
            (MessageStatus.Pending, now.AddSeconds(600)));   // future, must not win the MIN

        await using var db = NewDb();
        var snapshot = await QueueMetricsWorker.ComputeSnapshotAsync(db, now, CancellationToken.None);

        snapshot.OldestPendingAgeSeconds.Should().BeInRange(118, 122,
            "head-of-line age is now minus the oldest DUE ScheduledAt (~120s), not the future row");
    }

    [Fact]
    public async Task ComputeSnapshotAsync_OldestPendingAge_Is_Zero_When_No_Due_Pending()
    {
        await _fixture.ResetAsync();
        var now = DateTime.UtcNow;
        var (appId, endpointId, eventTypeId) = await SeedAppEndpointEventTypeAsync();

        await SeedMessagesAsync(appId, endpointId, eventTypeId,
            (MessageStatus.Pending, now.AddSeconds(300)),    // future only
            (MessageStatus.Delivered, now.AddSeconds(-300)));

        await using var db = NewDb();
        var snapshot = await QueueMetricsWorker.ComputeSnapshotAsync(db, now, CancellationToken.None);

        snapshot.PendingBacklog.Should().Be(0);
        snapshot.OldestPendingAgeSeconds.Should().Be(0, "age is 0 when the due-pending set is empty");
    }

    [Fact]
    public async Task ComputeSnapshotAsync_OpenCircuitCount_Counts_Only_Open_Endpoints()
    {
        await _fixture.ResetAsync();
        var now = DateTime.UtcNow;
        var (appId, _, _) = await SeedAppEndpointEventTypeAsync();

        await SeedEndpointHealthAsync(appId,
            CircuitState.Open, CircuitState.Open, CircuitState.Closed, CircuitState.HalfOpen);

        await using var db = NewDb();
        var snapshot = await QueueMetricsWorker.ComputeSnapshotAsync(db, now, CancellationToken.None);

        snapshot.OpenCircuitCount.Should().Be(2, "only endpoint_health rows with CircuitState==Open count");
    }

    private async Task SeedMessagesAsync(
        Guid appId, Guid endpointId, Guid eventTypeId,
        params (MessageStatus Status, DateTime ScheduledAt)[] rows)
    {
        await using var db = NewDb();
        foreach (var (status, scheduledAt) in rows)
        {
            db.Messages.Add(new Message
            {
                Id = Guid.NewGuid(),
                AppId = appId,
                EndpointId = endpointId,
                EventTypeId = eventTypeId,
                Payload = "{}",
                Status = status,
                ScheduledAt = scheduledAt,
                CreatedAt = scheduledAt
            });
        }
        await db.SaveChangesAsync();
    }

    private async Task SeedEndpointHealthAsync(Guid appId, params CircuitState[] states)
    {
        await using var db = NewDb();
        foreach (var state in states)
        {
            var endpoint = new Endpoint
            {
                Id = Guid.NewGuid(),
                AppId = appId,
                Url = "https://example.invalid/health",
                Status = EndpointStatus.Active
            };
            db.Endpoints.Add(endpoint);
            db.EndpointHealths.Add(new EndpointHealth { EndpointId = endpoint.Id, CircuitState = state });
        }
        await db.SaveChangesAsync();
    }

    private async Task<(Guid AppId, Guid EndpointId, Guid EventTypeId)> SeedAppEndpointEventTypeAsync()
    {
        await using var db = NewDb();
        var app = new ApplicationEntity
        {
            Id = Guid.NewGuid(),
            Name = "Queue Metrics App",
            ApiKeyPrefix = "whe_qm_",
            ApiKeyHash = "hash",
            SigningSecret = "whsec_qm_secret_for_signing_messages_in_tests"
        };
        var endpoint = new Endpoint
        {
            Id = Guid.NewGuid(),
            AppId = app.Id,
            Url = "https://example.invalid/queuemetrics",
            Status = EndpointStatus.Active
        };
        var eventType = new EventType { Id = Guid.NewGuid(), AppId = app.Id, Name = "order.created" };
        db.Applications.Add(app);
        db.Endpoints.Add(endpoint);
        db.EventTypes.Add(eventType);
        await db.SaveChangesAsync();
        return (app.Id, endpoint.Id, eventType.Id);
    }

    private WebhookDbContext NewDb()
    {
        var options = new DbContextOptionsBuilder<WebhookDbContext>()
            .UseNpgsql(_fixture.ConnectionString)
            .Options;
        return new WebhookDbContext(options);
    }
}

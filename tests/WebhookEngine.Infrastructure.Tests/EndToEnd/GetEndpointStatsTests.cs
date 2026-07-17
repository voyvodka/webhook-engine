using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using WebhookEngine.Core.Entities;
using WebhookEngine.Core.Enums;
using WebhookEngine.Infrastructure.Data;
using WebhookEngine.Infrastructure.Repositories;
using ApplicationEntity = WebhookEngine.Core.Entities.Application;

namespace WebhookEngine.Infrastructure.Tests.EndToEnd;

// GetEndpointStatsAsync (A12): raw COUNT/AVG/percentile_cont SQL — real PostgreSQL only, InMemory EF cannot run it.
public class GetEndpointStatsTests : IClassFixture<PostgresFixture>
{
    private readonly PostgresFixture _fixture;

    public GetEndpointStatsTests(PostgresFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task GetEndpointStatsAsync_Counts_Successful_And_AvgLatency_For_In_Window_Attempts_Only()
    {
        await _fixture.ResetAsync();

        var startAt = new DateTime(2026, 6, 1, 12, 0, 0, DateTimeKind.Utc);
        var (endpointId, messageId) = await SeedAppEndpointMessageAsync();

        await using (var seedDb = NewDb())
        {
            // First row sits exactly ON startAt so the inclusive `>=` filter is exercised (a `>` mutation drops it).
            seedDb.MessageAttempts.AddRange(
                NewAttempt(messageId, endpointId, latencyMs: 10, AttemptStatus.Success, startAt),
                NewAttempt(messageId, endpointId, latencyMs: 20, AttemptStatus.Success, startAt.AddMinutes(30)),
                NewAttempt(messageId, endpointId, latencyMs: 30, AttemptStatus.Failed, startAt.AddMinutes(60)),
                NewAttempt(messageId, endpointId, latencyMs: 40, AttemptStatus.Timeout, startAt.AddHours(2)),
                // Out-of-window (created_at < startAt): huge latencies so a broken filter would blow up avg/p95.
                NewAttempt(messageId, endpointId, latencyMs: 1000, AttemptStatus.Success, startAt.AddMinutes(-1)),
                NewAttempt(messageId, endpointId, latencyMs: 5000, AttemptStatus.Failed, startAt.AddDays(-1)));
            await seedDb.SaveChangesAsync();
        }

        EndpointStatsRow stats;
        await using (var db = NewDb())
        {
            var repository = new MessageRepository(db);
            stats = await repository.GetEndpointStatsAsync(endpointId, startAt, CancellationToken.None);
        }

        stats.TotalAttempts.Should().Be(4, "only the four rows with created_at >= startAt are counted");
        stats.Successful.Should().Be(2, "only Success rows count; Failed/Timeout do not");
        stats.AvgLatency.Should().BeApproximately(25, 0.001, "mean of the in-window latencies {10,20,30,40}");
        // percentile_cont(0.95) over {10,20,30,40}: pos = 0.95*3 = 2.85 → 30 + 0.85*10 = 38.5.
        stats.P95Latency.Should().BeApproximately(38.5, 0.001,
            "continuous percentile interpolates within the in-window set; out-of-window rows are excluded");
    }

    [Fact]
    public async Task GetEndpointStatsAsync_P95_Uses_Continuous_Interpolation_Not_Nearest_Rank()
    {
        await _fixture.ResetAsync();

        var startAt = new DateTime(2026, 6, 1, 12, 0, 0, DateTimeKind.Utc);
        var (endpointId, messageId) = await SeedAppEndpointMessageAsync();

        await using (var seedDb = NewDb())
        {
            seedDb.MessageAttempts.AddRange(
                NewAttempt(messageId, endpointId, latencyMs: 0, AttemptStatus.Success, startAt),
                NewAttempt(messageId, endpointId, latencyMs: 10, AttemptStatus.Success, startAt.AddMinutes(1)),
                NewAttempt(messageId, endpointId, latencyMs: 20, AttemptStatus.Success, startAt.AddMinutes(2)),
                NewAttempt(messageId, endpointId, latencyMs: 30, AttemptStatus.Success, startAt.AddMinutes(3)),
                NewAttempt(messageId, endpointId, latencyMs: 40, AttemptStatus.Success, startAt.AddMinutes(4)));
            await seedDb.SaveChangesAsync();
        }

        EndpointStatsRow stats;
        await using (var db = NewDb())
        {
            var repository = new MessageRepository(db);
            stats = await repository.GetEndpointStatsAsync(endpointId, startAt, CancellationToken.None);
        }

        stats.TotalAttempts.Should().Be(5);
        stats.AvgLatency.Should().BeApproximately(20, 0.001);
        // {0,10,20,30,40}: pos = 0.95*4 = 3.8 → 30 + 0.8*10 = 38. Nearest-rank (percentile_disc) would give 40.
        stats.P95Latency.Should().BeApproximately(38, 0.001,
            "percentile_cont interpolates (38), it is not nearest-rank (which would give 40)");
    }

    [Fact]
    public async Task GetEndpointStatsAsync_With_No_Attempts_Returns_Zeroed_Aggregates()
    {
        await _fixture.ResetAsync();

        var startAt = new DateTime(2026, 6, 1, 12, 0, 0, DateTimeKind.Utc);
        var (endpointId, _) = await SeedAppEndpointMessageAsync();

        EndpointStatsRow stats;
        await using (var db = NewDb())
        {
            var repository = new MessageRepository(db);
            stats = await repository.GetEndpointStatsAsync(endpointId, startAt, CancellationToken.None);
        }

        stats.TotalAttempts.Should().Be(0);
        stats.Successful.Should().Be(0);
        stats.AvgLatency.Should().Be(0, "COALESCE(AVG(...),0) guards the empty set");
        stats.P95Latency.Should().Be(0, "COALESCE(percentile_cont(...),0) guards the empty set");
    }

    private static MessageAttempt NewAttempt(
        Guid messageId,
        Guid endpointId,
        int latencyMs,
        AttemptStatus status,
        DateTime createdAt)
    {
        return new MessageAttempt
        {
            Id = Guid.NewGuid(),
            MessageId = messageId,
            EndpointId = endpointId,
            AttemptNumber = 1,
            Status = status,
            LatencyMs = latencyMs,
            CreatedAt = createdAt
        };
    }

    private async Task<(Guid EndpointId, Guid MessageId)> SeedAppEndpointMessageAsync()
    {
        await using var db = NewDb();

        var app = new ApplicationEntity
        {
            Id = Guid.NewGuid(),
            Name = "Stats App",
            ApiKeyPrefix = "whe_st_",
            ApiKeyHash = "hash",
            SigningSecret = "whsec_st_secret_for_signing_messages_in_tests"
        };

        var endpoint = new Endpoint
        {
            Id = Guid.NewGuid(),
            AppId = app.Id,
            Url = "https://example.invalid/stats",
            Status = EndpointStatus.Active
        };

        var eventType = new EventType
        {
            Id = Guid.NewGuid(),
            AppId = app.Id,
            Name = "order.created"
        };

        var message = new Message
        {
            Id = Guid.NewGuid(),
            AppId = app.Id,
            EndpointId = endpoint.Id,
            EventTypeId = eventType.Id,
            Payload = "{}",
            Status = MessageStatus.Delivered,
            AttemptCount = 1,
            MaxRetries = 7,
            ScheduledAt = new DateTime(2026, 6, 1, 12, 0, 0, DateTimeKind.Utc),
            CreatedAt = new DateTime(2026, 6, 1, 12, 0, 0, DateTimeKind.Utc)
        };

        db.Applications.Add(app);
        db.Endpoints.Add(endpoint);
        db.EventTypes.Add(eventType);
        db.Messages.Add(message);
        await db.SaveChangesAsync();

        return (endpoint.Id, message.Id);
    }

    private WebhookDbContext NewDb()
    {
        var options = new DbContextOptionsBuilder<WebhookDbContext>()
            .UseNpgsql(_fixture.ConnectionString)
            .Options;
        return new WebhookDbContext(options);
    }
}

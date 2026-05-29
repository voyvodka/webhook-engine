using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using WebhookEngine.Core.Entities;
using WebhookEngine.Core.Enums;
using WebhookEngine.Infrastructure.Data;
using WebhookEngine.Infrastructure.Repositories;
using ApplicationEntity = WebhookEngine.Core.Entities.Application;

namespace WebhookEngine.Infrastructure.Tests.EndToEnd;

/// <summary>
/// F2 compare-and-set guard regression. The terminal-state transitions
/// (<c>MarkDeliveredAsync</c> / <c>MarkFailedForRetryAsync</c> / <c>MarkDeadLetterAsync</c>)
/// only mutate when <c>Status == Sending AND LockedBy == lockedBy</c>. A worker
/// whose lock was stolen (stale-lock recovery or another worker) must get
/// <c>false</c> and leave the row untouched, so it can abandon without writing
/// duplicate state.
/// </summary>
public class CasGuardLockStolenTests : IClassFixture<PostgresFixture>
{
    private readonly PostgresFixture _fixture;

    public CasGuardLockStolenTests(PostgresFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task MarkDeliveredAsync_When_Lock_Stolen_Returns_False_And_Leaves_Row_Unchanged()
    {
        await _fixture.ResetAsync();

        const int attemptCount = 2;
        var (appId, endpointId, eventTypeId) = await SeedAppEndpointEventTypeAsync();
        var messageId = await SeedSendingMessageAsync(appId, endpointId, eventTypeId, "workerA", attemptCount);

        bool wrongWorkerResult;
        await using (var db = NewDb())
        {
            var repository = new MessageRepository(db);
            wrongWorkerResult = await repository.MarkDeliveredAsync(messageId, attemptCount, "workerB", CancellationToken.None);
        }

        wrongWorkerResult.Should().BeFalse("workerB does not own the lock, so the CAS guard must reject the transition");

        await using (var verifyDb = NewDb())
        {
            var row = await verifyDb.Messages.AsNoTracking().SingleAsync(m => m.Id == messageId);
            row.Status.Should().Be(MessageStatus.Sending, "the rejected transition must not mutate status");
            row.LockedBy.Should().Be("workerA", "the rejected transition must not steal the lock");
            row.DeliveredAt.Should().BeNull("the rejected transition must not set DeliveredAt");
        }

        bool rightWorkerResult;
        await using (var db = NewDb())
        {
            var repository = new MessageRepository(db);
            rightWorkerResult = await repository.MarkDeliveredAsync(messageId, attemptCount, "workerA", CancellationToken.None);
        }

        rightWorkerResult.Should().BeTrue("workerA owns the lock, so the CAS guard must accept the transition");

        await using var finalDb = NewDb();
        var delivered = await finalDb.Messages.AsNoTracking().SingleAsync(m => m.Id == messageId);
        delivered.Status.Should().Be(MessageStatus.Delivered);
        delivered.DeliveredAt.Should().NotBeNull();
        delivered.LockedBy.Should().BeNull("a committed terminal transition clears the lock");
    }

    [Fact]
    public async Task MarkFailedForRetryAsync_When_Status_Not_Sending_Returns_False_And_Leaves_Row_Unchanged()
    {
        await _fixture.ResetAsync();

        const int attemptCount = 1;
        var (appId, endpointId, eventTypeId) = await SeedAppEndpointEventTypeAsync();
        // Status is already Delivered (lock cleared) — the Status == Sending half
        // of the guard must reject the late retry-transition.
        var messageId = await SeedDeliveredMessageAsync(appId, endpointId, eventTypeId, attemptCount);

        bool result;
        await using (var db = NewDb())
        {
            var repository = new MessageRepository(db);
            result = await repository.MarkFailedForRetryAsync(
                messageId, attemptCount + 1, DateTime.UtcNow.AddMinutes(5), "workerA", CancellationToken.None);
        }

        result.Should().BeFalse("a row that is no longer Sending must not be transitioned back to Failed");

        await using var verifyDb = NewDb();
        var row = await verifyDb.Messages.AsNoTracking().SingleAsync(m => m.Id == messageId);
        row.Status.Should().Be(MessageStatus.Delivered, "the terminal Delivered state must be preserved");
        row.AttemptCount.Should().Be(attemptCount, "the rejected transition must not bump AttemptCount");
    }

    private async Task<Guid> SeedSendingMessageAsync(
        Guid appId, Guid endpointId, Guid eventTypeId, string lockedBy, int attemptCount)
    {
        await using var db = NewDb();

        var message = new Message
        {
            Id = Guid.NewGuid(),
            AppId = appId,
            EndpointId = endpointId,
            EventTypeId = eventTypeId,
            Payload = "{}",
            Status = MessageStatus.Sending,
            AttemptCount = attemptCount,
            LockedBy = lockedBy,
            LockedAt = DateTime.UtcNow,
            ScheduledAt = DateTime.UtcNow.AddSeconds(-5),
            CreatedAt = DateTime.UtcNow.AddSeconds(-5)
        };

        db.Messages.Add(message);
        await db.SaveChangesAsync();
        return message.Id;
    }

    private async Task<Guid> SeedDeliveredMessageAsync(
        Guid appId, Guid endpointId, Guid eventTypeId, int attemptCount)
    {
        await using var db = NewDb();

        var message = new Message
        {
            Id = Guid.NewGuid(),
            AppId = appId,
            EndpointId = endpointId,
            EventTypeId = eventTypeId,
            Payload = "{}",
            Status = MessageStatus.Delivered,
            AttemptCount = attemptCount,
            DeliveredAt = DateTime.UtcNow,
            ScheduledAt = DateTime.UtcNow.AddSeconds(-5),
            CreatedAt = DateTime.UtcNow.AddSeconds(-5)
        };

        db.Messages.Add(message);
        await db.SaveChangesAsync();
        return message.Id;
    }

    private async Task<(Guid AppId, Guid EndpointId, Guid EventTypeId)> SeedAppEndpointEventTypeAsync()
    {
        await using var db = NewDb();

        var app = new ApplicationEntity
        {
            Id = Guid.NewGuid(),
            Name = "CAS Guard App",
            ApiKeyPrefix = "whe_cas_",
            ApiKeyHash = "hash",
            SigningSecret = "whsec_cas_secret_for_signing_messages_in_tests"
        };

        var endpoint = new Endpoint
        {
            Id = Guid.NewGuid(),
            AppId = app.Id,
            Url = "https://example.invalid/cas",
            Status = EndpointStatus.Active
        };

        var eventType = new EventType
        {
            Id = Guid.NewGuid(),
            AppId = app.Id,
            Name = "order.created"
        };

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

using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using WebhookEngine.Core.Entities;
using WebhookEngine.Core.Enums;
using WebhookEngine.Infrastructure.Data;
using WebhookEngine.Infrastructure.Repositories;
using ApplicationEntity = WebhookEngine.Core.Entities.Application;

namespace WebhookEngine.Infrastructure.Tests.EndToEnd;

// A6: RetryAsync is a CAS (Failed/DeadLetter -> Pending) returning bool. The
// load-bearing case is that a non-retriable row — above all an in-flight Sending
// row — is left untouched and yields false, so a manual retry can't resurrect it.
public class RetryCasGuardTests : IClassFixture<PostgresFixture>
{
    private readonly PostgresFixture _fixture;

    public RetryCasGuardTests(PostgresFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task RetryAsync_When_Row_Is_Failed_Returns_True_And_Resets_To_Pending()
    {
        await _fixture.ResetAsync();

        var (appId, endpointId, eventTypeId) = await SeedAppEndpointEventTypeAsync();

        // Lock + DeliveredAt seeded non-null so the "cleared" assertions are
        // load-bearing (a natural Failed row already has them null).
        var messageId = await SeedMessageAsync(
            appId, endpointId, eventTypeId,
            MessageStatus.Failed,
            attemptCount: 5,
            lockedBy: "stale_worker_lease",
            lockedAt: DateTime.UtcNow.AddMinutes(-30),
            deliveredAt: DateTime.UtcNow.AddMinutes(-10),
            scheduledAt: DateTime.UtcNow.AddHours(-1));

        bool result;
        await using (var db = NewDb())
        {
            var repository = new MessageRepository(db);
            result = await repository.RetryAsync(messageId, CancellationToken.None);
        }

        result.Should().BeTrue("a Failed row is retriable, so the CAS must match and requeue it");

        await using var verifyDb = NewDb();
        var row = await verifyDb.Messages.AsNoTracking().SingleAsync(m => m.Id == messageId);
        row.Status.Should().Be(MessageStatus.Pending, "the retry requeues the message");
        row.AttemptCount.Should().Be(0, "the retry resets the attempt budget");
        row.DeliveredAt.Should().BeNull("the retry clears any prior DeliveredAt");
        row.LockedBy.Should().BeNull("the retry clears the lock owner");
        row.LockedAt.Should().BeNull("the retry clears the lock timestamp");
        row.ScheduledAt.Should().BeAfter(DateTime.UtcNow.AddMinutes(-1), "the retry schedules the message for now, not its stale past time");
    }

    [Fact]
    public async Task RetryAsync_When_Row_Is_DeadLetter_Returns_True_And_Resets_To_Pending()
    {
        await _fixture.ResetAsync();

        var (appId, endpointId, eventTypeId) = await SeedAppEndpointEventTypeAsync();

        // Stale lock seeded non-null to keep the "lock cleared" assertions meaningful.
        var messageId = await SeedMessageAsync(
            appId, endpointId, eventTypeId,
            MessageStatus.DeadLetter,
            attemptCount: 7,
            lockedBy: "stale_worker_lease",
            lockedAt: DateTime.UtcNow.AddMinutes(-30),
            deliveredAt: null,
            scheduledAt: DateTime.UtcNow.AddHours(-1));

        bool result;
        await using (var db = NewDb())
        {
            var repository = new MessageRepository(db);
            result = await repository.RetryAsync(messageId, CancellationToken.None);
        }

        result.Should().BeTrue("a DeadLetter row is retriable, so the CAS must match and requeue it");

        await using var verifyDb = NewDb();
        var row = await verifyDb.Messages.AsNoTracking().SingleAsync(m => m.Id == messageId);
        row.Status.Should().Be(MessageStatus.Pending, "the retry resurrects the dead-lettered message");
        row.AttemptCount.Should().Be(0, "the retry resets the attempt budget");
        row.DeliveredAt.Should().BeNull();
        row.LockedBy.Should().BeNull("the retry clears the lock owner");
        row.LockedAt.Should().BeNull("the retry clears the lock timestamp");
        row.ScheduledAt.Should().BeAfter(DateTime.UtcNow.AddMinutes(-1));
    }

    // Sending is the critical guard: an in-flight delivery must never be resurrected
    // by a concurrent manual retry. Pending and Delivered are also non-retriable.
    [Theory]
    [InlineData(MessageStatus.Sending)]
    [InlineData(MessageStatus.Pending)]
    [InlineData(MessageStatus.Delivered)]
    public async Task RetryAsync_When_Row_Is_Not_Retriable_Returns_False_And_Leaves_Row_Unchanged(MessageStatus status)
    {
        await _fixture.ResetAsync();

        var (appId, endpointId, eventTypeId) = await SeedAppEndpointEventTypeAsync();

        var seededLockedBy = status == MessageStatus.Sending ? "worker1" : null;
        var seededLockedAt = status == MessageStatus.Sending ? DateTime.UtcNow.AddMinutes(-1) : (DateTime?)null;
        var seededDeliveredAt = status == MessageStatus.Delivered ? DateTime.UtcNow.AddMinutes(-1) : (DateTime?)null;

        var messageId = await SeedMessageAsync(
            appId, endpointId, eventTypeId,
            status,
            attemptCount: 3,
            lockedBy: seededLockedBy,
            lockedAt: seededLockedAt,
            deliveredAt: seededDeliveredAt,
            scheduledAt: DateTime.UtcNow.AddHours(-1));

        bool result;
        await using (var db = NewDb())
        {
            var repository = new MessageRepository(db);
            result = await repository.RetryAsync(messageId, CancellationToken.None);
        }

        result.Should().BeFalse($"a {status} row is not retriable, so the CAS must reject the transition");

        await using var verifyDb = NewDb();
        var row = await verifyDb.Messages.AsNoTracking().SingleAsync(m => m.Id == messageId);
        row.Status.Should().Be(status, "the rejected retry must not change status");
        row.AttemptCount.Should().Be(3, "the rejected retry must not reset the attempt budget");
        row.LockedBy.Should().Be(seededLockedBy, "the rejected retry must not touch the in-flight lease");
    }

    private async Task<Guid> SeedMessageAsync(
        Guid appId,
        Guid endpointId,
        Guid eventTypeId,
        MessageStatus status,
        int attemptCount,
        string? lockedBy,
        DateTime? lockedAt,
        DateTime? deliveredAt,
        DateTime scheduledAt)
    {
        await using var db = NewDb();

        var message = new Message
        {
            Id = Guid.NewGuid(),
            AppId = appId,
            EndpointId = endpointId,
            EventTypeId = eventTypeId,
            Payload = "{}",
            Status = status,
            AttemptCount = attemptCount,
            LockedBy = lockedBy,
            LockedAt = lockedAt,
            DeliveredAt = deliveredAt,
            ScheduledAt = scheduledAt,
            CreatedAt = DateTime.UtcNow.AddHours(-1)
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
            Name = "Retry CAS App",
            ApiKeyPrefix = "whe_retry_",
            ApiKeyHash = "hash",
            SigningSecret = "whsec_retry_secret_for_signing_messages_in_tests"
        };

        var endpoint = new Endpoint
        {
            Id = Guid.NewGuid(),
            AppId = app.Id,
            Url = "https://example.invalid/retry",
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

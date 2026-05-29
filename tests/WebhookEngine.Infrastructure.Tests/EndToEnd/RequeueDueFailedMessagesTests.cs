using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using WebhookEngine.Core.Entities;
using WebhookEngine.Core.Enums;
using WebhookEngine.Infrastructure.Data;
using WebhookEngine.Infrastructure.Repositories;
using ApplicationEntity = WebhookEngine.Core.Entities.Application;

namespace WebhookEngine.Infrastructure.Tests.EndToEnd;

/// <summary>
/// Exercises the RetryScheduler's real requeue path —
/// <see cref="MessageRepository.RequeueDueFailedMessagesAsync"/> (raw set-based
/// UPDATE) — against real PostgreSQL. The eligibility predicate is
/// <c>Status == Failed AND AttemptCount &lt; MaxRetries AND ScheduledAt &lt;= now</c>;
/// this asserts only the due+retriable Failed message flips to Pending.
/// </summary>
public class RequeueDueFailedMessagesTests : IClassFixture<PostgresFixture>
{
    private readonly PostgresFixture _fixture;

    public RequeueDueFailedMessagesTests(PostgresFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task RequeueDueFailedMessagesAsync_Requeues_Only_Due_Retriable_Failed_Messages()
    {
        await _fixture.ResetAsync();

        var now = DateTime.UtcNow;
        var (appId, endpointId, eventTypeId) = await SeedAppEndpointEventTypeAsync();

        var dueFailed = NewMessage(appId, endpointId, eventTypeId, MessageStatus.Failed,
            attemptCount: 3, scheduledAt: now.AddMinutes(-1));
        var futureFailed = NewMessage(appId, endpointId, eventTypeId, MessageStatus.Failed,
            attemptCount: 3, scheduledAt: now.AddMinutes(10));
        var exhaustedFailed = NewMessage(appId, endpointId, eventTypeId, MessageStatus.Failed,
            attemptCount: 7, scheduledAt: now.AddMinutes(-1));
        var delivered = NewMessage(appId, endpointId, eventTypeId, MessageStatus.Delivered,
            attemptCount: 1, scheduledAt: now.AddMinutes(-1));
        var deadLetter = NewMessage(appId, endpointId, eventTypeId, MessageStatus.DeadLetter,
            attemptCount: 7, scheduledAt: now.AddMinutes(-1));

        await using (var seedDb = NewDb())
        {
            seedDb.Messages.AddRange(dueFailed, futureFailed, exhaustedFailed, delivered, deadLetter);
            await seedDb.SaveChangesAsync();
        }

        int requeued;
        await using (var db = NewDb())
        {
            var repository = new MessageRepository(db);
            requeued = await repository.RequeueDueFailedMessagesAsync(now, CancellationToken.None);
        }

        requeued.Should().Be(1, "only the due, non-exhausted Failed message is eligible");

        await using var verifyDb = NewDb();
        var byId = await verifyDb.Messages.AsNoTracking().ToDictionaryAsync(m => m.Id);

        byId[dueFailed.Id].Status.Should().Be(MessageStatus.Pending, "due retriable Failed message is requeued");
        byId[futureFailed.Id].Status.Should().Be(MessageStatus.Failed, "future ScheduledAt is not due yet");
        byId[exhaustedFailed.Id].Status.Should().Be(MessageStatus.Failed, "AttemptCount == MaxRetries is exhausted");
        byId[delivered.Id].Status.Should().Be(MessageStatus.Delivered, "Delivered is terminal");
        byId[deadLetter.Id].Status.Should().Be(MessageStatus.DeadLetter, "DeadLetter is terminal");
    }

    private static Message NewMessage(
        Guid appId,
        Guid endpointId,
        Guid eventTypeId,
        MessageStatus status,
        int attemptCount,
        DateTime scheduledAt)
    {
        return new Message
        {
            Id = Guid.NewGuid(),
            AppId = appId,
            EndpointId = endpointId,
            EventTypeId = eventTypeId,
            Payload = "{}",
            Status = status,
            AttemptCount = attemptCount,
            MaxRetries = 7,
            ScheduledAt = scheduledAt,
            CreatedAt = scheduledAt
        };
    }

    private async Task<(Guid AppId, Guid EndpointId, Guid EventTypeId)> SeedAppEndpointEventTypeAsync()
    {
        await using var db = NewDb();

        var app = new ApplicationEntity
        {
            Id = Guid.NewGuid(),
            Name = "Requeue App",
            ApiKeyPrefix = "whe_rq_",
            ApiKeyHash = "hash",
            SigningSecret = "whsec_rq_secret_for_signing_messages_in_tests"
        };

        var endpoint = new Endpoint
        {
            Id = Guid.NewGuid(),
            AppId = app.Id,
            Url = "https://example.invalid/requeue",
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

using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using WebhookEngine.Core.Entities;
using WebhookEngine.Core.Enums;
using WebhookEngine.Infrastructure.Data;
using WebhookEngine.Infrastructure.Queue;
using ApplicationEntity = WebhookEngine.Core.Entities.Application;

namespace WebhookEngine.Infrastructure.Tests.EndToEnd;

/// <summary>
/// Core at-least-once / no-double-delivery guarantee. Concurrent
/// <see cref="PostgresMessageQueue.DequeueAsync"/> calls run
/// <c>FOR UPDATE OF m SKIP LOCKED</c> so no two workers can lock the same Pending
/// row. This proves a message is never handed to two workers at once — the
/// foundation of the delivery engine's correctness.
/// </summary>
public class SkipLockedDequeueContentionTests : IClassFixture<PostgresFixture>
{
    private readonly PostgresFixture _fixture;

    public SkipLockedDequeueContentionTests(PostgresFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Concurrent_Dequeue_Never_Hands_The_Same_Message_To_Two_Workers()
    {
        await _fixture.ResetAsync();

        const int messageCount = 40;
        const int workerCount = 4;
        const int batchSize = 10;

        var (appId, endpointId, eventTypeId) = await SeedAppEndpointEventTypeAsync();
        await SeedPendingMessagesAsync(messageCount, appId, endpointId, eventTypeId);

        // Each worker gets its own queue + DbContext (own connection) so the
        // SKIP LOCKED contention happens at the database, not in a shared tracker.
        var dequeues = Enumerable.Range(0, workerCount)
            .Select(k => Task.Run(async () =>
            {
                await using var db = NewDb();
                var queue = new PostgresMessageQueue(db);
                var batch = await queue.DequeueAsync(batchSize, $"w{k}", CancellationToken.None);
                return (WorkerId: $"w{k}", Ids: batch.Select(m => m.Id).ToArray());
            }))
            .ToArray();

        var results = await Task.WhenAll(dequeues);

        var allReturnedIds = results.SelectMany(r => r.Ids).ToArray();
        // Demand (4×10) equals supply (40) and every row is due, so all must be
        // claimed — also stops OnlyHaveUniqueItems passing vacuously if a
        // regression made DequeueAsync silently return nothing.
        allReturnedIds.Should().HaveCount(messageCount,
            "every due message must be claimed exactly once across all workers");
        allReturnedIds.Should().OnlyHaveUniqueItems(
            "no message may be locked by more than one worker — SKIP LOCKED guarantees disjoint batches");

        // Map each returned id to the worker that claimed it, then assert the DB
        // reflects that exact owner in Sending status.
        var ownerByMessageId = results
            .SelectMany(r => r.Ids.Select(id => (id, r.WorkerId)))
            .ToDictionary(x => x.id, x => x.WorkerId);

        await using var verifyDb = NewDb();
        var locked = await verifyDb.Messages
            .AsNoTracking()
            .Where(m => allReturnedIds.Contains(m.Id))
            .ToListAsync();

        locked.Should().HaveCount(allReturnedIds.Length);
        locked.Should().OnlyContain(m => m.Status == MessageStatus.Sending,
            "every dequeued message must be flipped to Sending");

        foreach (var message in locked)
        {
            message.LockedBy.Should().Be(ownerByMessageId[message.Id],
                "the row's LockedBy must match the worker that received it");
        }
    }

    private async Task SeedPendingMessagesAsync(int count, Guid appId, Guid endpointId, Guid eventTypeId)
    {
        await using var db = NewDb();

        var dueAt = DateTime.UtcNow.AddSeconds(-5);
        for (int i = 0; i < count; i++)
        {
            db.Messages.Add(new Message
            {
                Id = Guid.NewGuid(),
                AppId = appId,
                EndpointId = endpointId,
                EventTypeId = eventTypeId,
                Payload = "{}",
                Status = MessageStatus.Pending,
                ScheduledAt = dueAt,
                CreatedAt = dueAt
            });
        }

        await db.SaveChangesAsync();
    }

    private async Task<(Guid AppId, Guid EndpointId, Guid EventTypeId)> SeedAppEndpointEventTypeAsync()
    {
        await using var db = NewDb();

        var app = new ApplicationEntity
        {
            Id = Guid.NewGuid(),
            Name = "Skip Locked App",
            ApiKeyPrefix = "whe_sl_",
            ApiKeyHash = "hash",
            SigningSecret = "whsec_sl_secret_for_signing_messages_in_tests"
        };

        var endpoint = new Endpoint
        {
            Id = Guid.NewGuid(),
            AppId = app.Id,
            Url = "https://example.invalid/skiplocked",
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

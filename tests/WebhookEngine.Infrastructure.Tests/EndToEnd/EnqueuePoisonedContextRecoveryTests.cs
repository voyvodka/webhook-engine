using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using WebhookEngine.Core.Entities;
using WebhookEngine.Core.Enums;
using WebhookEngine.Infrastructure.Data;
using WebhookEngine.Infrastructure.Queue;
using ApplicationEntity = WebhookEngine.Core.Entities.Application;

namespace WebhookEngine.Infrastructure.Tests.EndToEnd;

// Poisoned-context regression: a 23505 duplicate must be detached from the shared scoped context,
// else it re-flushes on the next SaveChanges and silently loses the good sibling. Needs real
// PostgreSQL — InMemory EF does not enforce idx_messages_app_endpoint_idempotency.
public class EnqueuePoisonedContextRecoveryTests : IClassFixture<PostgresFixture>
{
    private readonly PostgresFixture _fixture;

    public EnqueuePoisonedContextRecoveryTests(PostgresFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task EnqueueAsync_After_Duplicate_Key_Failure_Persists_Next_Sibling_On_Same_Context()
    {
        await _fixture.ResetAsync();

        const string duplicateKey = "poison-dup-key";
        var (appId, endpoint1Id, endpoint2Id, eventTypeId) = await SeedAppTwoEndpointsEventTypeAsync();

        // ONE context reused across all three enqueues models the controller's enqueue loop;
        // a fresh context per call would hide the bug entirely.
        await using var db = NewDb();
        var queue = new PostgresMessageQueue(db);

        var m1 = NewMessage(appId, endpoint1Id, eventTypeId, duplicateKey);
        await queue.EnqueueAsync(m1, CancellationToken.None);

        // M2 collides on the same (app, E1, "dup") tuple → 23505 from the unique partial index.
        var m2 = NewMessage(appId, endpoint1Id, eventTypeId, duplicateKey);
        var enqueueDuplicate = async () => await queue.EnqueueAsync(m2, CancellationToken.None);

        (await enqueueDuplicate.Should().ThrowAsync<DbUpdateException>(
                "the unique partial index rejects the duplicate (app, endpoint, key) tuple"))
            .Which.InnerException.Should().BeOfType<PostgresException>()
            .Which.SqlState.Should().Be("23505",
                "the collision must surface as PostgreSQL unique_violation, not some other error");

        // Load-bearing: pre-fix, M3's SaveChanges re-flushed the still-Added M2, re-threw 23505,
        // rolled back, and M3 was silently lost. Post-fix, M2 was detached so M3 commits.
        var m3 = NewMessage(appId, endpoint2Id, eventTypeId, "distinct-key");
        var enqueueSibling = async () => await queue.EnqueueAsync(m3, CancellationToken.None);

        await enqueueSibling.Should().NotThrowAsync(
            "a detached M2 must not poison M3's SaveChanges on the shared context");

        await using var verifyDb = NewDb();
        var persisted = await verifyDb.Messages
            .AsNoTracking()
            .Where(m => m.AppId == appId)
            .ToListAsync();

        persisted.Should().HaveCount(2, "M1 and M3 commit; the duplicate M2 never does");
        persisted.Should().ContainSingle(m => m.Id == m1.Id, "M1 committed cleanly");
        persisted.Should().ContainSingle(m => m.Id == m3.Id,
            "M3 must survive the poisoned-context sequence — this is the silent-loss guard");
        persisted.Should().NotContain(m => m.Id == m2.Id,
            "the duplicate M2 must never be committed");
    }

    private static Message NewMessage(Guid appId, Guid endpointId, Guid eventTypeId, string idempotencyKey)
        => new()
        {
            Id = Guid.NewGuid(),
            AppId = appId,
            EndpointId = endpointId,
            EventTypeId = eventTypeId,
            IdempotencyKey = idempotencyKey,
            Payload = "{}",
            Status = MessageStatus.Pending,
            ScheduledAt = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow
        };

    private async Task<(Guid AppId, Guid Endpoint1Id, Guid Endpoint2Id, Guid EventTypeId)> SeedAppTwoEndpointsEventTypeAsync()
    {
        await using var db = NewDb();

        var app = new ApplicationEntity
        {
            Id = Guid.NewGuid(),
            Name = "Poisoned Context App",
            ApiKeyPrefix = "whe_poison_",
            ApiKeyHash = "hash",
            SigningSecret = "whsec_poison_secret_for_signing_messages_in_tests"
        };

        var endpoint1 = new Endpoint
        {
            Id = Guid.NewGuid(),
            AppId = app.Id,
            Url = "https://example.invalid/e1",
            Status = EndpointStatus.Active
        };

        var endpoint2 = new Endpoint
        {
            Id = Guid.NewGuid(),
            AppId = app.Id,
            Url = "https://example.invalid/e2",
            Status = EndpointStatus.Active
        };

        var eventType = new EventType
        {
            Id = Guid.NewGuid(),
            AppId = app.Id,
            Name = "order.created"
        };

        db.Applications.Add(app);
        db.Endpoints.Add(endpoint1);
        db.Endpoints.Add(endpoint2);
        db.EventTypes.Add(eventType);
        await db.SaveChangesAsync();

        return (app.Id, endpoint1.Id, endpoint2.Id, eventType.Id);
    }

    private WebhookDbContext NewDb()
    {
        var options = new DbContextOptionsBuilder<WebhookDbContext>()
            .UseNpgsql(_fixture.ConnectionString)
            .Options;
        return new WebhookDbContext(options);
    }
}

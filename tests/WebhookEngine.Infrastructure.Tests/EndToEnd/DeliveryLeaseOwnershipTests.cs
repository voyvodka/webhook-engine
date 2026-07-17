using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using WebhookEngine.Core.Entities;
using WebhookEngine.Core.Enums;
using WebhookEngine.Infrastructure.Data;
using WebhookEngine.Infrastructure.Queue;
using WebhookEngine.Infrastructure.Repositories;
using ApplicationEntity = WebhookEngine.Core.Entities.Application;

namespace WebhookEngine.Infrastructure.Tests.EndToEnd;

// CORR-02 delivery-lifecycle ownership guards: RefreshLockAsync / ResetToPendingIfOwnedAsync
// only mutate a row still owned (Status == Sending AND LockedBy == lockedBy). Load-bearing
// case: a late reset must never regress a committed Delivered/DeadLetter row to Pending.
public class DeliveryLeaseOwnershipTests : IClassFixture<PostgresFixture>
{
    private readonly PostgresFixture _fixture;

    public DeliveryLeaseOwnershipTests(PostgresFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task RefreshLockAsync_When_Owner_Returns_True_And_Advances_LockedAt()
    {
        await _fixture.ResetAsync();

        var (appId, endpointId, eventTypeId) = await SeedAppEndpointEventTypeAsync();
        var messageId = await SeedMessageAsync(
            appId, endpointId, eventTypeId,
            MessageStatus.Sending, lockedBy: "worker1",
            lockedAt: DateTime.UtcNow.AddMinutes(-30));

        var oldLockedAt = await ReadLockedAtAsync(messageId);

        bool result;
        await using (var db = NewDb())
        {
            var repository = new MessageRepository(db);
            result = await repository.RefreshLockAsync(messageId, "worker1", CancellationToken.None);
        }

        result.Should().BeTrue("worker1 still owns the Sending row, so the lease refresh must succeed");

        await using var verifyDb = NewDb();
        var row = await verifyDb.Messages.AsNoTracking().SingleAsync(m => m.Id == messageId);
        row.Status.Should().Be(MessageStatus.Sending, "a lease refresh must not change status");
        row.LockedBy.Should().Be("worker1", "a lease refresh must not change the owner");
        row.LockedAt.Should().NotBeNull();
        row.LockedAt!.Value.Should().BeAfter(oldLockedAt!.Value, "the lease refresh must advance locked_at forward");
    }

    [Fact]
    public async Task RefreshLockAsync_When_Not_Owner_Returns_False_And_Leaves_LockedAt_Unchanged()
    {
        await _fixture.ResetAsync();

        var (appId, endpointId, eventTypeId) = await SeedAppEndpointEventTypeAsync();
        var messageId = await SeedMessageAsync(
            appId, endpointId, eventTypeId,
            MessageStatus.Sending, lockedBy: "worker1",
            lockedAt: DateTime.UtcNow.AddMinutes(-30));

        var oldLockedAt = await ReadLockedAtAsync(messageId);

        bool result;
        await using (var db = NewDb())
        {
            var repository = new MessageRepository(db);
            result = await repository.RefreshLockAsync(messageId, "worker2", CancellationToken.None);
        }

        result.Should().BeFalse("worker2 does not own the lease, so the CAS refresh must be rejected");

        await using var verifyDb = NewDb();
        var row = await verifyDb.Messages.AsNoTracking().SingleAsync(m => m.Id == messageId);
        row.Status.Should().Be(MessageStatus.Sending);
        row.LockedBy.Should().Be("worker1", "a rejected refresh must not steal the lease");
        row.LockedAt!.Value.Should().Be(oldLockedAt!.Value, "a rejected refresh must not touch locked_at");
    }

    [Theory]
    [InlineData(MessageStatus.Delivered)]
    [InlineData(MessageStatus.Pending)]
    public async Task RefreshLockAsync_When_Row_Not_Sending_Returns_False(MessageStatus status)
    {
        await _fixture.ResetAsync();

        var (appId, endpointId, eventTypeId) = await SeedAppEndpointEventTypeAsync();
        // Non-Sending rows carry no lock (delivery clears it on the terminal transition).
        var messageId = await SeedMessageAsync(
            appId, endpointId, eventTypeId,
            status, lockedBy: null, lockedAt: null);

        bool result;
        await using (var db = NewDb())
        {
            var repository = new MessageRepository(db);
            result = await repository.RefreshLockAsync(messageId, "worker1", CancellationToken.None);
        }

        result.Should().BeFalse("only a Sending row can have its lease refreshed");

        await using var verifyDb = NewDb();
        var row = await verifyDb.Messages.AsNoTracking().SingleAsync(m => m.Id == messageId);
        row.Status.Should().Be(status, "a rejected refresh must not change status");
        row.LockedAt.Should().BeNull("a rejected refresh must not set locked_at on a non-Sending row");
    }

    [Fact]
    public async Task ResetToPendingIfOwnedAsync_When_Owner_Returns_True_And_Clears_Lock()
    {
        await _fixture.ResetAsync();

        var (appId, endpointId, eventTypeId) = await SeedAppEndpointEventTypeAsync();
        var messageId = await SeedMessageAsync(
            appId, endpointId, eventTypeId,
            MessageStatus.Sending, lockedBy: "worker1",
            lockedAt: DateTime.UtcNow.AddSeconds(-5));

        bool result;
        await using (var db = NewDb())
        {
            var repository = new MessageRepository(db);
            result = await repository.ResetToPendingIfOwnedAsync(messageId, "worker1", CancellationToken.None);
        }

        result.Should().BeTrue("worker1 owns the Sending row, so the error-recovery reset must succeed");

        await using var verifyDb = NewDb();
        var row = await verifyDb.Messages.AsNoTracking().SingleAsync(m => m.Id == messageId);
        row.Status.Should().Be(MessageStatus.Pending, "the reset must requeue the message");
        row.LockedBy.Should().BeNull("the reset must clear the owner");
        row.LockedAt.Should().BeNull("the reset must clear locked_at");
    }

    // CORR-02 core: DeadLetter -> Pending is a legal state-machine transition, so only the CAS
    // "Status == Sending" clause blocks a terminal regression (lockedBy="worker1" isolates it).
    [Theory]
    [InlineData(MessageStatus.Delivered, null)]
    [InlineData(MessageStatus.Delivered, "worker1")]
    [InlineData(MessageStatus.DeadLetter, null)]
    [InlineData(MessageStatus.DeadLetter, "worker1")]
    public async Task ResetToPendingIfOwnedAsync_When_Row_Already_Terminal_Returns_False_And_Does_Not_Regress(
        MessageStatus terminalStatus, string? seededLockedBy)
    {
        await _fixture.ResetAsync();

        var (appId, endpointId, eventTypeId) = await SeedAppEndpointEventTypeAsync();
        var messageId = await SeedMessageAsync(
            appId, endpointId, eventTypeId,
            terminalStatus, lockedBy: seededLockedBy, lockedAt: null);

        bool result;
        await using (var db = NewDb())
        {
            var repository = new MessageRepository(db);
            result = await repository.ResetToPendingIfOwnedAsync(messageId, "worker1", CancellationToken.None);
        }

        result.Should().BeFalse(
            $"a committed {terminalStatus} row must never be reset back to Pending by a late error-recovery catch");

        await using var verifyDb = NewDb();
        var row = await verifyDb.Messages.AsNoTracking().SingleAsync(m => m.Id == messageId);
        row.Status.Should().Be(terminalStatus, "a terminal row must never regress to Pending");
        row.LockedBy.Should().Be(seededLockedBy, "the rejected reset must not touch the row");
    }

    [Fact]
    public async Task ResetToPendingIfOwnedAsync_When_Owned_By_Another_Worker_Returns_False_And_Leaves_Row_Unchanged()
    {
        await _fixture.ResetAsync();

        var (appId, endpointId, eventTypeId) = await SeedAppEndpointEventTypeAsync();
        var messageId = await SeedMessageAsync(
            appId, endpointId, eventTypeId,
            MessageStatus.Sending, lockedBy: "worker2",
            lockedAt: DateTime.UtcNow.AddSeconds(-5));

        bool result;
        await using (var db = NewDb())
        {
            var repository = new MessageRepository(db);
            result = await repository.ResetToPendingIfOwnedAsync(messageId, "worker1", CancellationToken.None);
        }

        result.Should().BeFalse("worker1 does not own the Sending row, so the CAS reset must be rejected");

        await using var verifyDb = NewDb();
        var row = await verifyDb.Messages.AsNoTracking().SingleAsync(m => m.Id == messageId);
        row.Status.Should().Be(MessageStatus.Sending, "another worker's in-flight lease must be preserved");
        row.LockedBy.Should().Be("worker2", "worker1's reset must not steal or clear worker2's lease");
    }

    // Full A4 stale-steal interleaving: after the real sweep reclaims an expired lease, the
    // original worker must not re-own the row via either CAS primitive (else it double-delivers).
    [Fact]
    public async Task Stale_Steal_Interleaving_Original_Worker_Cannot_Reacquire_After_Sweep()
    {
        await _fixture.ResetAsync();

        var (appId, endpointId, eventTypeId) = await SeedAppEndpointEventTypeAsync();
        var messageId = await SeedMessageAsync(
            appId, endpointId, eventTypeId,
            MessageStatus.Sending, lockedBy: "worker1",
            lockedAt: DateTime.UtcNow.AddMinutes(-30));

        int released;
        await using (var db = NewDb())
        {
            var queue = new PostgresMessageQueue(db);
            released = await queue.ReleaseStaleLocksAsync(TimeSpan.FromMinutes(5), CancellationToken.None);
        }

        released.Should().Be(1, "the sweep must reclaim the one message whose lease is older than the stale threshold");

        await using (var afterSweepDb = NewDb())
        {
            var swept = await afterSweepDb.Messages.AsNoTracking().SingleAsync(m => m.Id == messageId);
            swept.Status.Should().Be(MessageStatus.Pending, "the stale sweep requeues the message");
            swept.LockedBy.Should().BeNull("the stale sweep clears the stolen lease");
            swept.LockedAt.Should().BeNull();
        }

        bool refreshResult;
        bool resetResult;
        await using (var db = NewDb())
        {
            var repository = new MessageRepository(db);
            refreshResult = await repository.RefreshLockAsync(messageId, "worker1", CancellationToken.None);
            resetResult = await repository.ResetToPendingIfOwnedAsync(messageId, "worker1", CancellationToken.None);
        }

        refreshResult.Should().BeFalse(
            "worker1 lost the lease to the stale sweep, so its lease refresh must fail and ProcessMessageAsync must skip");
        resetResult.Should().BeFalse(
            "worker1 no longer owns the row, so its late error-recovery reset must be a no-op");

        await using var finalDb = NewDb();
        var final = await finalDb.Messages.AsNoTracking().SingleAsync(m => m.Id == messageId);
        final.Status.Should().Be(MessageStatus.Pending, "a stolen message stays queued for whichever worker claims it next");
        final.LockedBy.Should().BeNull("the original worker must never re-own a swept message");
    }

    private async Task<DateTime?> ReadLockedAtAsync(Guid messageId)
    {
        await using var db = NewDb();
        var row = await db.Messages.AsNoTracking().SingleAsync(m => m.Id == messageId);
        return row.LockedAt;
    }

    private async Task<Guid> SeedMessageAsync(
        Guid appId,
        Guid endpointId,
        Guid eventTypeId,
        MessageStatus status,
        string? lockedBy,
        DateTime? lockedAt)
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
            AttemptCount = 1,
            LockedBy = lockedBy,
            LockedAt = lockedAt,
            DeliveredAt = status == MessageStatus.Delivered ? DateTime.UtcNow : null,
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
            Name = "Lease Ownership App",
            ApiKeyPrefix = "whe_lease_",
            ApiKeyHash = "hash",
            SigningSecret = "whsec_lease_secret_for_signing_messages_in_tests"
        };

        var endpoint = new Endpoint
        {
            Id = Guid.NewGuid(),
            AppId = app.Id,
            Url = "https://example.invalid/lease",
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

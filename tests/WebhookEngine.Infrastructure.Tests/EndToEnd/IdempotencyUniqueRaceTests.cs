using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using WebhookEngine.Core.Entities;
using WebhookEngine.Core.Enums;
using WebhookEngine.Infrastructure.Data;
using ApplicationEntity = WebhookEngine.Core.Entities.Application;

namespace WebhookEngine.Infrastructure.Tests.EndToEnd;

/// <summary>
/// F7 idempotency race regression. The unique partial index
/// <c>idx_messages_app_endpoint_idempotency</c> on
/// <c>(app_id, endpoint_id, idempotency_key) WHERE idempotency_key IS NOT NULL</c>
/// must serialize concurrent inserts of the same key so exactly one row wins.
/// The controller's catch-and-replay layer sits on top of this and is not under
/// test here — this proves the database-level guarantee the replay relies on.
/// </summary>
public class IdempotencyUniqueRaceTests : IClassFixture<PostgresFixture>
{
    private readonly PostgresFixture _fixture;

    public IdempotencyUniqueRaceTests(PostgresFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Concurrent_Inserts_With_Same_Idempotency_Key_Produce_Exactly_One_Row()
    {
        await _fixture.ResetAsync();

        const int concurrency = 8;
        const string idempotencyKey = "idem-race-key-001";
        var (appId, endpointId, eventTypeId) = await SeedAppEndpointEventTypeAsync();

        // Each actor owns its own DbContext (its own connection) so the inserts
        // race for real at the database boundary rather than serializing through
        // a single shared change tracker.
        var inserts = Enumerable.Range(0, concurrency)
            .Select(_ => Task.Run(async () =>
            {
                await using var db = NewDb();
                db.Messages.Add(new Message
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
                });

                try
                {
                    await db.SaveChangesAsync();
                    return (Succeeded: true, SqlState: (string?)null);
                }
                catch (DbUpdateException ex)
                {
                    var sqlState = (ex.InnerException as PostgresException)?.SqlState;
                    return (Succeeded: false, SqlState: sqlState);
                }
            }))
            .ToArray();

        var results = await Task.WhenAll(inserts);

        var winners = results.Where(r => r.Succeeded).ToArray();
        var losers = results.Where(r => !r.Succeeded).ToArray();

        winners.Should().HaveCount(1, "the unique partial index serializes the insert to a single winner");
        losers.Should().HaveCount(concurrency - 1);
        losers.Should().OnlyContain(r => r.SqlState == "23505",
            "every loser must fail with PostgreSQL unique_violation (23505), not some other error");

        await using var verifyDb = NewDb();
        var rowCount = await verifyDb.Messages
            .AsNoTracking()
            .CountAsync(m => m.AppId == appId
                && m.EndpointId == endpointId
                && m.IdempotencyKey == idempotencyKey);

        rowCount.Should().Be(1, "exactly one row exists for the contended idempotency key");
    }

    private async Task<(Guid AppId, Guid EndpointId, Guid EventTypeId)> SeedAppEndpointEventTypeAsync()
    {
        await using var db = NewDb();

        var app = new ApplicationEntity
        {
            Id = Guid.NewGuid(),
            Name = "Idempotency Race App",
            ApiKeyPrefix = "whe_idem_",
            ApiKeyHash = "hash",
            SigningSecret = "whsec_idem_secret_for_signing_messages_in_tests"
        };

        var endpoint = new Endpoint
        {
            Id = Guid.NewGuid(),
            AppId = app.Id,
            Url = "https://example.invalid/idem",
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

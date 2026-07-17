using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using WebhookEngine.Core.Entities;
using WebhookEngine.Core.Enums;
using WebhookEngine.Core.Options;
using WebhookEngine.Infrastructure.Data;
using WebhookEngine.Infrastructure.Queue;
using ApplicationEntity = WebhookEngine.Core.Entities.Application;

namespace WebhookEngine.Infrastructure.Tests.EndToEnd;

// B3: EnqueueAsync must stamp the configured RetryPolicyOptions.MaxRetries at the enqueue
// chokepoint, so operator config — not the entity default (7) — decides the retry cap.
public class EnqueueMaxRetriesStampTests : IClassFixture<PostgresFixture>
{
    private readonly PostgresFixture _fixture;

    public EnqueueMaxRetriesStampTests(PostgresFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task EnqueueAsync_Stamps_Configured_MaxRetries_Over_Entity_Default()
    {
        await _fixture.ResetAsync();
        var (appId, endpointId, eventTypeId) = await SeedAppEndpointEventTypeAsync();

        var retryPolicy = Options.Create(new RetryPolicyOptions { MaxRetries = 3 });

        var messageId = Guid.NewGuid();
        await using (var db = NewDb())
        {
            var queue = new PostgresMessageQueue(db, retryPolicy);
            var message = new Message
            {
                Id = messageId,
                AppId = appId,
                EndpointId = endpointId,
                EventTypeId = eventTypeId,
                Payload = "{}"
                // MaxRetries deliberately left at the entity default (7).
            };

            message.MaxRetries.Should().Be(7, "the entity default is what the config must override");

            await queue.EnqueueAsync(message, CancellationToken.None);
        }

        await using var verifyDb = NewDb();
        var persisted = await verifyDb.Messages
            .AsNoTracking()
            .SingleAsync(m => m.Id == messageId);

        persisted.MaxRetries.Should().Be(3,
            "EnqueueAsync must stamp the configured RetryPolicyOptions.MaxRetries, overriding the entity default");
    }

    [Fact]
    public async Task EnqueueAsync_Without_RetryPolicy_Falls_Back_To_Default_MaxRetries()
    {
        await _fixture.ResetAsync();
        var (appId, endpointId, eventTypeId) = await SeedAppEndpointEventTypeAsync();

        var messageId = Guid.NewGuid();
        await using (var db = NewDb())
        {
            var queue = new PostgresMessageQueue(db);
            var message = new Message
            {
                Id = messageId,
                AppId = appId,
                EndpointId = endpointId,
                EventTypeId = eventTypeId,
                Payload = "{}",
                MaxRetries = 99 // caller-set value must still be overwritten by the fallback default
            };

            await queue.EnqueueAsync(message, CancellationToken.None);
        }

        await using var verifyDb = NewDb();
        var persisted = await verifyDb.Messages
            .AsNoTracking()
            .SingleAsync(m => m.Id == messageId);

        persisted.MaxRetries.Should().Be(new RetryPolicyOptions().MaxRetries,
            "with no bound options the queue falls back to a fresh RetryPolicyOptions (default 7)");
    }

    private async Task<(Guid AppId, Guid EndpointId, Guid EventTypeId)> SeedAppEndpointEventTypeAsync()
    {
        await using var db = NewDb();

        var app = new ApplicationEntity
        {
            Id = Guid.NewGuid(),
            Name = "MaxRetries Stamp App",
            ApiKeyPrefix = "whe_mr_",
            ApiKeyHash = "hash",
            SigningSecret = "whsec_mr_secret_for_signing_messages_in_tests"
        };

        var endpoint = new Endpoint
        {
            Id = Guid.NewGuid(),
            AppId = app.Id,
            Url = "https://example.invalid/maxretries",
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

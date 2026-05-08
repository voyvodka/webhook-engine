using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using WebhookEngine.Core.Entities;
using WebhookEngine.Core.Enums;
using WebhookEngine.Infrastructure.Data;
using ApplicationEntity = WebhookEngine.Core.Entities.Application;

namespace WebhookEngine.Infrastructure.Tests.EndToEnd;

/// <summary>
/// End-to-end coverage for the FK cascades introduced by
/// CascadeMessageDeleteOnAppAndEndpoint. The Postgres-backed fixture is the
/// only place these can be exercised — the InMemory provider used by the
/// API integration tests doesn't enforce FK semantics at all.
/// </summary>
public class AppDeleteCascadeTests : IClassFixture<PostgresFixture>
{
    private readonly PostgresFixture _fixture;

    public AppDeleteCascadeTests(PostgresFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Deleting_An_Application_Cascades_To_Endpoints_EventTypes_And_Messages()
    {
        await _fixture.ResetAsync();

        var (appId, _, _) = await SeedApplicationWithMessagesAsync(messageCount: 3);

        // Snapshot the row counts so the post-delete assertions only check the
        // app under test, not the fixture's other state.
        await using (var db = NewDb())
        {
            (await db.Endpoints.CountAsync(e => e.AppId == appId)).Should().Be(1);
            (await db.EventTypes.CountAsync(e => e.AppId == appId)).Should().Be(1);
            (await db.Messages.CountAsync(m => m.AppId == appId)).Should().Be(3);
        }

        // The repository's raw ExecuteDeleteAsync would fail with a FK
        // violation under the old NoAction semantics; with CASCADE the DELETE
        // takes the bound rows down with it.
        await using (var db = NewDb())
        {
            await db.Applications.Where(a => a.Id == appId).ExecuteDeleteAsync();
        }

        await using (var db = NewDb())
        {
            (await db.Applications.AnyAsync(a => a.Id == appId)).Should().BeFalse();
            (await db.Endpoints.AnyAsync(e => e.AppId == appId)).Should().BeFalse();
            (await db.EventTypes.AnyAsync(e => e.AppId == appId)).Should().BeFalse();
            (await db.Messages.AnyAsync(m => m.AppId == appId)).Should().BeFalse();
        }
    }

    [Fact]
    public async Task Deleting_An_Endpoint_Cascades_To_Bound_Messages()
    {
        await _fixture.ResetAsync();

        var (_, endpointId, _) = await SeedApplicationWithMessagesAsync(messageCount: 2);

        await using (var db = NewDb())
        {
            (await db.Messages.CountAsync(m => m.EndpointId == endpointId)).Should().Be(2);

            await db.Endpoints.Where(e => e.Id == endpointId).ExecuteDeleteAsync();
        }

        await using (var db = NewDb())
        {
            (await db.Endpoints.AnyAsync(e => e.Id == endpointId)).Should().BeFalse();
            (await db.Messages.AnyAsync(m => m.EndpointId == endpointId)).Should().BeFalse();
        }
    }

    private async Task<(Guid AppId, Guid EndpointId, Guid EventTypeId)> SeedApplicationWithMessagesAsync(int messageCount)
    {
        var appId = Guid.NewGuid();
        var endpointId = Guid.NewGuid();
        var eventTypeId = Guid.NewGuid();

        await using var db = NewDb();

        db.Applications.Add(new ApplicationEntity
        {
            Id = appId,
            Name = $"Cascade App {appId.ToString("N")[..6]}",
            ApiKeyPrefix = $"whe_{appId.ToString("N")[..6]}_",
            ApiKeyHash = "hash",
            SigningSecret = "whsec_cascade_secret"
        });

        db.Endpoints.Add(new Endpoint
        {
            Id = endpointId,
            AppId = appId,
            Url = "https://example.invalid/webhook",
            Status = EndpointStatus.Active
        });

        db.EventTypes.Add(new EventType
        {
            Id = eventTypeId,
            AppId = appId,
            Name = "cascade.event"
        });

        for (var i = 0; i < messageCount; i++)
        {
            db.Messages.Add(new Message
            {
                Id = Guid.NewGuid(),
                AppId = appId,
                EndpointId = endpointId,
                EventTypeId = eventTypeId,
                EventId = $"evt_{Guid.NewGuid():N}"[..32],
                Payload = "{}",
                Status = MessageStatus.Delivered,
                CreatedAt = DateTime.UtcNow.AddMinutes(-i)
            });
        }

        await db.SaveChangesAsync();
        return (appId, endpointId, eventTypeId);
    }

    private WebhookDbContext NewDb()
    {
        var options = new Microsoft.EntityFrameworkCore.DbContextOptionsBuilder<WebhookDbContext>()
            .UseNpgsql(_fixture.ConnectionString)
            .Options;
        return new WebhookDbContext(options);
    }
}

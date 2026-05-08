using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using WebhookEngine.Core.Entities;
using WebhookEngine.Core.Enums;
using WebhookEngine.Core.Options;
using WebhookEngine.Infrastructure.Data;
using WebhookEngine.Worker;
using ApplicationEntity = WebhookEngine.Core.Entities.Application;

namespace WebhookEngine.Infrastructure.Tests.EndToEnd;

public class RetentionCleanupTests : IClassFixture<PostgresFixture>
{
    private readonly PostgresFixture _fixture;

    public RetentionCleanupTests(PostgresFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Per_App_RetentionDeliveredDays_Override_Trims_Earlier_Than_Global_Default()
    {
        await _fixture.ResetAsync();

        // Global default is 30 days. App-A overrides to 7 days. Both apps
        // get an identical 10-day-old delivered message — only App-A's
        // copy should land outside its retention window.
        var appAggressive = await SeedApplicationAsync(retentionDeliveredDays: 7);
        var appDefault = await SeedApplicationAsync(retentionDeliveredDays: null);

        var nowUtc = DateTime.UtcNow;
        await SeedDeliveredMessageAsync(appAggressive, nowUtc.AddDays(-10));
        await SeedDeliveredMessageAsync(appDefault, nowUtc.AddDays(-10));

        var result = await RunCleanupAsync(nowUtc);

        result.DeletedDelivered.Should().Be(1);

        await using var db = NewDb();
        var aggressiveCount = await db.Messages.CountAsync(m => m.AppId == appAggressive);
        var defaultCount = await db.Messages.CountAsync(m => m.AppId == appDefault);

        aggressiveCount.Should().Be(0);
        defaultCount.Should().Be(1);
    }

    [Fact]
    public async Task Per_App_RetentionDeadLetterDays_Override_Trims_Earlier_Than_Global_Default()
    {
        await _fixture.ResetAsync();

        // Global default is 90 days. App-A overrides to 14 days. A 30-day-old
        // dead-letter row falls inside the global default but outside App-A's
        // override; only App-A's copy should be reaped.
        var appAggressive = await SeedApplicationAsync(retentionDeadLetterDays: 14);
        var appDefault = await SeedApplicationAsync(retentionDeadLetterDays: null);

        var nowUtc = DateTime.UtcNow;
        await SeedDeadLetterMessageAsync(appAggressive, nowUtc.AddDays(-30));
        await SeedDeadLetterMessageAsync(appDefault, nowUtc.AddDays(-30));

        var result = await RunCleanupAsync(nowUtc);

        result.DeletedDeadLetter.Should().Be(1);

        await using var db = NewDb();
        var aggressiveCount = await db.Messages.CountAsync(m => m.AppId == appAggressive);
        var defaultCount = await db.Messages.CountAsync(m => m.AppId == appDefault);

        aggressiveCount.Should().Be(0);
        defaultCount.Should().Be(1);
    }

    [Fact]
    public async Task Per_App_Override_Of_Zero_Days_Is_Treated_As_Fallback_Not_Reap_Everything()
    {
        // Sanity guard: if an override of 0 ever leaks through (the API
        // validator translates 0 into NULL/clear, so this is the post-DB state
        // when the override is absent), behavior should match the global
        // default — never "delete everything immediately".
        await _fixture.ResetAsync();

        var app = await SeedApplicationAsync(retentionDeliveredDays: null);

        var nowUtc = DateTime.UtcNow;
        await SeedDeliveredMessageAsync(app, nowUtc.AddHours(-1));

        var result = await RunCleanupAsync(nowUtc);

        result.DeletedDelivered.Should().Be(0);

        await using var db = NewDb();
        (await db.Messages.CountAsync(m => m.AppId == app)).Should().Be(1);
    }

    // ── Plumbing ───────────────────────────────────────

    private async Task<RetentionCleanupResult> RunCleanupAsync(DateTime nowUtc)
    {
        var options = Options.Create(new RetentionOptions
        {
            DeliveredRetentionDays = 30,
            DeadLetterRetentionDays = 90
        });

        await using var db = NewDb();

        // The worker normally resolves DbContext from a scope; for the public
        // RunCleanupAsync entry point we hand it one directly.
        var worker = new RetentionCleanupWorker(
            serviceProvider: null!,
            logger: NullLogger<RetentionCleanupWorker>.Instance,
            options: options);

        return await worker.RunCleanupAsync(db, nowUtc, CancellationToken.None);
    }

    private async Task<Guid> SeedApplicationAsync(int? retentionDeliveredDays = null, int? retentionDeadLetterDays = null)
    {
        var appId = Guid.NewGuid();

        await using var db = NewDb();
        db.Applications.Add(new ApplicationEntity
        {
            Id = appId,
            Name = $"Retention App {appId.ToString("N")[..6]}",
            ApiKeyPrefix = $"whe_{appId.ToString("N")[..6]}_",
            ApiKeyHash = "hash",
            SigningSecret = "whsec_retention_secret",
            RetentionDeliveredDays = retentionDeliveredDays,
            RetentionDeadLetterDays = retentionDeadLetterDays
        });
        await db.SaveChangesAsync();

        return appId;
    }

    private async Task SeedDeliveredMessageAsync(Guid appId, DateTime createdAtUtc)
    {
        var (endpointId, eventTypeId) = await EnsureEndpointAndEventTypeAsync(appId);

        await using var db = NewDb();
        db.Messages.Add(new Message
        {
            Id = Guid.NewGuid(),
            AppId = appId,
            EndpointId = endpointId,
            EventTypeId = eventTypeId,
            EventId = $"evt_{Guid.NewGuid():N}"[..32],
            Payload = "{}",
            Status = MessageStatus.Delivered,
            CreatedAt = createdAtUtc,
            DeliveredAt = createdAtUtc
        });
        await db.SaveChangesAsync();
    }

    private async Task SeedDeadLetterMessageAsync(Guid appId, DateTime createdAtUtc)
    {
        var (endpointId, eventTypeId) = await EnsureEndpointAndEventTypeAsync(appId);

        await using var db = NewDb();
        db.Messages.Add(new Message
        {
            Id = Guid.NewGuid(),
            AppId = appId,
            EndpointId = endpointId,
            EventTypeId = eventTypeId,
            EventId = $"evt_{Guid.NewGuid():N}"[..32],
            Payload = "{}",
            Status = MessageStatus.DeadLetter,
            CreatedAt = createdAtUtc
        });
        await db.SaveChangesAsync();
    }

    private async Task<(Guid EndpointId, Guid EventTypeId)> EnsureEndpointAndEventTypeAsync(Guid appId)
    {
        await using var db = NewDb();
        var existingEndpoint = await db.Endpoints.AsNoTracking().FirstOrDefaultAsync(e => e.AppId == appId);
        var existingEventType = await db.EventTypes.AsNoTracking().FirstOrDefaultAsync(e => e.AppId == appId);

        var endpointId = existingEndpoint?.Id ?? Guid.NewGuid();
        var eventTypeId = existingEventType?.Id ?? Guid.NewGuid();

        if (existingEndpoint is null)
        {
            db.Endpoints.Add(new Endpoint
            {
                Id = endpointId,
                AppId = appId,
                Url = "https://example.invalid/webhook",
                Status = EndpointStatus.Active
            });
        }

        if (existingEventType is null)
        {
            db.EventTypes.Add(new EventType
            {
                Id = eventTypeId,
                AppId = appId,
                Name = "test.event"
            });
        }

        if (existingEndpoint is null || existingEventType is null)
        {
            await db.SaveChangesAsync();
        }

        return (endpointId, eventTypeId);
    }

    private WebhookDbContext NewDb()
    {
        var options = new DbContextOptionsBuilder<WebhookDbContext>()
            .UseNpgsql(_fixture.ConnectionString)
            .Options;
        return new WebhookDbContext(options);
    }
}

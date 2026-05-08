using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using WebhookEngine.Core.Entities;
using WebhookEngine.Core.Enums;
using WebhookEngine.Core.Interfaces;
using WebhookEngine.Core.Models;
using WebhookEngine.Core.Options;
using WebhookEngine.Core.StateMachine;
using WebhookEngine.Infrastructure.Data;
using WebhookEngine.Infrastructure.Queue;
using WebhookEngine.Infrastructure.Repositories;
using WebhookEngine.Infrastructure.Services;
using WebhookEngine.Worker;
using ApplicationEntity = WebhookEngine.Core.Entities.Application;

namespace WebhookEngine.Infrastructure.Tests.EndToEnd;

public class DeliveryFlowEndToEndTests : IClassFixture<PostgresFixture>
{
    private readonly PostgresFixture _fixture;

    public DeliveryFlowEndToEndTests(PostgresFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Successful_Delivery_Marks_Message_As_Delivered()
    {
        await _fixture.ResetAsync();

        var deliveryService = Substitute.For<IDeliveryService>();
        deliveryService
            .DeliverAsync(Arg.Any<DeliveryRequest>(), Arg.Any<CancellationToken>())
            .Returns(new DeliveryResult { Success = true, StatusCode = 200, LatencyMs = 42 });

        await using var sp = BuildServiceProvider(deliveryService);
        var seed = await SeedAsync(sp);
        await EnqueueMessageAsync(sp, seed);

        await RunWorkerOnceAsync(sp);

        var message = await GetMessageAsync(sp, seed.MessageId);
        message.Status.Should().Be(MessageStatus.Delivered);
        message.AttemptCount.Should().Be(1);
        message.DeliveredAt.Should().NotBeNull();
    }

    [Fact]
    public async Task Failed_Delivery_Increments_Attempt_And_Schedules_Retry()
    {
        await _fixture.ResetAsync();

        var deliveryService = Substitute.For<IDeliveryService>();
        deliveryService
            .DeliverAsync(Arg.Any<DeliveryRequest>(), Arg.Any<CancellationToken>())
            .Returns(new DeliveryResult { Success = false, StatusCode = 500, Error = "Server error", LatencyMs = 30 });

        await using var sp = BuildServiceProvider(deliveryService);
        var seed = await SeedAsync(sp);
        await EnqueueMessageAsync(sp, seed);

        await RunWorkerOnceAsync(sp);

        var message = await GetMessageAsync(sp, seed.MessageId);
        message.Status.Should().Be(MessageStatus.Failed);
        message.AttemptCount.Should().Be(1);
        message.DeliveredAt.Should().BeNull();
        // RetryScheduler will move it back to Pending later; for now the row
        // sits as Failed with a future ScheduledAt set by the worker.
        message.ScheduledAt.Should().BeAfter(DateTime.UtcNow.AddSeconds(-5));

        // The error landed on the attempt row (one-to-many), not the message
        // itself; verify it survived the round-trip.
        using var scope = sp.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<WebhookDbContext>();
        var attempt = await db.MessageAttempts.AsNoTracking()
            .Where(a => a.MessageId == seed.MessageId)
            .OrderByDescending(a => a.CreatedAt)
            .FirstAsync();
        attempt.Status.Should().Be(AttemptStatus.Failed);
    }

    [Fact]
    public async Task Exhausted_Retries_Move_Message_To_DeadLetter()
    {
        await _fixture.ResetAsync();

        var deliveryService = Substitute.For<IDeliveryService>();
        deliveryService
            .DeliverAsync(Arg.Any<DeliveryRequest>(), Arg.Any<CancellationToken>())
            .Returns(new DeliveryResult { Success = false, StatusCode = 500, Error = "Server error", LatencyMs = 30 });

        await using var sp = BuildServiceProvider(deliveryService);
        var seed = await SeedAsync(sp);
        // Pre-set the message to its last allowed attempt so the next failure
        // pushes it across the dead-letter threshold without us needing to
        // wait for seven real retry cycles.
        await EnqueueMessageAsync(sp, seed, attemptCount: 6, maxRetries: 7);

        await RunWorkerOnceAsync(sp);

        var message = await GetMessageAsync(sp, seed.MessageId);
        message.Status.Should().Be(MessageStatus.DeadLetter);
        message.AttemptCount.Should().Be(7);
    }

    [Fact]
    public async Task Rate_Limited_Endpoint_Defers_Excess_Messages_To_Pending()
    {
        await _fixture.ResetAsync();

        var deliveryService = Substitute.For<IDeliveryService>();
        deliveryService
            .DeliverAsync(Arg.Any<DeliveryRequest>(), Arg.Any<CancellationToken>())
            .Returns(new DeliveryResult { Success = true, StatusCode = 200, LatencyMs = 10 });

        await using var sp = BuildServiceProvider(deliveryService);

        // Endpoint allows exactly one delivery per minute. Of the three messages
        // we enqueue, the first should drain through the limiter and deliver;
        // the remaining two should be reset to Pending with a future
        // ScheduledAt rather than failing or moving to DeadLetter.
        var seed = await SeedAsync(sp, rateLimitPerMinute: 1);
        var messageIds = await EnqueueMessagesAsync(sp, seed, count: 3);

        await RunWorkerOnceAsync(sp);

        using var scope = sp.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<WebhookDbContext>();
        var messages = await db.Messages.AsNoTracking()
            .Where(m => messageIds.Contains(m.Id))
            .ToListAsync();

        var delivered = messages.Where(m => m.Status == MessageStatus.Delivered).ToList();
        var deferred = messages.Where(m => m.Status == MessageStatus.Pending).ToList();

        delivered.Should().HaveCount(1);
        deferred.Should().HaveCount(2);

        deferred.Should().OnlyContain(m => m.ScheduledAt > DateTime.UtcNow);
        deferred.Should().OnlyContain(m => m.LockedAt == null && m.LockedBy == null);
        deferred.Should().OnlyContain(m => m.AttemptCount == 0);

        // The delivery side-effect happened exactly once — the limiter must
        // turn the other two attempts into reschedules without ever calling
        // the delivery service for them.
        await deliveryService.Received(1).DeliverAsync(Arg.Any<DeliveryRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task App_Rate_Limited_Application_Defers_Excess_Messages_To_Pending()
    {
        await _fixture.ResetAsync();

        var deliveryService = Substitute.For<IDeliveryService>();
        deliveryService
            .DeliverAsync(Arg.Any<DeliveryRequest>(), Arg.Any<CancellationToken>())
            .Returns(new DeliveryResult { Success = true, StatusCode = 200, LatencyMs = 10 });

        await using var sp = BuildServiceProvider(deliveryService);

        // App's per-second cap is 1; we enqueue three messages back-to-back.
        // The first one drains through the limiter; the next two should be
        // rescheduled (Pending + future ScheduledAt) without ever calling the
        // delivery service. This proves the app-level gate runs *before* the
        // endpoint-level gate (and before signing). The worker spins for only
        // 500 ms so we don't accidentally cross into the next 1-second window
        // and burn a second token before the assertion.
        var seed = await SeedAsync(sp, appRateLimitPerSecond: 1);
        var messageIds = await EnqueueMessagesAsync(sp, seed, count: 3);

        var worker = sp.GetRequiredService<DeliveryWorker>();
        using var cts = new CancellationTokenSource();
        await worker.StartAsync(cts.Token);
        await Task.Delay(500);
        await cts.CancelAsync();
        await worker.StopAsync(CancellationToken.None);

        using var scope = sp.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<WebhookDbContext>();
        var messages = await db.Messages.AsNoTracking()
            .Where(m => messageIds.Contains(m.Id))
            .ToListAsync();

        var delivered = messages.Where(m => m.Status == MessageStatus.Delivered).ToList();
        var deferred = messages.Where(m => m.Status == MessageStatus.Pending).ToList();

        delivered.Should().HaveCount(1);
        deferred.Should().HaveCount(2);

        deferred.Should().OnlyContain(m => m.ScheduledAt > DateTime.UtcNow);
        deferred.Should().OnlyContain(m => m.LockedAt == null && m.LockedBy == null);
        deferred.Should().OnlyContain(m => m.AttemptCount == 0);

        await deliveryService.Received(1).DeliverAsync(Arg.Any<DeliveryRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Allowlist_Mismatch_Sends_Message_To_DeadLetter_Without_Calling_Delivery()
    {
        await _fixture.ResetAsync();

        var deliveryService = Substitute.For<IDeliveryService>();
        deliveryService
            .DeliverAsync(Arg.Any<DeliveryRequest>(), Arg.Any<CancellationToken>())
            .Returns(new DeliveryResult { Success = true, StatusCode = 200, LatencyMs = 10 });

        await using var sp = BuildServiceProvider(deliveryService);

        // The endpoint hostname (example.invalid) cannot resolve at all under
        // RFC 6761, so AllAddressesAllowed returns false on an empty resolved
        // set with a non-empty allowlist. Either way (resolution failure or
        // resolved IP outside the list), the worker takes the DeadLetter path
        // before signing, so DeliverAsync is never called.
        var seed = await SeedAsync(sp, allowedIps: ["203.0.113.0/24"]);
        var messageIds = await EnqueueMessagesAsync(sp, seed, count: 1);

        await RunWorkerOnceAsync(sp);

        using var scope = sp.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<WebhookDbContext>();
        var message = await db.Messages.AsNoTracking().SingleAsync(m => m.Id == messageIds[0]);

        message.Status.Should().Be(MessageStatus.DeadLetter);
        await deliveryService.DidNotReceiveWithAnyArgs().DeliverAsync(default!, default);
    }

    [Fact]
    public async Task Repeated_Failures_Open_Endpoint_Circuit()
    {
        await _fixture.ResetAsync();

        var deliveryService = Substitute.For<IDeliveryService>();
        deliveryService
            .DeliverAsync(Arg.Any<DeliveryRequest>(), Arg.Any<CancellationToken>())
            .Returns(new DeliveryResult { Success = false, StatusCode = 500, Error = "Server error", LatencyMs = 30 });

        await using var sp = BuildServiceProvider(deliveryService);
        var seed = await SeedAsync(sp);

        // Default circuit-breaker threshold is 5 consecutive failures, so
        // we enqueue and process six independent messages back-to-back to
        // make sure the circuit moves to Open.
        for (var i = 0; i < 6; i++)
        {
            await EnqueueMessageAsync(sp, seed, suffix: i.ToString());
            await RunWorkerOnceAsync(sp);
        }

        using var scope = sp.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<WebhookDbContext>();
        var health = await db.EndpointHealths.AsNoTracking()
            .FirstAsync(h => h.EndpointId == seed.EndpointId);

        health.CircuitState.Should().Be(CircuitState.Open);
        health.ConsecutiveFailures.Should().BeGreaterThanOrEqualTo(5);
    }

    // ── Plumbing ───────────────────────────────────────

    private ServiceProvider BuildServiceProvider(IDeliveryService deliveryServiceMock)
    {
        var services = new ServiceCollection();

        services.AddLogging(b => b.SetMinimumLevel(LogLevel.Warning));

        services.AddDbContext<WebhookDbContext>(o => o.UseNpgsql(_fixture.ConnectionString));

        services.AddScoped<ApplicationRepository>();
        services.AddScoped<EndpointRepository>();
        services.AddScoped<MessageRepository>();
        services.AddScoped<EventTypeRepository>();

        services.AddScoped<IMessageQueue, PostgresMessageQueue>();
        services.AddSingleton<ISigningService, HmacSigningService>();
        services.AddScoped<IEndpointHealthTracker, EndpointHealthTracker>();
        services.AddSingleton<IEndpointRateLimiter, EndpointRateLimiter>();
        services.AddSingleton<IApplicationRateLimiter, ApplicationRateLimiter>();
        services.AddSingleton<IMessageStateMachine, MessageStateMachine>();

        services.AddSingleton<IDeliveryService>(deliveryServiceMock);

        services.Configure<DeliveryOptions>(o =>
        {
            o.PollIntervalMs = 50;
            o.BatchSize = 10;
        });
        services.Configure<RetryPolicyOptions>(_ => { });
        services.Configure<CircuitBreakerOptions>(_ => { });
        services.Configure<TransformationOptions>(_ => { });

        services.AddSingleton<IPayloadTransformer, JmesPathPayloadTransformer>();

        services.AddSingleton<DeliveryWorker>();

        return services.BuildServiceProvider();
    }

    /// <summary>
    /// Spins the worker up just long enough to drain the queue once. The 50 ms
    /// poll interval combined with a 1.2 s wait gives ~20 polling chances —
    /// far more than the single iteration each scenario needs.
    /// </summary>
    private static async Task RunWorkerOnceAsync(IServiceProvider sp)
    {
        var worker = sp.GetRequiredService<DeliveryWorker>();
        using var cts = new CancellationTokenSource();

        await worker.StartAsync(cts.Token);
        await Task.Delay(1200);
        await cts.CancelAsync();
        await worker.StopAsync(CancellationToken.None);
    }

    private static async Task<SeedIds> SeedAsync(
        IServiceProvider sp,
        int? rateLimitPerMinute = null,
        int? appRateLimitPerSecond = null,
        IReadOnlyList<string>? allowedIps = null)
    {
        using var scope = sp.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<WebhookDbContext>();

        var app = new ApplicationEntity
        {
            Id = Guid.NewGuid(),
            Name = "E2E App",
            ApiKeyPrefix = "whe_e2e_",
            ApiKeyHash = "hash",
            SigningSecret = "whsec_e2e_secret_for_signing_messages_in_tests",
            RateLimitPerSecond = appRateLimitPerSecond
        };

        var metadataJson = rateLimitPerMinute is int rl
            ? $$"""{"rateLimitPerMinute":{{rl}}}"""
            : "{}";

        var endpoint = new Endpoint
        {
            Id = Guid.NewGuid(),
            AppId = app.Id,
            Url = "https://example.invalid/webhook",
            Status = EndpointStatus.Active,
            MetadataJson = metadataJson,
            AllowedIpsJson = allowedIps is { Count: > 0 }
                ? System.Text.Json.JsonSerializer.Serialize(allowedIps)
                : null
        };

        var eventType = new EventType
        {
            Id = Guid.NewGuid(),
            AppId = app.Id,
            Name = "test.event"
        };

        db.Applications.Add(app);
        db.Endpoints.Add(endpoint);
        db.EventTypes.Add(eventType);
        await db.SaveChangesAsync();

        return new SeedIds(app.Id, endpoint.Id, eventType.Id, Guid.Empty);
    }

    private static async Task EnqueueMessageAsync(
        IServiceProvider sp,
        SeedIds seed,
        string suffix = "",
        int attemptCount = 0,
        int maxRetries = 7)
    {
        using var scope = sp.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<WebhookDbContext>();

        var message = new Message
        {
            Id = Guid.NewGuid(),
            AppId = seed.AppId,
            EndpointId = seed.EndpointId,
            EventTypeId = seed.EventTypeId,
            EventId = $"evt_{suffix}_{Guid.NewGuid():N}"[..32],
            Payload = """{"hello":"world"}""",
            Status = MessageStatus.Pending,
            AttemptCount = attemptCount,
            MaxRetries = maxRetries,
            ScheduledAt = DateTime.UtcNow.AddSeconds(-1)
        };

        db.Messages.Add(message);
        await db.SaveChangesAsync();

        // Seed the message id back into the seed record for the caller to read.
        seed.LastMessageIdSetter(message.Id);
    }

    private static async Task<List<Guid>> EnqueueMessagesAsync(IServiceProvider sp, SeedIds seed, int count)
    {
        using var scope = sp.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<WebhookDbContext>();

        var ids = new List<Guid>(count);
        for (var i = 0; i < count; i++)
        {
            var message = new Message
            {
                Id = Guid.NewGuid(),
                AppId = seed.AppId,
                EndpointId = seed.EndpointId,
                EventTypeId = seed.EventTypeId,
                EventId = $"evt_batch_{i}_{Guid.NewGuid():N}"[..32],
                Payload = """{"hello":"world"}""",
                Status = MessageStatus.Pending,
                AttemptCount = 0,
                MaxRetries = 7,
                ScheduledAt = DateTime.UtcNow.AddSeconds(-1)
            };

            db.Messages.Add(message);
            ids.Add(message.Id);
        }

        await db.SaveChangesAsync();
        return ids;
    }

    private static async Task<Message> GetMessageAsync(IServiceProvider sp, Guid messageId)
    {
        using var scope = sp.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<WebhookDbContext>();
        return await db.Messages.AsNoTracking().FirstAsync(m => m.Id == messageId);
    }

    private sealed class SeedIds
    {
        public SeedIds(Guid appId, Guid endpointId, Guid eventTypeId, Guid messageId)
        {
            AppId = appId;
            EndpointId = endpointId;
            EventTypeId = eventTypeId;
            MessageId = messageId;
        }

        public Guid AppId { get; }
        public Guid EndpointId { get; }
        public Guid EventTypeId { get; }
        public Guid MessageId { get; private set; }

        // Stored as a delegate so the immutable-ish record can still be
        // updated by the helper that creates the message row.
        public Action<Guid> LastMessageIdSetter => id => MessageId = id;
    }
}

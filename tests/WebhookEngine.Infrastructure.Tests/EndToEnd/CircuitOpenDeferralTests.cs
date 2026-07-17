using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
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

// B4: the DeliveryWorker circuit-open branch is a pure reschedule — no attempt
// increment, no dead-letter, and the next-try is clamped into the future. Driven
// through the real worker against Testcontainers with a seeded Open circuit.
public class CircuitOpenDeferralTests : IClassFixture<PostgresFixture>
{
    private const int PollIntervalMs = 30_000;

    private readonly PostgresFixture _fixture;

    public CircuitOpenDeferralTests(PostgresFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Circuit_Open_With_Future_Cooldown_Reschedules_To_Cooldown_Without_Attempt_Or_DeadLetter()
    {
        await _fixture.ResetAsync();

        var deliveryService = Substitute.For<IDeliveryService>();
        deliveryService
            .DeliverAsync(Arg.Any<DeliveryRequest>(), Arg.Any<CancellationToken>())
            .Returns(new DeliveryResult { Success = true, StatusCode = 200, LatencyMs = 10 });

        await using var sp = BuildServiceProvider(deliveryService);
        var seed = await SeedAsync(sp);
        var cooldownUntil = DateTime.UtcNow.AddMinutes(10);
        await SeedOpenCircuitAsync(sp, seed.EndpointId, cooldownUntil);

        // AttemptCount at MaxRetries-1 is the exact premature-dead-letter case B4 fixes:
        // the pre-fix branch would dead-letter here instead of deferring.
        var messageId = await EnqueueMessageAsync(sp, seed, attemptCount: 6, maxRetries: 7);

        await RunWorkerOnceAsync(sp);

        using var scope = sp.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<WebhookDbContext>();
        var message = await db.Messages.AsNoTracking().SingleAsync(m => m.Id == messageId);
        var health = await db.EndpointHealths.AsNoTracking().SingleAsync(h => h.EndpointId == seed.EndpointId);

        message.Status.Should().Be(MessageStatus.Pending, "a circuit-open deferral requeues the message, it must not dead-letter");
        message.AttemptCount.Should().Be(6, "a circuit-open deferral must not consume the retry budget");
        message.ScheduledAt.Should().BeCloseTo(health.CooldownUntil!.Value, TimeSpan.FromMilliseconds(1), "the message is deferred to the endpoint's cooldown");
        message.ScheduledAt.Should().BeAfter(DateTime.UtcNow, "a future cooldown means the reschedule is in the future");
        message.LockedBy.Should().BeNull("the deferral clears the lock");
        message.LockedAt.Should().BeNull();

        var attemptCount = await db.MessageAttempts.CountAsync(a => a.MessageId == messageId);
        attemptCount.Should().Be(0, "a circuit-open deferral records no delivery attempt");

        await deliveryService.DidNotReceiveWithAnyArgs().DeliverAsync(default!, default);
    }

    [Fact]
    public async Task Circuit_Open_With_Past_Cooldown_Clamps_ScheduledAt_To_Future()
    {
        await _fixture.ResetAsync();

        var deliveryService = Substitute.For<IDeliveryService>();
        deliveryService
            .DeliverAsync(Arg.Any<DeliveryRequest>(), Arg.Any<CancellationToken>())
            .Returns(new DeliveryResult { Success = true, StatusCode = 200, LatencyMs = 10 });

        await using var sp = BuildServiceProvider(deliveryService);
        var seed = await SeedAsync(sp);
        // Cooldown already elapsed — the reschedule must clamp forward, never leave
        // the message due in the past (which would hot-loop the queue).
        var pastCooldown = DateTime.UtcNow.AddMinutes(-5);
        await SeedOpenCircuitAsync(sp, seed.EndpointId, pastCooldown);

        var messageId = await EnqueueMessageAsync(sp, seed, attemptCount: 0, maxRetries: 7);

        await RunWorkerOnceAsync(sp);

        using var scope = sp.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<WebhookDbContext>();
        var message = await db.Messages.AsNoTracking().SingleAsync(m => m.Id == messageId);

        message.Status.Should().Be(MessageStatus.Pending);
        message.AttemptCount.Should().Be(0, "a circuit-open deferral must not consume the retry budget");
        message.ScheduledAt.Should().BeAfter(DateTime.UtcNow, "a past cooldown must be clamped into the future");
        message.ScheduledAt.Should().BeCloseTo(DateTime.UtcNow.AddMilliseconds(PollIntervalMs), TimeSpan.FromSeconds(10), "the clamp uses now + PollIntervalMs");

        var attemptCount = await db.MessageAttempts.CountAsync(a => a.MessageId == messageId);
        attemptCount.Should().Be(0, "a circuit-open deferral records no delivery attempt");

        await deliveryService.DidNotReceiveWithAnyArgs().DeliverAsync(default!, default);
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

        services.AddSingleton(deliveryServiceMock);

        // Large poll interval keeps the past-cooldown clamp comfortably in the future
        // and stops the single seeded message from being re-processed within the run.
        services.Configure<DeliveryOptions>(o =>
        {
            o.PollIntervalMs = PollIntervalMs;
            o.BatchSize = 10;
        });
        services.Configure<RetryPolicyOptions>(_ => { });
        services.Configure<CircuitBreakerOptions>(_ => { });
        services.Configure<TransformationOptions>(_ => { });

        services.AddSingleton<IPayloadTransformer, JmesPathPayloadTransformer>();

        services.AddSingleton<DeliveryWorker>();

        return services.BuildServiceProvider();
    }

    private static async Task RunWorkerOnceAsync(IServiceProvider sp)
    {
        var worker = sp.GetRequiredService<DeliveryWorker>();
        using var cts = new CancellationTokenSource();

        await worker.StartAsync(cts.Token);
        await Task.Delay(1200);
        await cts.CancelAsync();
        await worker.StopAsync(CancellationToken.None);
    }

    private static async Task<SeedIds> SeedAsync(IServiceProvider sp)
    {
        using var scope = sp.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<WebhookDbContext>();

        var app = new ApplicationEntity
        {
            Id = Guid.NewGuid(),
            Name = "Circuit Open App",
            ApiKeyPrefix = "whe_circ_",
            ApiKeyHash = "hash",
            SigningSecret = "whsec_circ_secret_for_signing_messages_in_tests"
        };

        var endpoint = new Endpoint
        {
            Id = Guid.NewGuid(),
            AppId = app.Id,
            Url = "https://example.invalid/webhook",
            Status = EndpointStatus.Active
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

        return new SeedIds(app.Id, endpoint.Id, eventType.Id);
    }

    private static async Task SeedOpenCircuitAsync(IServiceProvider sp, Guid endpointId, DateTime cooldownUntil)
    {
        using var scope = sp.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<WebhookDbContext>();

        db.EndpointHealths.Add(new EndpointHealth
        {
            EndpointId = endpointId,
            CircuitState = CircuitState.Open,
            ConsecutiveFailures = 5,
            CooldownUntil = cooldownUntil,
            LastFailureAt = DateTime.UtcNow.AddMinutes(-1)
        });
        await db.SaveChangesAsync();
    }

    private static async Task<Guid> EnqueueMessageAsync(IServiceProvider sp, SeedIds seed, int attemptCount, int maxRetries)
    {
        using var scope = sp.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<WebhookDbContext>();

        var message = new Message
        {
            Id = Guid.NewGuid(),
            AppId = seed.AppId,
            EndpointId = seed.EndpointId,
            EventTypeId = seed.EventTypeId,
            Payload = """{"hello":"world"}""",
            Status = MessageStatus.Pending,
            AttemptCount = attemptCount,
            MaxRetries = maxRetries,
            ScheduledAt = DateTime.UtcNow.AddSeconds(-1)
        };

        db.Messages.Add(message);
        await db.SaveChangesAsync();
        return message.Id;
    }

    private sealed record SeedIds(Guid AppId, Guid EndpointId, Guid EventTypeId);
}

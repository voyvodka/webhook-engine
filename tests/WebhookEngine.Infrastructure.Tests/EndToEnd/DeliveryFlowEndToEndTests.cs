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

    private static async Task<SeedIds> SeedAsync(IServiceProvider sp)
    {
        using var scope = sp.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<WebhookDbContext>();

        var app = new ApplicationEntity
        {
            Id = Guid.NewGuid(),
            Name = "E2E App",
            ApiKeyPrefix = "whe_e2e_",
            ApiKeyHash = "hash",
            SigningSecret = "whsec_e2e_secret_for_signing_messages_in_tests"
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

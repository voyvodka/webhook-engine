using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using WebhookEngine.Core.Entities;
using WebhookEngine.Core.Enums;
using WebhookEngine.Core.Interfaces;
using WebhookEngine.Core.Utilities;
using WebhookEngine.Infrastructure.Data;

namespace WebhookEngine.API.Services;

public interface IDevTrafficGenerator
{
    bool IsEnabled { get; }
    DevTrafficStatus GetStatus();
    Task<DevTrafficStatus> StartAsync(DevTrafficStartRequest request, CancellationToken ct = default);
    Task<DevTrafficStatus> StopAsync(CancellationToken ct = default);
    Task<DevTrafficSeedResult> SeedOnceAsync(DevTrafficSeedRequest request, CancellationToken ct = default);
}

public class DevTrafficGenerator : IDevTrafficGenerator, IDisposable
{
    private const int QueueDepthSoftLimit = 500;
    private const int DefaultRateLimitPerMinute = 60;

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IWebHostEnvironment _environment;
    private readonly ILogger<DevTrafficGenerator> _logger;
    // _stateLock protects only orchestrator lifecycle fields below
    private readonly object _stateLock = new();
    private readonly TrafficScheduler _scheduler = new();

    private CancellationTokenSource? _loopCts;
    private Task? _loopTask;
    private Guid? _appIdFilter;
    private int _intervalMs = 1500;
    private int _messagesPerTick = 6;
    private DateTime? _startedAtUtc;
    private DateTime? _lastSeedAtUtc;
    private int _lastEnqueuedCount;
    private string? _lastError;

    public DevTrafficGenerator(
        IServiceScopeFactory scopeFactory,
        IWebHostEnvironment environment,
        ILogger<DevTrafficGenerator> logger)
    {
        _scopeFactory = scopeFactory;
        _environment = environment;
        _logger = logger;
    }

    public bool IsEnabled => _environment.IsDevelopment() || IsDebugBuild();

    public DevTrafficStatus GetStatus()
    {
        lock (_stateLock)
        {
            return new DevTrafficStatus
            {
                Enabled = IsEnabled,
                Running = _loopCts is not null,
                AppId = _appIdFilter,
                IntervalMs = _intervalMs,
                MessagesPerTick = _messagesPerTick,
                StartedAtUtc = _startedAtUtc,
                LastSeedAtUtc = _lastSeedAtUtc,
                LastEnqueuedCount = _lastEnqueuedCount,
                LastError = _lastError
            };
        }
    }

    public Task<DevTrafficStatus> StartAsync(DevTrafficStartRequest request, CancellationToken ct = default)
    {
        if (!IsEnabled)
            return Task.FromResult(GetStatus());

        lock (_stateLock)
        {
            _appIdFilter = request.AppId;
            _intervalMs = Math.Clamp(request.IntervalMs <= 0 ? 1500 : request.IntervalMs, 250, 60_000);
            _messagesPerTick = Math.Clamp(request.MessagesPerTick <= 0 ? 6 : request.MessagesPerTick, 1, 25);
            _lastError = null;

            if (_loopCts is not null)
                return Task.FromResult(GetStatus());

            _loopCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            _startedAtUtc = DateTime.UtcNow;
            _loopTask = Task.Run(() => RunLoopAsync(_loopCts.Token), _loopCts.Token);
        }

        return Task.FromResult(GetStatus());
    }

    public async Task<DevTrafficStatus> StopAsync(CancellationToken ct = default)
    {
        CancellationTokenSource? cts;
        Task? loopTask;

        lock (_stateLock)
        {
            cts = _loopCts;
            loopTask = _loopTask;

            _loopCts = null;
            _loopTask = null;
            _startedAtUtc = null;
        }

        if (cts is null)
            return GetStatus();

        cts.Cancel();

        if (loopTask is not null)
        {
            try
            {
                await loopTask.WaitAsync(ct);
            }
            catch (OperationCanceledException)
            {
                // normal
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Dev traffic loop stopped with error");
            }
        }

        cts.Dispose();
        _scheduler.Clear();
        return GetStatus();
    }

    public async Task<DevTrafficSeedResult> SeedOnceAsync(DevTrafficSeedRequest request, CancellationToken ct = default)
    {
        if (!IsEnabled)
        {
            return new DevTrafficSeedResult
            {
                Enabled = false,
                EnqueuedMessages = 0,
                TargetedEndpoints = 0,
                ActiveApplications = 0,
                SeededAtUtc = DateTime.UtcNow,
                Error = "Dev traffic generator is only enabled in Development or DEBUG builds."
            };
        }

        var appId = request.AppId;
        var messages = Math.Clamp(request.Messages <= 0 ? _messagesPerTick : request.Messages, 1, 50);

        var result = await GenerateOnceAsync(appId, messages, ct);

        lock (_stateLock)
        {
            _lastSeedAtUtc = result.SeededAtUtc;
            _lastEnqueuedCount = result.EnqueuedMessages;
            _lastError = result.Error;
        }

        return result;
    }

    public void Dispose()
    {
        _loopCts?.Cancel();
        _loopCts?.Dispose();
    }

    private async Task RunLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            Guid? appId;
            int intervalMs;
            int messagesPerTick;

            lock (_stateLock)
            {
                appId = _appIdFilter;
                intervalMs = _intervalMs;
                messagesPerTick = _messagesPerTick;
            }

            try
            {
                var result = await GenerateOnceAsync(appId, messagesPerTick, ct);

                lock (_stateLock)
                {
                    _lastSeedAtUtc = result.SeededAtUtc;
                    _lastEnqueuedCount = result.EnqueuedMessages;
                    _lastError = result.Error;
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Dev traffic tick failed");
                lock (_stateLock)
                {
                    _lastError = ex.Message;
                }
            }

            try
            {
                await Task.Delay(intervalMs, ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                break;
            }
        }
    }

    private async Task<DevTrafficSeedResult> GenerateOnceAsync(Guid? appId, int maxMessages, CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<WebhookDbContext>();
        var queue = scope.ServiceProvider.GetRequiredService<IMessageQueue>();

        var queueDepth = await db.Messages
            .AsNoTracking()
            .CountAsync(m => m.Status == MessageStatus.Pending || m.Status == MessageStatus.Sending, ct);

        if (queueDepth >= QueueDepthSoftLimit)
        {
            return new DevTrafficSeedResult
            {
                Enabled = true,
                EnqueuedMessages = 0,
                TargetedEndpoints = 0,
                ActiveApplications = 0,
                SeededAtUtc = DateTime.UtcNow,
                Error = $"Queue depth is high ({queueDepth}). Dev generator paused to avoid backlog."
            };
        }

        var endpointsQuery = db.Endpoints
            .AsNoTracking()
            .Include(e => e.EventTypes)
            .Where(e => e.Status != EndpointStatus.Disabled && e.Application.IsActive);

        if (appId.HasValue)
        {
            endpointsQuery = endpointsQuery.Where(e => e.AppId == appId.Value);
        }

        var endpoints = await endpointsQuery.ToListAsync(ct);

        if (endpoints.Count == 0)
        {
            return new DevTrafficSeedResult
            {
                Enabled = true,
                EnqueuedMessages = 0,
                TargetedEndpoints = 0,
                ActiveApplications = 0,
                SeededAtUtc = DateTime.UtcNow,
                Error = "No active endpoints found."
            };
        }

        var appIds = endpoints.Select(e => e.AppId).Distinct().ToList();
        var eventTypes = await db.EventTypes
            .Where(et => appIds.Contains(et.AppId) && !et.IsArchived)
            .ToListAsync(ct);

        var missingEventTypeAppIds = appIds.Except(eventTypes.Select(et => et.AppId)).ToList();
        if (missingEventTypeAppIds.Count > 0)
        {
            var newEventTypes = missingEventTypeAppIds.Select(id => new EventType
            {
                AppId = id,
                Name = "order.created",
                Description = "Auto-generated for development traffic"
            }).ToList();

            db.EventTypes.AddRange(newEventTypes);
            await db.SaveChangesAsync(ct);
            eventTypes.AddRange(newEventTypes);
        }

        var now = DateTime.UtcNow;
        var endpointProfiles = endpoints
            .Select(endpoint =>
            {
                var configuredRate = RateLimitResolver.ResolveRateLimitPerMinute(endpoint.MetadataJson);
                var effectiveRate = configuredRate is > 0 ? configuredRate.Value : DefaultRateLimitPerMinute;
                return EndpointTrafficProfiler.BuildProfile(endpoint, effectiveRate);
            })
            .ToList();

        var readyProfiles = endpointProfiles
            .Where(profile => _scheduler.IsReady(profile.Endpoint.Id, now))
            .ToList();

        if (readyProfiles.Count == 0)
        {
            return new DevTrafficSeedResult
            {
                Enabled = true,
                EnqueuedMessages = 0,
                TargetedEndpoints = 0,
                ActiveApplications = appIds.Count,
                SeededAtUtc = now
            };
        }

        var boundedMaxMessages = Math.Clamp(maxMessages, 1, 25);

        // Distribute messages across ready endpoints (multiple messages per endpoint allowed)
        var selectedProfiles = EndpointTrafficProfiler.SelectForTick(readyProfiles, Math.Min(boundedMaxMessages, readyProfiles.Count));
        var assignments = DistributeMessages(selectedProfiles, boundedMaxMessages);

        var enqueuedCount = 0;
        var messageIds = new List<Guid>(boundedMaxMessages);

        foreach (var (profile, count) in assignments)
        {
            var endpoint = profile.Endpoint;

            var availableEndpointEventTypes = endpoint.EventTypes.Where(et => !et.IsArchived).ToList();
            var appEventTypes = eventTypes.Where(et => et.AppId == endpoint.AppId).ToList();

            for (var i = 0; i < count; i++)
            {
                var selectedEventType = availableEndpointEventTypes.Count > 0
                    ? availableEndpointEventTypes[Random.Shared.Next(availableEndpointEventTypes.Count)]
                    : appEventTypes[Random.Shared.Next(appEventTypes.Count)];

                var orderId = $"ord_{Guid.NewGuid():N}"[..16];
                var customerId = $"cus_{Random.Shared.Next(1000, 9999)}";
                var amount = Math.Round(Random.Shared.NextDouble() * 1900 + 100, 2);

                var payload = new
                {
                    orderId,
                    customerId,
                    amount,
                    currency = "TRY",
                    source = "dashboard.dev-generator",
                    generatedAt = now
                };

                var message = new Message
                {
                    AppId = endpoint.AppId,
                    EndpointId = endpoint.Id,
                    EventTypeId = selectedEventType.Id,
                    EventId = $"evt_{Guid.NewGuid():N}"[..16],
                    Payload = JsonSerializer.Serialize(payload),
                    Status = MessageStatus.Pending,
                    ScheduledAt = now
                };

                await queue.EnqueueAsync(message, ct);
                enqueuedCount++;
                messageIds.Add(message.Id);
            }

            _scheduler.MarkSent(endpoint.Id, profile.EffectiveRatePerMinute, now);
        }

        return new DevTrafficSeedResult
        {
            Enabled = true,
            EnqueuedMessages = enqueuedCount,
            TargetedEndpoints = selectedProfiles.Count,
            ActiveApplications = appIds.Count,
            SeededAtUtc = now,
            MessageIds = messageIds.Select(id => id.ToString()).ToList()
        };
    }

    /// <summary>
    /// Distributes totalMessages across selected profiles, ensuring each gets at least 1.
    /// </summary>
    private static List<(EndpointTrafficProfile Profile, int Count)> DistributeMessages(
        List<EndpointTrafficProfile> profiles, int totalMessages)
    {
        if (profiles.Count == 0)
            return [];

        var baseCount = totalMessages / profiles.Count;
        var remainder = totalMessages % profiles.Count;

        return profiles.Select((p, i) => (p, baseCount + (i < remainder ? 1 : 0))).ToList();
    }

    private static bool IsDebugBuild()
    {
#if DEBUG
        return true;
#else
        return false;
#endif
    }
}

public class DevTrafficStatus
{
    public bool Enabled { get; set; }
    public bool Running { get; set; }
    public Guid? AppId { get; set; }
    public int IntervalMs { get; set; }
    public int MessagesPerTick { get; set; }
    public DateTime? StartedAtUtc { get; set; }
    public DateTime? LastSeedAtUtc { get; set; }
    public int LastEnqueuedCount { get; set; }
    public string? LastError { get; set; }
}

public class DevTrafficStartRequest
{
    public Guid? AppId { get; set; }
    public int IntervalMs { get; set; } = 1500;
    public int MessagesPerTick { get; set; } = 6;
}

public class DevTrafficSeedRequest
{
    public Guid? AppId { get; set; }
    public int Messages { get; set; } = 12;
}

public class DevTrafficSeedResult
{
    public bool Enabled { get; set; }
    public int EnqueuedMessages { get; set; }
    public int TargetedEndpoints { get; set; }
    public int ActiveApplications { get; set; }
    public DateTime SeededAtUtc { get; set; }
    public List<string> MessageIds { get; set; } = [];
    public string? Error { get; set; }
}

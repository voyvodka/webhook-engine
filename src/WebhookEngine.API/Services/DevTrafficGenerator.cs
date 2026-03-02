using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using WebhookEngine.Core.Entities;
using WebhookEngine.Core.Enums;
using WebhookEngine.Core.Interfaces;
using WebhookEngine.Infrastructure.Data;
using EndpointEntity = WebhookEngine.Core.Entities.Endpoint;

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
    private readonly object _stateLock = new();
    private readonly Dictionary<Guid, DateTime> _endpointNextAllowedAtUtc = [];

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
                var configuredRate = ResolveRateLimitPerMinute(endpoint.MetadataJson);
                var effectiveRate = configuredRate is > 0 ? configuredRate.Value : DefaultRateLimitPerMinute;
                return new EndpointTrafficProfile(
                    endpoint,
                    effectiveRate,
                    IsFailureCandidate(endpoint));
            })
            .ToList();

        var readyProfiles = endpointProfiles
            .Where(profile => IsEndpointReady(profile.Endpoint.Id, now))
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
        var selectedProfiles = SelectProfilesForTick(readyProfiles, boundedMaxMessages);

        var enqueuedCount = 0;
        var messageIds = new List<Guid>(selectedProfiles.Count);

        foreach (var profile in selectedProfiles)
        {
            var endpoint = profile.Endpoint;

            var availableEndpointEventTypes = endpoint.EventTypes.Where(et => !et.IsArchived).ToList();
            var selectedEventType = availableEndpointEventTypes.Count > 0
                ? availableEndpointEventTypes[Random.Shared.Next(availableEndpointEventTypes.Count)]
                : eventTypes.First(et => et.AppId == endpoint.AppId);

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
            MarkEndpointSent(endpoint.Id, profile.EffectiveRatePerMinute, now);
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

    private bool IsEndpointReady(Guid endpointId, DateTime now)
    {
        lock (_stateLock)
        {
            if (!_endpointNextAllowedAtUtc.TryGetValue(endpointId, out var nextAllowedAt))
                return true;

            return now >= nextAllowedAt;
        }
    }

    private void MarkEndpointSent(Guid endpointId, int rateLimitPerMinute, DateTime now)
    {
        var intervalMs = Math.Max(250, (int)Math.Ceiling(60_000d / Math.Max(1, rateLimitPerMinute)));
        var nextAllowedAt = now.AddMilliseconds(intervalMs);

        lock (_stateLock)
        {
            _endpointNextAllowedAtUtc[endpointId] = nextAllowedAt;
        }
    }

    private static List<EndpointTrafficProfile> SelectProfilesForTick(List<EndpointTrafficProfile> readyProfiles, int maxMessages)
    {
        if (readyProfiles.Count <= maxMessages)
            return readyProfiles;

        var result = new List<EndpointTrafficProfile>(maxMessages);

        var successCandidates = readyProfiles.Where(p => !p.IsFailureCandidate).OrderBy(_ => Random.Shared.Next()).ToList();
        var failureCandidates = readyProfiles.Where(p => p.IsFailureCandidate).OrderBy(_ => Random.Shared.Next()).ToList();

        if (maxMessages >= 2)
        {
            if (successCandidates.Count > 0)
            {
                result.Add(successCandidates[0]);
                successCandidates.RemoveAt(0);
            }

            if (failureCandidates.Count > 0)
            {
                result.Add(failureCandidates[0]);
                failureCandidates.RemoveAt(0);
            }
        }

        var remaining = readyProfiles
            .Except(result)
            .OrderBy(_ => Random.Shared.Next())
            .Take(Math.Max(0, maxMessages - result.Count))
            .ToList();

        result.AddRange(remaining);
        return result;
    }

    private static int? ResolveRateLimitPerMinute(string? metadataJson)
    {
        if (string.IsNullOrWhiteSpace(metadataJson))
            return null;

        try
        {
            using var document = JsonDocument.Parse(metadataJson);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
                return null;

            if (!document.RootElement.TryGetProperty("rateLimitPerMinute", out var rateLimitElement))
                return null;

            if (rateLimitElement.ValueKind == JsonValueKind.Number && rateLimitElement.TryGetInt32(out var numericValue))
                return numericValue > 0 ? numericValue : null;

            if (rateLimitElement.ValueKind == JsonValueKind.String
                && int.TryParse(rateLimitElement.GetString(), out var stringValue))
            {
                return stringValue > 0 ? stringValue : null;
            }

            return null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static bool IsFailureCandidate(EndpointEntity endpoint)
    {
        if (endpoint.Status == EndpointStatus.Failed || endpoint.Status == EndpointStatus.Degraded)
            return true;

        var url = endpoint.Url.ToLowerInvariant();

        return url.Contains("fail")
            || url.Contains("invalid")
            || url.Contains("unreachable")
            || url.Contains(":5999")
            || url.Contains(":5998")
            || url.Contains(":1/");
    }

    private static bool IsDebugBuild()
    {
#if DEBUG
        return true;
#else
        return false;
#endif
    }

    private sealed record EndpointTrafficProfile(
        EndpointEntity Endpoint,
        int EffectiveRatePerMinute,
        bool IsFailureCandidate);
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

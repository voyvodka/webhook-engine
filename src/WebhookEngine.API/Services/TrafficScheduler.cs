namespace WebhookEngine.API.Services;

/// <summary>
/// Rate-limit-aware scheduler that tracks per-endpoint send timing.
/// Owns its own lock — no shared mutable state with DevTrafficGenerator.
/// </summary>
internal sealed class TrafficScheduler
{
    private readonly Dictionary<Guid, DateTime> _nextAllowedAtUtc = [];
    private readonly object _lock = new();

    /// <summary>
    /// Returns true if the endpoint is ready to receive a message at the given time.
    /// </summary>
    public bool IsReady(Guid endpointId, DateTime now)
    {
        lock (_lock)
        {
            if (!_nextAllowedAtUtc.TryGetValue(endpointId, out var nextAllowedAt))
                return true;

            return now >= nextAllowedAt;
        }
    }

    /// <summary>
    /// Records that a message was sent to the endpoint and calculates when the next send is allowed.
    /// </summary>
    public void MarkSent(Guid endpointId, int rateLimitPerMinute, DateTime now)
    {
        var intervalMs = Math.Max(250, (int)Math.Ceiling(60_000d / Math.Max(1, rateLimitPerMinute)));
        var nextAllowedAt = now.AddMilliseconds(intervalMs);

        lock (_lock)
        {
            _nextAllowedAtUtc[endpointId] = nextAllowedAt;
        }
    }

    /// <summary>
    /// Clears all scheduling state. Called when traffic generation stops.
    /// </summary>
    public void Clear()
    {
        lock (_lock)
        {
            _nextAllowedAtUtc.Clear();
        }
    }
}

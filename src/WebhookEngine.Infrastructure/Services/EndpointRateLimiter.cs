using System.Collections.Concurrent;
using WebhookEngine.Core.Interfaces;

namespace WebhookEngine.Infrastructure.Services;

public class EndpointRateLimiter : IEndpointRateLimiter
{
    private static readonly TimeSpan Window = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan IdleAfter = TimeSpan.FromMinutes(15);
    private static readonly TimeSpan SweepInterval = TimeSpan.FromMinutes(5);

    private readonly ConcurrentDictionary<Guid, WindowState> _windows = new();
    private DateTime _lastSweepUtc = DateTime.UtcNow;

    public bool TryAcquire(Guid endpointId, int limitPerMinute, out DateTime retryAtUtc)
    {
        var now = DateTime.UtcNow;

        // Periodically sweep entries that haven't been touched in 15 minutes
        // so the dictionary doesn't grow with every endpoint we ever saw.
        // Cheap: only walks the keys when at least 5 minutes have passed
        // since the last sweep, and the workload that pays for the walk is
        // already paying to acquire a lock right after.
        if (now - _lastSweepUtc > SweepInterval)
        {
            SweepIdleEntries(now);
            _lastSweepUtc = now;
        }

        if (limitPerMinute <= 0)
        {
            retryAtUtc = now;
            return true;
        }

        var state = _windows.GetOrAdd(endpointId, _ => new WindowState
        {
            WindowStartUtc = now,
            Count = 0
        });

        lock (state.Lock)
        {
            if (now - state.WindowStartUtc >= Window)
            {
                state.WindowStartUtc = now;
                state.Count = 0;
            }

            state.LastUsedUtc = now;

            if (state.Count < limitPerMinute)
            {
                state.Count++;
                retryAtUtc = now;
                return true;
            }

            retryAtUtc = state.WindowStartUtc.Add(Window);
            return false;
        }
    }

    private void SweepIdleEntries(DateTime now)
    {
        foreach (var (endpointId, state) in _windows)
        {
            if (now - state.LastUsedUtc > IdleAfter)
            {
                _windows.TryRemove(endpointId, out _);
            }
        }
    }

    private sealed class WindowState
    {
        public DateTime WindowStartUtc { get; set; }
        public DateTime LastUsedUtc { get; set; } = DateTime.UtcNow;
        public int Count { get; set; }
        public object Lock { get; } = new();
    }
}

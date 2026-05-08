using System.Collections.Concurrent;
using WebhookEngine.Core.Interfaces;

namespace WebhookEngine.Infrastructure.Services;

/// <summary>
/// Singleton 1-second window-counter for application-level rate limiting.
/// Mirrors the structure of <see cref="EndpointRateLimiter"/> so the two
/// limiters compose predictably in the worker. Idle entries (apps that
/// haven't dequeued anything in 15 minutes) are pruned periodically so the
/// dictionary cannot grow without bound.
/// </summary>
public class ApplicationRateLimiter : IApplicationRateLimiter
{
    private static readonly TimeSpan Window = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan IdleAfter = TimeSpan.FromMinutes(15);
    private static readonly TimeSpan SweepInterval = TimeSpan.FromMinutes(5);

    private readonly ConcurrentDictionary<Guid, WindowState> _windows = new();
    private DateTime _lastSweepUtc = DateTime.UtcNow;

    public bool TryAcquire(Guid appId, int limitPerSecond, out DateTime retryAtUtc)
    {
        var now = DateTime.UtcNow;

        if (now - _lastSweepUtc > SweepInterval)
        {
            SweepIdleEntries(now);
            _lastSweepUtc = now;
        }

        if (limitPerSecond <= 0)
        {
            retryAtUtc = now;
            return true;
        }

        var state = _windows.GetOrAdd(appId, _ => new WindowState
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

            if (state.Count < limitPerSecond)
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
        foreach (var (appId, state) in _windows)
        {
            if (now - state.LastUsedUtc > IdleAfter)
            {
                _windows.TryRemove(appId, out _);
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

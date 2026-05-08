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
    // Stored as Ticks so Volatile.Read / Interlocked.Exchange give us atomic
    // 64-bit reads / writes on every platform. A torn DateTime read could
    // otherwise spawn back-to-back sweeps on a 32-bit host, which is harmless
    // but wasteful.
    private long _lastSweepTicks = DateTime.UtcNow.Ticks;

    public bool TryAcquire(Guid appId, int limitPerSecond, out DateTime retryAtUtc)
    {
        var now = DateTime.UtcNow;

        var lastSweepTicks = Volatile.Read(ref _lastSweepTicks);
        if (now.Ticks - lastSweepTicks > SweepInterval.Ticks)
        {
            // CAS so only one caller drives the sweep when a flood of
            // concurrent acquires all crosses the interval boundary at once.
            if (Interlocked.CompareExchange(ref _lastSweepTicks, now.Ticks, lastSweepTicks) == lastSweepTicks)
            {
                SweepIdleEntries(now);
            }
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
            // Read LastUsedUtc under the per-state lock so we can't race a
            // concurrent TryAcquire that's mid-update. Without this the read
            // could be torn on a 32-bit host (DateTime is not atomically read)
            // and we'd evict an entry that's actually warm; the next acquire
            // would just spawn a fresh state with Count=0, leaking one slot
            // in that 1-second window — bounded but worth tightening.
            DateTime lastUsed;
            lock (state.Lock)
            {
                lastUsed = state.LastUsedUtc;
            }

            if (now - lastUsed > IdleAfter)
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

using System.Collections.Concurrent;
using WebhookEngine.Core.Interfaces;

namespace WebhookEngine.Infrastructure.Services;

public class EndpointRateLimiter : IEndpointRateLimiter
{
    private static readonly TimeSpan Window = TimeSpan.FromMinutes(1);

    private readonly ConcurrentDictionary<Guid, WindowState> _windows = new();

    public bool TryAcquire(Guid endpointId, int limitPerMinute, out DateTime retryAtUtc)
    {
        var now = DateTime.UtcNow;

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

    private sealed class WindowState
    {
        public DateTime WindowStartUtc { get; set; }
        public int Count { get; set; }
        public object Lock { get; } = new();
    }
}

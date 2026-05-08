namespace WebhookEngine.Core.Interfaces;

/// <summary>
/// Application-wide deliverability cap, enforced as a 1-second window.
/// Applies before <see cref="IEndpointRateLimiter"/> in the delivery worker
/// so a noisy app cannot drown its sibling apps' fair share.
/// </summary>
public interface IApplicationRateLimiter
{
    /// <summary>
    /// Acquire one slot for <paramref name="appId"/> against the per-second
    /// limit. Returns true and burns a slot when the window has room. Returns
    /// false with <paramref name="retryAtUtc"/> set to the next window start
    /// when the cap is full.
    /// </summary>
    /// <param name="limitPerSecond">
    /// Effective cap. <c>&lt;= 0</c> disables the limiter and always allows.
    /// </param>
    bool TryAcquire(Guid appId, int limitPerSecond, out DateTime retryAtUtc);
}

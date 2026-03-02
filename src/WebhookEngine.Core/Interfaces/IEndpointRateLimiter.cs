namespace WebhookEngine.Core.Interfaces;

public interface IEndpointRateLimiter
{
    bool TryAcquire(Guid endpointId, int limitPerMinute, out DateTime retryAtUtc);
}

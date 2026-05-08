using WebhookEngine.Core.Enums;

namespace WebhookEngine.Core.Interfaces;

/// <summary>
/// Pushes real-time delivery status updates to connected dashboard clients (e.g., via SignalR).
/// </summary>
public interface IDeliveryNotifier
{
    Task NotifyDeliverySuccessAsync(Guid messageId, Guid endpointId, int attemptCount, int latencyMs, CancellationToken ct = default);
    Task NotifyDeliveryFailureAsync(Guid messageId, Guid endpointId, int attemptCount, string? error, CancellationToken ct = default);
    Task NotifyDeadLetterAsync(Guid messageId, Guid endpointId, int attemptCount, CancellationToken ct = default);

    /// <summary>
    /// Fired when an endpoint's circuit-breaker state or visible status changes
    /// (e.g. Closed→Open after consecutive failures, Open→HalfOpen on cooldown
    /// expiry, HalfOpen→Closed on a probe success). Lets the dashboard refresh
    /// the endpoint's health badge in real time without a poll.
    /// </summary>
    Task NotifyEndpointHealthChangedAsync(
        Guid endpointId,
        EndpointStatus endpointStatus,
        CircuitState circuitState,
        int consecutiveFailures,
        DateTime? cooldownUntilUtc,
        CancellationToken ct = default);
}

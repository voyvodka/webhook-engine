namespace WebhookEngine.Core.Interfaces;

/// <summary>
/// Pushes real-time delivery status updates to connected dashboard clients (e.g., via SignalR).
/// </summary>
public interface IDeliveryNotifier
{
    Task NotifyDeliverySuccessAsync(Guid messageId, Guid endpointId, int attemptCount, int latencyMs, CancellationToken ct = default);
    Task NotifyDeliveryFailureAsync(Guid messageId, Guid endpointId, int attemptCount, string? error, CancellationToken ct = default);
    Task NotifyDeadLetterAsync(Guid messageId, Guid endpointId, int attemptCount, CancellationToken ct = default);
}

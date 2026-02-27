using Microsoft.AspNetCore.SignalR;
using WebhookEngine.Core.Interfaces;

namespace WebhookEngine.API.Hubs;

/// <summary>
/// SignalR hub for real-time delivery status updates.
/// Dashboard clients connect to receive live delivery events.
/// </summary>
public class DeliveryHub : Hub
{
    public override async Task OnConnectedAsync()
    {
        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        await base.OnDisconnectedAsync(exception);
    }
}

/// <summary>
/// IDeliveryNotifier implementation that pushes events to all connected SignalR clients.
/// Registered as a singleton in DI so workers can resolve it.
/// </summary>
public class SignalRDeliveryNotifier : IDeliveryNotifier
{
    private readonly IHubContext<DeliveryHub> _hubContext;

    public SignalRDeliveryNotifier(IHubContext<DeliveryHub> hubContext)
    {
        _hubContext = hubContext;
    }

    public async Task NotifyDeliverySuccessAsync(Guid messageId, Guid endpointId, int attemptCount, int latencyMs, CancellationToken ct = default)
    {
        await _hubContext.Clients.All.SendAsync("DeliverySuccess", new
        {
            messageId,
            endpointId,
            attemptCount,
            latencyMs,
            status = "Delivered",
            timestamp = DateTime.UtcNow
        }, ct);
    }

    public async Task NotifyDeliveryFailureAsync(Guid messageId, Guid endpointId, int attemptCount, string? error, CancellationToken ct = default)
    {
        await _hubContext.Clients.All.SendAsync("DeliveryFailure", new
        {
            messageId,
            endpointId,
            attemptCount,
            error,
            status = "Failed",
            timestamp = DateTime.UtcNow
        }, ct);
    }

    public async Task NotifyDeadLetterAsync(Guid messageId, Guid endpointId, int attemptCount, CancellationToken ct = default)
    {
        await _hubContext.Clients.All.SendAsync("DeadLetter", new
        {
            messageId,
            endpointId,
            attemptCount,
            status = "DeadLetter",
            timestamp = DateTime.UtcNow
        }, ct);
    }
}

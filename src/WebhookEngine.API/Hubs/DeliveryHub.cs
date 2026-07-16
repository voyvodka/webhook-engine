using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using WebhookEngine.Core.Enums;
using WebhookEngine.Core.Interfaces;

namespace WebhookEngine.API.Hubs;

// Cookie-only: the event stream spans all applications and carries cross-tenant detail.
[Authorize(AuthenticationSchemes = CookieAuthenticationDefaults.AuthenticationScheme)]
public class DeliveryHub : Hub
{
}

// Singleton IDeliveryNotifier so workers can resolve it; fans events out to all clients.
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

    public async Task NotifyEndpointHealthChangedAsync(
        Guid endpointId,
        EndpointStatus endpointStatus,
        CircuitState circuitState,
        int consecutiveFailures,
        DateTime? cooldownUntilUtc,
        CancellationToken ct = default)
    {
        await _hubContext.Clients.All.SendAsync("EndpointHealthChanged", new
        {
            endpointId,
            // Match the dashboard's existing lowercase string convention
            // (see DashboardEndpointController.ListEndpoints).
            status = endpointStatus.ToString().ToLowerInvariant(),
            circuitState = circuitState.ToString(),
            consecutiveFailures,
            cooldownUntilUtc,
            timestamp = DateTime.UtcNow
        }, ct);
    }
}

using System.Net;

namespace WebhookEngine.Infrastructure.Services;

public static class DeliveryHttpRequestOptions
{
    // Carries the per-endpoint IP allowlist onto the outgoing request so the
    // ConnectCallback enforces it against the same resolution it pins to.
    // Absent when no allowlist is configured (unrestricted egress).
    public static readonly HttpRequestOptionsKey<IReadOnlyList<IPNetwork>> AllowedNetworks =
        new("webhookengine.delivery.allowed-networks");
}

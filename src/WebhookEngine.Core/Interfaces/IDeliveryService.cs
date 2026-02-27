using WebhookEngine.Core.Models;

namespace WebhookEngine.Core.Interfaces;

public interface IDeliveryService
{
    Task<DeliveryResult> DeliverAsync(DeliveryRequest request, CancellationToken ct = default);
}

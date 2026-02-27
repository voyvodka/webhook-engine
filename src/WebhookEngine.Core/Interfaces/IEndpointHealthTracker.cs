using WebhookEngine.Core.Entities;
using WebhookEngine.Core.Enums;

namespace WebhookEngine.Core.Interfaces;

public interface IEndpointHealthTracker
{
    Task RecordSuccessAsync(Guid endpointId, CancellationToken ct = default);
    Task RecordFailureAsync(Guid endpointId, CancellationToken ct = default);
    Task<CircuitState> GetCircuitStateAsync(Guid endpointId, CancellationToken ct = default);
    Task<EndpointHealth?> GetHealthAsync(Guid endpointId, CancellationToken ct = default);
}

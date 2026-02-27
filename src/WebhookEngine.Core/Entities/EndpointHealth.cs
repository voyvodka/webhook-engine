using WebhookEngine.Core.Enums;

namespace WebhookEngine.Core.Entities;

public class EndpointHealth
{
    public Guid EndpointId { get; set; }
    public CircuitState CircuitState { get; set; } = CircuitState.Closed;
    public int ConsecutiveFailures { get; set; }
    public DateTime? LastFailureAt { get; set; }
    public DateTime? LastSuccessAt { get; set; }
    public DateTime? CooldownUntil { get; set; }
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    // Navigation property
    public Endpoint Endpoint { get; set; } = null!;
}

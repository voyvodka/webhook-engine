using WebhookEngine.Core.Enums;

namespace WebhookEngine.Core.Entities;

public class Endpoint
{
    public Guid Id { get; set; }
    public Guid AppId { get; set; }
    public string Url { get; set; } = string.Empty;
    public string? Description { get; set; }
    public EndpointStatus Status { get; set; } = EndpointStatus.Active;
    public string CustomHeadersJson { get; set; } = "{}";
    public string? SecretOverride { get; set; }
    public string MetadataJson { get; set; } = "{}";

    // Payload transformation (ADR-003) — declarative JMESPath reshape applied
    // before delivery. Stored only in Phase 1; pipeline integration arrives in
    // Phase 2 (HttpDeliveryService) and the dashboard editor in Phase 3.
    public string? TransformExpression { get; set; }
    public bool TransformEnabled { get; set; }
    public DateTime? TransformValidatedAt { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    // Navigation properties
    public Application Application { get; set; } = null!;
    public ICollection<EventType> EventTypes { get; set; } = [];
    public ICollection<Message> Messages { get; set; } = [];
    public EndpointHealth? Health { get; set; }
}

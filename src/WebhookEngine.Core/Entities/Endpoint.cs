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
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    // Navigation properties
    public Application Application { get; set; } = null!;
    public ICollection<EventType> EventTypes { get; set; } = [];
    public ICollection<Message> Messages { get; set; } = [];
    public EndpointHealth? Health { get; set; }
}

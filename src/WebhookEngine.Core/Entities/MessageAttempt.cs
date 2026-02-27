using WebhookEngine.Core.Enums;

namespace WebhookEngine.Core.Entities;

public class MessageAttempt
{
    public Guid Id { get; set; }
    public Guid MessageId { get; set; }
    public Guid EndpointId { get; set; }
    public int AttemptNumber { get; set; }
    public AttemptStatus Status { get; set; }
    public int? StatusCode { get; set; }
    public string? RequestHeadersJson { get; set; }
    public string? ResponseBody { get; set; }
    public string? Error { get; set; }
    public int LatencyMs { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // Navigation properties
    public Message Message { get; set; } = null!;
    public Endpoint Endpoint { get; set; } = null!;
}

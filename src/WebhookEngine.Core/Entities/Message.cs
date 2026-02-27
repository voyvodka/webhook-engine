using WebhookEngine.Core.Enums;

namespace WebhookEngine.Core.Entities;

public class Message
{
    public Guid Id { get; set; }
    public Guid AppId { get; set; }
    public Guid EndpointId { get; set; }
    public Guid EventTypeId { get; set; }
    public string? EventId { get; set; }
    public string? IdempotencyKey { get; set; }
    public string Payload { get; set; } = "{}";
    public MessageStatus Status { get; set; } = MessageStatus.Pending;
    public int AttemptCount { get; set; }
    public int MaxRetries { get; set; } = 7;
    public DateTime ScheduledAt { get; set; } = DateTime.UtcNow;
    public DateTime? LockedAt { get; set; }
    public string? LockedBy { get; set; }
    public DateTime? DeliveredAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // Navigation properties
    public Application Application { get; set; } = null!;
    public Endpoint Endpoint { get; set; } = null!;
    public EventType EventType { get; set; } = null!;
    public ICollection<MessageAttempt> Attempts { get; set; } = [];
}

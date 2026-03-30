namespace WebhookEngine.Core.Entities;

public class Application
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string ApiKeyPrefix { get; set; } = string.Empty;
    public string ApiKeyHash { get; set; } = string.Empty;
    public string SigningSecret { get; set; } = string.Empty;
    public string RetryPolicyJson { get; set; } = """{"maxRetries":7,"backoffSchedule":[5,30,120,900,3600,21600,86400]}""";
    public bool IsActive { get; set; } = true;
    public int IdempotencyWindowMinutes { get; set; } = 1440;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    // Navigation properties
    public ICollection<EventType> EventTypes { get; set; } = [];
    public ICollection<Endpoint> Endpoints { get; set; } = [];
    public ICollection<Message> Messages { get; set; } = [];
}

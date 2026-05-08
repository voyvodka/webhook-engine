namespace WebhookEngine.Core.Entities;

public class EventType
{
    public Guid Id { get; set; }
    public Guid AppId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? SchemaJson { get; set; }
    public bool IsArchived { get; set; }

    /// <summary>
    /// Per-event-type override for the idempotency-key reuse window. Null
    /// falls back to <see cref="Application.IdempotencyWindowMinutes"/>.
    /// </summary>
    public int? IdempotencyWindowMinutes { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // Navigation properties
    public Application Application { get; set; } = null!;
    public ICollection<Endpoint> Endpoints { get; set; } = [];
}

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

    // Per-app retention overrides. Null = fall back to global RetentionOptions
    // (DeliveredRetentionDays = 30, DeadLetterRetentionDays = 90 by default).
    public int? RetentionDeliveredDays { get; set; }
    public int? RetentionDeadLetterDays { get; set; }

    /// <summary>
    /// Optional per-application rate limit, in messages-per-second across all
    /// endpoints. Null = no application-level cap (the per-endpoint limit, if
    /// set, still applies). Useful for tier-gating ("free=10/s, pro=1000/s").
    /// </summary>
    public int? RateLimitPerSecond { get; set; }

    /// <summary>
    /// HMAC-SHA256 secret used to verify short-lived portal JWTs that the host
    /// SaaS mints for its end-users. Null = the embeddable customer portal is
    /// disabled for this application. Treat as a secret: never returned through
    /// list/read APIs, never written to the audit log; only the dedicated
    /// rotation endpoint exposes the raw value, and only at generation time.
    /// </summary>
    public string? PortalSigningKey { get; set; }

    /// <summary>
    /// JSON-serialized array of allowed CORS origins for portal endpoints
    /// (e.g. <c>["https://app.acme.com"]</c>). Wildcards are intentionally not
    /// supported — the host SaaS must enumerate exact origins. Null = portal
    /// CORS is unconfigured (effectively no allowed origins).
    /// </summary>
    public string? AllowedPortalOriginsJson { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    // Navigation properties
    public ICollection<EventType> EventTypes { get; set; } = [];
    public ICollection<Endpoint> Endpoints { get; set; } = [];
    public ICollection<Message> Messages { get; set; } = [];
}

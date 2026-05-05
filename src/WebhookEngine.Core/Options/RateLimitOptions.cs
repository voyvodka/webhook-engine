namespace WebhookEngine.Core.Options;

public class RateLimitOptions
{
    public const string SectionName = "WebhookEngine:RateLimit";

    /// <summary>Token bucket: max tokens in bucket at any time. Default 500
    /// supports a sensible burst for self-hosted single-tenant deployments.</summary>
    public int PermitLimit { get; set; } = 500;

    /// <summary>Token bucket: replenishment period in seconds.</summary>
    public int ReplenishmentPeriodSeconds { get; set; } = 1;

    /// <summary>Tokens added per replenishment tick. Default 100 sustains
    /// 100 req/s per app long-term — well above the original 2 req/s
    /// floor that surprised early operators.</summary>
    public int TokensPerPeriod { get; set; } = 100;

    /// <summary>Max requests queued when bucket empty. Default 200 absorbs
    /// transient bursts above PermitLimit instead of returning 429
    /// immediately; 0 keeps the strict reject-on-empty behavior.</summary>
    public int QueueLimit { get; set; } = 200;
}

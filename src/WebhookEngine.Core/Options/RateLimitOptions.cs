namespace WebhookEngine.Core.Options;

public class RateLimitOptions
{
    public const string SectionName = "WebhookEngine:RateLimit";

    /// <summary>Token bucket: max tokens in bucket at any time.</summary>
    public int PermitLimit { get; set; } = 100;

    /// <summary>Token bucket: replenishment period in seconds.</summary>
    public int ReplenishmentPeriodSeconds { get; set; } = 1;

    /// <summary>Tokens added per replenishment tick.</summary>
    public int TokensPerPeriod { get; set; } = 2;

    /// <summary>Max requests queued when bucket empty (0 = reject immediately).</summary>
    public int QueueLimit { get; set; } = 0;
}

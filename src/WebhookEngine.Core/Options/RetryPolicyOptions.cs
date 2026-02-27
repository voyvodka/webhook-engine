namespace WebhookEngine.Core.Options;

public class RetryPolicyOptions
{
    public const string SectionName = "WebhookEngine:RetryPolicy";

    public int MaxRetries { get; set; } = 7;

    /// <summary>
    /// Backoff schedule in seconds: 5s, 30s, 2m, 15m, 1h, 6h, 24h
    /// </summary>
    public int[] BackoffSchedule { get; set; } = [5, 30, 120, 900, 3600, 21600, 86400];
}

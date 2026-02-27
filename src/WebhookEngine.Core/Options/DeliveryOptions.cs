namespace WebhookEngine.Core.Options;

public class DeliveryOptions
{
    public const string SectionName = "WebhookEngine:Delivery";

    /// <summary>
    /// HTTP timeout for webhook delivery requests in seconds.
    /// </summary>
    public int TimeoutSeconds { get; set; } = 30;

    /// <summary>
    /// Number of messages to dequeue per batch.
    /// </summary>
    public int BatchSize { get; set; } = 10;

    /// <summary>
    /// Interval in milliseconds between queue polls when queue is empty.
    /// </summary>
    public int PollIntervalMs { get; set; } = 1000;

    /// <summary>
    /// Duration in minutes after which a locked message is considered stale.
    /// </summary>
    public int StaleLockMinutes { get; set; } = 5;
}

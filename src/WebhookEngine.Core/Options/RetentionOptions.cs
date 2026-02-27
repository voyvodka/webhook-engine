namespace WebhookEngine.Core.Options;

public class RetentionOptions
{
    public const string SectionName = "WebhookEngine:Retention";

    /// <summary>
    /// Days to keep delivered messages before cleanup.
    /// </summary>
    public int DeliveredRetentionDays { get; set; } = 30;

    /// <summary>
    /// Days to keep dead-letter messages before cleanup.
    /// </summary>
    public int DeadLetterRetentionDays { get; set; } = 90;
}

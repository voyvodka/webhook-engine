namespace WebhookEngine.Core.Options;

/// <summary>
/// Global configuration for the payload transformation pipeline (ADR-003).
/// All limits are enforced at delivery time before the HTTP POST. Per-endpoint
/// transformation toggles live on the <c>Endpoint</c> entity (TransformEnabled,
/// TransformExpression).
/// </summary>
public class TransformationOptions
{
    public const string SectionName = "WebhookEngine:Transformation";

    /// <summary>
    /// Global kill switch. When false, no transformations are applied regardless
    /// of per-endpoint settings — every delivery uses the original payload.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Hard timeout per JMESPath evaluation in milliseconds. Pathological
    /// expressions are aborted and the delivery falls back to the original payload.
    /// </summary>
    public int TimeoutMs { get; set; } = 100;

    /// <summary>
    /// Maximum size of the transformed payload in bytes (UTF-8). Prevents
    /// transformations from inflating output beyond the message-size budget.
    /// Matches the input payload limit (256 KB).
    /// </summary>
    public int MaxOutputBytes { get; set; } = 256 * 1024;
}

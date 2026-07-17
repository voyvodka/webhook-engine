namespace WebhookEngine.Core.Options;

public class LoginRateLimitOptions
{
    public const string SectionName = "WebhookEngine:LoginRateLimit";

    // Low by design: PBKDF2 caps per-attempt cost, this caps attempt volume so
    // an online brute-force can't outrun it.
    public int PermitLimit { get; set; } = 5;

    public int WindowSeconds { get; set; } = 60;

    // 0 = reject immediately with 429, the right default for an auth endpoint.
    public int QueueLimit { get; set; } = 0;
}

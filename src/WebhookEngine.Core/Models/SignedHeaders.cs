namespace WebhookEngine.Core.Models;

public class SignedHeaders
{
    public string WebhookId { get; set; } = string.Empty;
    public string WebhookTimestamp { get; set; } = string.Empty;
    public string WebhookSignature { get; set; } = string.Empty;
}

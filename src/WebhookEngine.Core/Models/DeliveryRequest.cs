namespace WebhookEngine.Core.Models;

public class DeliveryRequest
{
    public string MessageId { get; set; } = string.Empty;
    public string EndpointUrl { get; set; } = string.Empty;
    public string Payload { get; set; } = "{}";
    public SignedHeaders SignedHeaders { get; set; } = new();
    public Dictionary<string, string> CustomHeaders { get; set; } = [];

    // Raw per-endpoint IP allowlist (Endpoint.AllowedIpsJson). Carried so the
    // delivery client can enforce it at connect time, on the same resolution
    // that pins the socket — closing the DNS-rebinding gap. Null/empty = unrestricted.
    public string? AllowedIpsJson { get; set; }
}

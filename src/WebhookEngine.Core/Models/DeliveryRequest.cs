namespace WebhookEngine.Core.Models;

public class DeliveryRequest
{
    public string MessageId { get; set; } = string.Empty;
    public string EndpointUrl { get; set; } = string.Empty;
    public string Payload { get; set; } = "{}";
    public SignedHeaders SignedHeaders { get; set; } = new();
    public Dictionary<string, string> CustomHeaders { get; set; } = [];
}

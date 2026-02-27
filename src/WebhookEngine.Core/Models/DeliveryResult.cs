namespace WebhookEngine.Core.Models;

public class DeliveryResult
{
    public bool Success { get; set; }
    public int StatusCode { get; set; }
    public string? ResponseBody { get; set; }
    public string? Error { get; set; }
    public long LatencyMs { get; set; }
}

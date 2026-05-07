using System.Text.Json;
using WebhookEngine.Core.Entities;
using ApplicationEntity = WebhookEngine.Core.Entities.Application;

namespace WebhookEngine.Core.Models;

/// <summary>
/// Caller-supplied payload for an ad-hoc endpoint probe. The probe is fire-and-forget:
/// it does not persist a Message row, does not consult the idempotency window, does
/// not move the endpoint's circuit, and does not appear in delivery logs.
/// </summary>
public class EndpointTestRequest
{
    public string EventTypeName { get; set; } = string.Empty;
    public JsonElement? Payload { get; set; }
}

/// <summary>
/// Outcome of a single ad-hoc endpoint probe. Mirrors <see cref="DeliveryResult"/>
/// for the wire-level fields and adds a <see cref="Request"/> preview so the
/// dashboard can show exactly what was signed and sent.
/// </summary>
public class EndpointTestResult
{
    public bool Success { get; set; }
    public int StatusCode { get; set; }
    public long LatencyMs { get; set; }
    public string ResponseBody { get; set; } = string.Empty;
    public string? Error { get; set; }
    public EndpointTestRequestPreview Request { get; set; } = new();
}

/// <summary>
/// Preview of the signed request that was actually sent — useful for debugging
/// signature-verification failures on the receiver side.
/// </summary>
public class EndpointTestRequestPreview
{
    public string Url { get; set; } = string.Empty;
    public Dictionary<string, string> Headers { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public string Body { get; set; } = "{}";
}

/// <summary>
/// Bundle of inputs the tester needs to build a signed delivery. Caller resolves
/// the endpoint and parent application from its own auth context (public API key
/// vs dashboard cookie) and hands them to the service.
/// </summary>
public class EndpointTestContext
{
    public Endpoint Endpoint { get; init; } = null!;
    public ApplicationEntity Application { get; init; } = null!;
    public EndpointTestRequest Request { get; init; } = new();
}

using WebhookEngine.Core.Models;

namespace WebhookEngine.Core.Interfaces;

/// <summary>
/// Sends a one-off, fully-signed webhook to an endpoint and returns the wire-level
/// outcome (status code, latency, response body) plus a preview of the signed
/// request. Used by the dashboard "Send test" button and by the public test API.
/// The probe never touches the persisted message log, the idempotency table, or
/// the circuit-breaker state — it goes through the same HMAC + custom-header +
/// transformation pipeline as a real delivery, just with no side-effects on the
/// engine's data.
/// </summary>
public interface IEndpointTester
{
    Task<EndpointTestResult> ExecuteAsync(EndpointTestContext context, CancellationToken ct = default);
}

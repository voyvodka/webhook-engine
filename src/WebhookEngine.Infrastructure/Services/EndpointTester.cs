using System.Text.Json;
using WebhookEngine.Core.Interfaces;
using WebhookEngine.Core.Models;

namespace WebhookEngine.Infrastructure.Services;

/// <summary>
/// Wire-compatible probe that signs and POSTs a one-off webhook through the same
/// pipeline as <c>DeliveryWorker</c>. No persisted state mutates: no Message row,
/// no MessageAttempt row, no idempotency consultation, no EndpointHealth update.
/// The caller resolves auth + lookup; this service is the engine of the probe.
/// </summary>
public class EndpointTester : IEndpointTester
{
    private const int ResponseBodyTruncateBytes = 1024;

    private readonly ISigningService _signingService;
    private readonly IPayloadTransformer _payloadTransformer;
    private readonly IDeliveryService _deliveryService;

    public EndpointTester(
        ISigningService signingService,
        IPayloadTransformer payloadTransformer,
        IDeliveryService deliveryService)
    {
        _signingService = signingService;
        _payloadTransformer = payloadTransformer;
        _deliveryService = deliveryService;
    }

    public async Task<EndpointTestResult> ExecuteAsync(EndpointTestContext context, CancellationToken ct = default)
    {
        var endpoint = context.Endpoint;
        var application = context.Application;
        var request = context.Request;

        // Default payload when caller omits one — gives the receiver enough to
        // recognise this as a probe but no app-specific data.
        var rawPayload = request.Payload is JsonElement element
            ? element.GetRawText()
            : JsonSerializer.Serialize(new
            {
                test = true,
                eventType = request.EventTypeName,
                timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
            });

        var deliveryPayload = ApplyTransformation(rawPayload, endpoint);

        var messageId = $"test_{Guid.NewGuid():N}";
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var signingSecret = endpoint.SecretOverride ?? application.SigningSecret;
        var signedHeaders = _signingService.Sign(messageId, timestamp, deliveryPayload, signingSecret);

        var customHeaders = ParseCustomHeaders(endpoint.CustomHeadersJson);
        var allHeaders = BuildHeadersPreview(signedHeaders, customHeaders);

        var deliveryRequest = new DeliveryRequest
        {
            MessageId = messageId,
            EndpointUrl = endpoint.Url,
            Payload = deliveryPayload,
            SignedHeaders = signedHeaders,
            CustomHeaders = customHeaders
        };

        var deliveryResult = await _deliveryService.DeliverAsync(deliveryRequest, ct);

        return new EndpointTestResult
        {
            Success = deliveryResult.Success,
            StatusCode = deliveryResult.StatusCode,
            LatencyMs = deliveryResult.LatencyMs,
            ResponseBody = TruncateResponseBody(deliveryResult.ResponseBody),
            Error = deliveryResult.Error,
            Request = new EndpointTestRequestPreview
            {
                Url = endpoint.Url,
                Headers = allHeaders,
                Body = deliveryPayload
            }
        };
    }

    private string ApplyTransformation(string payload, Core.Entities.Endpoint endpoint)
    {
        if (!endpoint.TransformEnabled || string.IsNullOrWhiteSpace(endpoint.TransformExpression))
        {
            return payload;
        }

        var result = _payloadTransformer.Transform(endpoint.TransformExpression!, payload);
        return result.IsSuccess && result.TransformedPayload is not null
            ? result.TransformedPayload
            : payload;
    }

    private static Dictionary<string, string> ParseCustomHeaders(string? customHeadersJson)
    {
        if (string.IsNullOrWhiteSpace(customHeadersJson))
        {
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        try
        {
            var parsed = JsonSerializer.Deserialize<Dictionary<string, string>>(customHeadersJson);
            return parsed is null
                ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                : new Dictionary<string, string>(parsed, StringComparer.OrdinalIgnoreCase);
        }
        catch (JsonException)
        {
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }
    }

    private static Dictionary<string, string> BuildHeadersPreview(
        SignedHeaders signedHeaders,
        Dictionary<string, string> customHeaders)
    {
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["webhook-id"] = signedHeaders.WebhookId,
            ["webhook-timestamp"] = signedHeaders.WebhookTimestamp,
            ["webhook-signature"] = signedHeaders.WebhookSignature,
            ["User-Agent"] = "WebhookEngine/1.0"
        };

        foreach (var (key, value) in customHeaders)
        {
            headers[key] = value;
        }

        return headers;
    }

    private static string TruncateResponseBody(string? responseBody)
    {
        if (string.IsNullOrEmpty(responseBody))
        {
            return string.Empty;
        }

        return responseBody.Length <= ResponseBodyTruncateBytes
            ? responseBody
            : responseBody[..ResponseBodyTruncateBytes] + "... [truncated]";
    }
}

using System.Diagnostics;
using System.Text;
using WebhookEngine.Core.Interfaces;
using WebhookEngine.Core.Models;

namespace WebhookEngine.Infrastructure.Services;

public class HttpDeliveryService : IDeliveryService
{
    private readonly IHttpClientFactory _httpClientFactory;

    public HttpDeliveryService(IHttpClientFactory httpClientFactory)
    {
        _httpClientFactory = httpClientFactory;
    }

    public async Task<DeliveryResult> DeliverAsync(DeliveryRequest request, CancellationToken ct = default)
    {
        var client = _httpClientFactory.CreateClient("webhook-delivery");

        var httpRequest = new HttpRequestMessage(HttpMethod.Post, request.EndpointUrl);
        httpRequest.Content = new StringContent(request.Payload, Encoding.UTF8, "application/json");

        // Add signature headers
        httpRequest.Headers.Add("webhook-id", request.SignedHeaders.WebhookId);
        httpRequest.Headers.Add("webhook-timestamp", request.SignedHeaders.WebhookTimestamp);
        httpRequest.Headers.Add("webhook-signature", request.SignedHeaders.WebhookSignature);
        httpRequest.Headers.Add("User-Agent", "WebhookEngine/1.0");

        // Add custom endpoint headers
        foreach (var header in request.CustomHeaders)
        {
            httpRequest.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }

        var stopwatch = Stopwatch.StartNew();
        try
        {
            var response = await client.SendAsync(httpRequest, ct);
            stopwatch.Stop();

            // Truncate response body to 10KB max to prevent storage explosion
            var responseBody = await response.Content.ReadAsStringAsync(ct);
            if (responseBody.Length > 10240)
            {
                responseBody = responseBody[..10240] + "... [truncated]";
            }

            return new DeliveryResult
            {
                Success = response.IsSuccessStatusCode,
                StatusCode = (int)response.StatusCode,
                ResponseBody = responseBody,
                LatencyMs = stopwatch.ElapsedMilliseconds
            };
        }
        catch (TaskCanceledException)
        {
            stopwatch.Stop();
            return new DeliveryResult
            {
                Success = false,
                StatusCode = 0,
                Error = "Timeout",
                LatencyMs = stopwatch.ElapsedMilliseconds
            };
        }
        catch (HttpRequestException ex)
        {
            stopwatch.Stop();
            return new DeliveryResult
            {
                Success = false,
                StatusCode = 0,
                Error = ex.Message,
                LatencyMs = stopwatch.ElapsedMilliseconds
            };
        }
    }
}

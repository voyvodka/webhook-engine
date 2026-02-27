using System.Net.Http.Json;
using System.Text.Json;

namespace WebhookEngine.Sdk;

/// <summary>
/// .NET SDK client for WebhookEngine API.
/// Provides access to event types, endpoints, and messages.
/// </summary>
/// <example>
/// // Simple usage
/// var client = new WebhookEngineClient("whe_abc_your-api-key", "http://localhost:5100");
/// await client.Messages.SendAsync(new SendMessageRequest { EventType = "order.created", Payload = new { id = 1 } });
///
/// // With IHttpClientFactory (recommended for ASP.NET Core)
/// builder.Services.AddHttpClient("webhookengine", c => {
///     c.BaseAddress = new Uri("http://localhost:5100");
///     c.DefaultRequestHeaders.Add("Authorization", "Bearer whe_abc_your-api-key");
/// });
/// var client = new WebhookEngineClient(httpClientFactory.CreateClient("webhookengine"));
/// </example>
public sealed class WebhookEngineClient : IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly bool _ownsHttpClient;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    /// <summary>
    /// Creates a new client with a managed HttpClient.
    /// The client will be disposed when this instance is disposed.
    /// </summary>
    public WebhookEngineClient(string apiKey, string baseUrl = "http://localhost:5100")
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(apiKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(baseUrl);

        _httpClient = new HttpClient { BaseAddress = new Uri(baseUrl) };
        _httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {apiKey}");
        _ownsHttpClient = true;

        EventTypes = new EventTypeClient(_httpClient, JsonOptions);
        Endpoints = new EndpointClient(_httpClient, JsonOptions);
        Messages = new MessageClient(_httpClient, JsonOptions);
    }

    /// <summary>
    /// Creates a new client with an externally managed HttpClient.
    /// Use this with IHttpClientFactory. The caller is responsible for the HttpClient lifecycle.
    /// </summary>
    public WebhookEngineClient(HttpClient httpClient)
    {
        ArgumentNullException.ThrowIfNull(httpClient);

        _httpClient = httpClient;
        _ownsHttpClient = false;

        EventTypes = new EventTypeClient(_httpClient, JsonOptions);
        Endpoints = new EndpointClient(_httpClient, JsonOptions);
        Messages = new MessageClient(_httpClient, JsonOptions);
    }

    /// <summary>Event type operations (create, list, get, update, archive).</summary>
    public EventTypeClient EventTypes { get; }

    /// <summary>Endpoint operations (create, list, get, update, enable, disable, delete, stats).</summary>
    public EndpointClient Endpoints { get; }

    /// <summary>Message operations (send, get, list, retry, list attempts).</summary>
    public MessageClient Messages { get; }

    public void Dispose()
    {
        if (_ownsHttpClient)
            _httpClient.Dispose();
    }
}

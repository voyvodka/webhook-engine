using System.Text.Json;

namespace WebhookEngine.Sdk;

/// <summary>
/// Client for message operations.
/// </summary>
public class MessageClient
{
    private readonly HttpClient _http;
    private readonly JsonSerializerOptions _json;
    private const string BasePath = "/api/v1/messages";

    internal MessageClient(HttpClient http, JsonSerializerOptions json)
    {
        _http = http;
        _json = json;
    }

    /// <summary>
    /// Send a message (webhook) to all endpoints subscribed to the given event type.
    /// Returns 202 Accepted — delivery is asynchronous.
    /// </summary>
    public async Task<SendMessageResponse?> SendAsync(SendMessageRequest request, CancellationToken ct = default)
    {
        var result = await SdkHelpers.PostAsync<SendMessageResponse>(_http, BasePath, request, _json, ct);
        return result.Data;
    }

    /// <summary>
    /// Send multiple messages in a single API call.
    /// Returns per-item success/error results and total enqueued message count.
    /// </summary>
    public async Task<BatchSendMessagesResponse?> BatchSendAsync(BatchSendMessagesRequest request, CancellationToken ct = default)
    {
        var result = await SdkHelpers.PostAsync<BatchSendMessagesResponse>(_http, $"{BasePath}/batch", request, _json, ct);
        return result.Data;
    }

    /// <summary>Get a single message by ID.</summary>
    public async Task<MessageResponse?> GetAsync(Guid messageId, CancellationToken ct = default)
    {
        var result = await SdkHelpers.GetAsync<MessageResponse>(_http, $"{BasePath}/{messageId}", _json, ct);
        return result.Data;
    }

    /// <summary>List messages with optional filtering and pagination.</summary>
    public async Task<ApiResponse<List<MessageResponse>>> ListAsync(ListMessagesOptions? options = null, CancellationToken ct = default)
    {
        options ??= new ListMessagesOptions();
        var query = SdkHelpers.BuildQuery(
            ("page", options.Page.ToString()),
            ("pageSize", options.PageSize.ToString()),
            ("status", options.Status),
            ("endpointId", options.EndpointId?.ToString()),
            ("eventTypeId", options.EventTypeId?.ToString()),
            ("after", options.After?.ToString("O")),
            ("before", options.Before?.ToString("O")));

        return await SdkHelpers.GetAsync<List<MessageResponse>>(_http, $"{BasePath}{query}", _json, ct);
    }

    /// <summary>
    /// Retry a failed or dead-letter message.
    /// Resets status to pending and schedules immediate delivery.
    /// </summary>
    public async Task<RetryMessageResponse?> RetryAsync(Guid messageId, CancellationToken ct = default)
    {
        var result = await SdkHelpers.PostAsync<RetryMessageResponse>(_http, $"{BasePath}/{messageId}/retry", null, _json, ct);
        return result.Data;
    }

    /// <summary>
    /// Replay historical messages by event type and date range.
    /// Creates new pending messages for redelivery to active endpoints.
    /// </summary>
    public async Task<ReplayMessagesResponse?> ReplayAsync(ReplayMessagesRequest request, CancellationToken ct = default)
    {
        var result = await SdkHelpers.PostAsync<ReplayMessagesResponse>(_http, $"{BasePath}/replay", request, _json, ct);
        return result.Data;
    }

    /// <summary>List delivery attempts for a message.</summary>
    public async Task<ApiResponse<List<MessageAttemptResponse>>> ListAttemptsAsync(Guid messageId, ListOptions? options = null, CancellationToken ct = default)
    {
        options ??= new ListOptions();
        var query = SdkHelpers.BuildQuery(
            ("page", options.Page.ToString()),
            ("pageSize", options.PageSize.ToString()));

        return await SdkHelpers.GetAsync<List<MessageAttemptResponse>>(_http, $"{BasePath}/{messageId}/attempts{query}", _json, ct);
    }
}

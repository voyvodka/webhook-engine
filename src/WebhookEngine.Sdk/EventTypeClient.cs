using System.Text.Json;

namespace WebhookEngine.Sdk;

/// <summary>
/// Client for event type operations.
/// </summary>
public class EventTypeClient
{
    private readonly HttpClient _http;
    private readonly JsonSerializerOptions _json;
    private const string BasePath = "/api/v1/event-types";

    internal EventTypeClient(HttpClient http, JsonSerializerOptions json)
    {
        _http = http;
        _json = json;
    }

    /// <summary>Create a new event type.</summary>
    public async Task<EventTypeResponse?> CreateAsync(CreateEventTypeRequest request, CancellationToken ct = default)
    {
        var result = await SdkHelpers.PostAsync<EventTypeResponse>(_http, BasePath, request, _json, ct);
        return result.Data;
    }

    /// <summary>List event types with optional filtering and pagination.</summary>
    public async Task<ApiResponse<List<EventTypeResponse>>> ListAsync(ListEventTypesOptions? options = null, CancellationToken ct = default)
    {
        options ??= new ListEventTypesOptions();
        var query = SdkHelpers.BuildQuery(
            ("page", options.Page.ToString()),
            ("pageSize", options.PageSize.ToString()),
            ("includeArchived", options.IncludeArchived ? "true" : null));

        return await SdkHelpers.GetAsync<List<EventTypeResponse>>(_http, $"{BasePath}{query}", _json, ct);
    }

    /// <summary>Get a single event type by ID.</summary>
    public async Task<EventTypeResponse?> GetAsync(Guid eventTypeId, CancellationToken ct = default)
    {
        var result = await SdkHelpers.GetAsync<EventTypeResponse>(_http, $"{BasePath}/{eventTypeId}", _json, ct);
        return result.Data;
    }

    /// <summary>Update an event type.</summary>
    public async Task<EventTypeResponse?> UpdateAsync(Guid eventTypeId, UpdateEventTypeRequest request, CancellationToken ct = default)
    {
        var result = await SdkHelpers.PutAsync<EventTypeResponse>(_http, $"{BasePath}/{eventTypeId}", request, _json, ct);
        return result.Data;
    }

    /// <summary>Archive (soft delete) an event type.</summary>
    public async Task ArchiveAsync(Guid eventTypeId, CancellationToken ct = default)
    {
        await SdkHelpers.DeleteAsync(_http, $"{BasePath}/{eventTypeId}", ct);
    }
}

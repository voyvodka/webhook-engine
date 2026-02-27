using System.Text.Json;

namespace WebhookEngine.Sdk;

/// <summary>
/// Client for endpoint operations.
/// </summary>
public class EndpointClient
{
    private readonly HttpClient _http;
    private readonly JsonSerializerOptions _json;
    private const string BasePath = "/api/v1/endpoints";

    internal EndpointClient(HttpClient http, JsonSerializerOptions json)
    {
        _http = http;
        _json = json;
    }

    /// <summary>Create a new endpoint.</summary>
    public async Task<EndpointResponse?> CreateAsync(CreateEndpointRequest request, CancellationToken ct = default)
    {
        var result = await SdkHelpers.PostAsync<EndpointResponse>(_http, BasePath, request, _json, ct);
        return result.Data;
    }

    /// <summary>List endpoints with optional filtering and pagination.</summary>
    public async Task<ApiResponse<List<EndpointResponse>>> ListAsync(ListEndpointsOptions? options = null, CancellationToken ct = default)
    {
        options ??= new ListEndpointsOptions();
        var query = SdkHelpers.BuildQuery(
            ("page", options.Page.ToString()),
            ("pageSize", options.PageSize.ToString()),
            ("status", options.Status));

        return await SdkHelpers.GetAsync<List<EndpointResponse>>(_http, $"{BasePath}{query}", _json, ct);
    }

    /// <summary>Get a single endpoint by ID.</summary>
    public async Task<EndpointResponse?> GetAsync(Guid endpointId, CancellationToken ct = default)
    {
        var result = await SdkHelpers.GetAsync<EndpointResponse>(_http, $"{BasePath}/{endpointId}", _json, ct);
        return result.Data;
    }

    /// <summary>Update an endpoint.</summary>
    public async Task<EndpointResponse?> UpdateAsync(Guid endpointId, UpdateEndpointRequest request, CancellationToken ct = default)
    {
        var result = await SdkHelpers.PutAsync<EndpointResponse>(_http, $"{BasePath}/{endpointId}", request, _json, ct);
        return result.Data;
    }

    /// <summary>Disable an endpoint (stops receiving deliveries).</summary>
    public async Task<EndpointResponse?> DisableAsync(Guid endpointId, CancellationToken ct = default)
    {
        var result = await SdkHelpers.PostAsync<EndpointResponse>(_http, $"{BasePath}/{endpointId}/disable", null, _json, ct);
        return result.Data;
    }

    /// <summary>Enable a previously disabled endpoint.</summary>
    public async Task<EndpointResponse?> EnableAsync(Guid endpointId, CancellationToken ct = default)
    {
        var result = await SdkHelpers.PostAsync<EndpointResponse>(_http, $"{BasePath}/{endpointId}/enable", null, _json, ct);
        return result.Data;
    }

    /// <summary>Delete an endpoint permanently.</summary>
    public async Task DeleteAsync(Guid endpointId, CancellationToken ct = default)
    {
        await SdkHelpers.DeleteAsync(_http, $"{BasePath}/{endpointId}", ct);
    }

    /// <summary>Get delivery statistics for an endpoint.</summary>
    public async Task<EndpointStatsResponse?> GetStatsAsync(Guid endpointId, string period = "24h", CancellationToken ct = default)
    {
        var query = SdkHelpers.BuildQuery(("period", period));
        var result = await SdkHelpers.GetAsync<EndpointStatsResponse>(_http, $"{BasePath}/{endpointId}/stats{query}", _json, ct);
        return result.Data;
    }
}

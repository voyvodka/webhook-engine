using System.Net.Http.Json;
using System.Text.Json;

namespace WebhookEngine.Sdk;

/// <summary>
/// Shared HTTP helpers for SDK sub-clients.
/// </summary>
internal static class SdkHelpers
{
    internal static async Task<ApiResponse<T>> GetAsync<T>(
        HttpClient http, string url, JsonSerializerOptions json, CancellationToken ct)
    {
        var response = await http.GetAsync(url, ct);
        await EnsureSuccessAsync(response, ct);
        return await response.Content.ReadFromJsonAsync<ApiResponse<T>>(json, ct)
               ?? new ApiResponse<T>();
    }

    internal static async Task<ApiResponse<T>> PostAsync<T>(
        HttpClient http, string url, object? body, JsonSerializerOptions json, CancellationToken ct)
    {
        HttpResponseMessage response;
        if (body is not null)
            response = await http.PostAsJsonAsync(url, body, json, ct);
        else
            response = await http.PostAsync(url, null, ct);

        await EnsureSuccessAsync(response, ct);
        return await response.Content.ReadFromJsonAsync<ApiResponse<T>>(json, ct)
               ?? new ApiResponse<T>();
    }

    internal static async Task<ApiResponse<T>> PutAsync<T>(
        HttpClient http, string url, object body, JsonSerializerOptions json, CancellationToken ct)
    {
        var response = await http.PutAsJsonAsync(url, body, json, ct);
        await EnsureSuccessAsync(response, ct);
        return await response.Content.ReadFromJsonAsync<ApiResponse<T>>(json, ct)
               ?? new ApiResponse<T>();
    }

    internal static async Task DeleteAsync(
        HttpClient http, string url, CancellationToken ct)
    {
        var response = await http.DeleteAsync(url, ct);
        await EnsureSuccessAsync(response, ct);
    }

    internal static string BuildQuery(params (string key, string? value)[] parameters)
    {
        var parts = new List<string>();
        foreach (var (key, value) in parameters)
        {
            if (value is not null)
                parts.Add($"{Uri.EscapeDataString(key)}={Uri.EscapeDataString(value)}");
        }
        return parts.Count > 0 ? "?" + string.Join("&", parts) : string.Empty;
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken ct)
    {
        if (response.IsSuccessStatusCode)
            return;

        string body;
        try
        {
            body = await response.Content.ReadAsStringAsync(ct);
        }
        catch
        {
            body = string.Empty;
        }

        throw new WebhookEngineException(
            $"API returned {(int)response.StatusCode} {response.ReasonPhrase}",
            (int)response.StatusCode,
            body);
    }
}

/// <summary>
/// Exception thrown when the WebhookEngine API returns an error response.
/// </summary>
public class WebhookEngineException : Exception
{
    public int StatusCode { get; }
    public string ResponseBody { get; }

    public WebhookEngineException(string message, int statusCode, string responseBody)
        : base(message)
    {
        StatusCode = statusCode;
        ResponseBody = responseBody;
    }
}

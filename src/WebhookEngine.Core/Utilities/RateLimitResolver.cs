using System.Text.Json;

namespace WebhookEngine.Core.Utilities;

public static class RateLimitResolver
{
    public static int? ResolveRateLimitPerMinute(string? metadataJson)
    {
        if (string.IsNullOrWhiteSpace(metadataJson))
            return null;

        try
        {
            using var document = JsonDocument.Parse(metadataJson);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
                return null;

            if (!document.RootElement.TryGetProperty("rateLimitPerMinute", out var rateLimitElement))
                return null;

            if (rateLimitElement.ValueKind == JsonValueKind.Number && rateLimitElement.TryGetInt32(out var numericValue))
                return numericValue > 0 ? numericValue : null;

            if (rateLimitElement.ValueKind == JsonValueKind.String
                && int.TryParse(rateLimitElement.GetString(), out var stringValue))
                return stringValue > 0 ? stringValue : null;

            return null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}

using System.Text.Json;
using WebhookEngine.API.Contracts;
using WebhookEngine.Core.Entities;
using WebhookEngine.Core.Models;
using WebhookEngine.Infrastructure.Repositories;
using EndpointEntity = WebhookEngine.Core.Entities.Endpoint;

namespace WebhookEngine.API.Contracts.Portal;

/// <summary>
/// Customer-facing endpoint summary returned by the portal list route. Drops the
/// admin-only fields (transform-*, allowed-ips) outright; presents a boolean
/// <see cref="HasSecretOverride"/> instead of leaking the secret value, and exposes
/// custom-header NAMES only — values are deliberately omitted because the portal
/// is shown to end-customers who should not see other tenants' shared secrets that
/// host SaaS implementations sometimes plumb through custom headers.
/// </summary>
public sealed class PortalEndpointListItem
{
    public Guid Id { get; init; }
    public string Url { get; init; } = string.Empty;
    public string? Description { get; init; }
    public string Status { get; init; } = string.Empty;
    public bool HasSecretOverride { get; init; }
    public List<string> CustomHeaderNames { get; init; } = [];
    public JsonElement Metadata { get; init; }
    public List<Guid> FilterEventTypes { get; init; } = [];
    public DateTime CreatedAt { get; init; }
    public DateTime UpdatedAt { get; init; }
}

/// <summary>
/// Customer-facing endpoint detail. Same narrowing rules as the list shape — the
/// transform expression, transform flags, and CIDR allowlist never reach the
/// portal because they're host-operator concerns, not customer concerns.
/// </summary>
public sealed class PortalEndpointDetail
{
    public Guid Id { get; init; }
    public string Url { get; init; } = string.Empty;
    public string? Description { get; init; }
    public string Status { get; init; } = string.Empty;
    public bool HasSecretOverride { get; init; }
    public List<string> CustomHeaderNames { get; init; } = [];
    public JsonElement Metadata { get; init; }
    public List<Guid> FilterEventTypes { get; init; } = [];
    public DateTime CreatedAt { get; init; }
    public DateTime UpdatedAt { get; init; }
}

/// <summary>
/// Single attempt row served to the embedded portal. Mirrors
/// <see cref="MessageAttemptResponseDto"/> minus request-headers / response-body
/// (the portal is not a debugging tool — those fields stay on the dashboard).
/// </summary>
public sealed class PortalAttemptRow
{
    public Guid Id { get; init; }
    public Guid MessageId { get; init; }
    public int AttemptNumber { get; init; }
    public string Status { get; init; } = string.Empty;
    public int? StatusCode { get; init; }
    public string? Error { get; init; }
    public int LatencyMs { get; init; }
    public DateTime CreatedAt { get; init; }
}

/// <summary>
/// Portal create-endpoint payload. Transform fields and allowed-IPs are deliberately
/// absent from this DTO — JSON model binding silently drops unknown properties so
/// a host SaaS that mistakenly forwards an admin payload won't be rejected, the
/// extra fields just don't reach the persistence layer.
/// </summary>
public class PortalCreateEndpointRequest
{
    public string Url { get; set; } = string.Empty;
    public string? Description { get; set; }
    public List<Guid>? FilterEventTypes { get; set; }
    public Dictionary<string, string>? CustomHeaders { get; set; }
    public Dictionary<string, string>? Metadata { get; set; }
    // Customer-facing optional override. Empty/whitespace clears the override.
    public string? SecretOverride { get; set; }
}

/// <summary>
/// Portal update-endpoint payload. Same field-narrowing rationale as
/// <see cref="PortalCreateEndpointRequest"/>.
/// </summary>
public class PortalUpdateEndpointRequest
{
    public string? Url { get; set; }
    public string? Description { get; set; }
    public List<Guid>? FilterEventTypes { get; set; }
    public Dictionary<string, string>? CustomHeaders { get; set; }
    public Dictionary<string, string>? Metadata { get; set; }
    public string? SecretOverride { get; set; }
}

/// <summary>
/// Portal "send test" request — same shape as the admin probe (the engine does
/// the same signed delivery either way), kept as a separate type so portal
/// validators can diverge later if customer-facing constraints change.
/// </summary>
public class PortalEndpointTestRequest
{
    public string? EventType { get; set; }
    public JsonElement? Payload { get; set; }
}

/// <summary>
/// Customer-facing outcome of a portal endpoint probe. Wire-level fields mirror the
/// admin <see cref="EndpointTestResult"/>, but the request preview is redacted:
/// custom-header VALUES (often an operator API key) are masked so the portal never
/// leaks the secrets it hides everywhere else. Signed webhook headers stay verbatim.
/// </summary>
public sealed class PortalEndpointTestResult
{
    public bool Success { get; init; }
    public int StatusCode { get; init; }
    public long LatencyMs { get; init; }
    public string ResponseBody { get; init; } = string.Empty;
    public string? Error { get; init; }
    public PortalEndpointTestRequestPreview Request { get; init; } = new();
}

/// <summary>
/// Redacted preview of the signed request the portal probe sent. Custom-header names
/// are preserved but their values are masked with
/// <see cref="PortalDtoMapper.RedactedHeaderValue"/>.
/// </summary>
public sealed class PortalEndpointTestRequestPreview
{
    public string Url { get; init; } = string.Empty;
    public Dictionary<string, string> Headers { get; init; } = new(StringComparer.OrdinalIgnoreCase);
    public string Body { get; init; } = "{}";
}

/// <summary>
/// Customer-facing event-type list item used to populate the endpoint
/// FilterEventTypes dropdown. Archived rows are excluded at the controller.
/// </summary>
public sealed class PortalEventTypeListItem
{
    public Guid Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public string? Description { get; init; }
}

public static class PortalDtoMapper
{
    public const string RedactedHeaderValue = "***";

    // Anchored on the four known signed/standard keys so a custom header colliding
    // with one of these names (e.g. operator-set "user-agent" or spoofed
    // "webhook-signature") is still redacted rather than masquerading as signed.
    private static readonly HashSet<string> SignedPreviewHeaderNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "webhook-id",
        "webhook-timestamp",
        "webhook-signature",
        "User-Agent"
    };

    public static PortalEndpointDetail ToPortalDetail(this EndpointEntity endpoint)
    {
        return new PortalEndpointDetail
        {
            Id = endpoint.Id,
            Url = endpoint.Url,
            Description = endpoint.Description,
            Status = endpoint.Status.ToString().ToLowerInvariant(),
            HasSecretOverride = !string.IsNullOrEmpty(endpoint.SecretOverride),
            CustomHeaderNames = ParseHeaderNames(endpoint.CustomHeadersJson),
            Metadata = JsonValueParser.ParseOrEmptyObject(endpoint.MetadataJson),
            FilterEventTypes = endpoint.EventTypes.Select(et => et.Id).ToList(),
            CreatedAt = endpoint.CreatedAt,
            UpdatedAt = endpoint.UpdatedAt
        };
    }

    public static PortalEndpointListItem ToPortalListItem(this EndpointEntity endpoint)
    {
        return new PortalEndpointListItem
        {
            Id = endpoint.Id,
            Url = endpoint.Url,
            Description = endpoint.Description,
            Status = endpoint.Status.ToString().ToLowerInvariant(),
            HasSecretOverride = !string.IsNullOrEmpty(endpoint.SecretOverride),
            CustomHeaderNames = ParseHeaderNames(endpoint.CustomHeadersJson),
            Metadata = JsonValueParser.ParseOrEmptyObject(endpoint.MetadataJson),
            FilterEventTypes = endpoint.EventTypes.Select(et => et.Id).ToList(),
            CreatedAt = endpoint.CreatedAt,
            UpdatedAt = endpoint.UpdatedAt
        };
    }

    public static PortalEndpointListItem ToPortalListItem(this EndpointListItem item)
    {
        return new PortalEndpointListItem
        {
            Id = item.Id,
            Url = item.Url,
            Description = item.Description,
            Status = item.Status.ToString().ToLowerInvariant(),
            HasSecretOverride = !string.IsNullOrEmpty(item.SecretOverride),
            CustomHeaderNames = ParseHeaderNames(item.CustomHeadersJson),
            Metadata = JsonValueParser.ParseOrEmptyObject(item.MetadataJson),
            FilterEventTypes = item.EventTypeIds,
            CreatedAt = item.CreatedAt,
            UpdatedAt = item.UpdatedAt
        };
    }

    public static PortalAttemptRow ToPortalRow(this MessageAttempt attempt)
    {
        return new PortalAttemptRow
        {
            Id = attempt.Id,
            MessageId = attempt.MessageId,
            AttemptNumber = attempt.AttemptNumber,
            Status = attempt.Status.ToString().ToLowerInvariant(),
            StatusCode = attempt.StatusCode,
            Error = attempt.Error,
            LatencyMs = attempt.LatencyMs,
            CreatedAt = attempt.CreatedAt
        };
    }

    public static PortalEventTypeListItem ToPortalListItem(this EventType eventType)
    {
        return new PortalEventTypeListItem
        {
            Id = eventType.Id,
            Name = eventType.Name,
            Description = eventType.Description
        };
    }

    /// <summary>
    /// Projects an admin <see cref="EndpointTestResult"/> into the portal-safe shape,
    /// masking every custom-header value. Redaction anchors on
    /// <paramref name="customHeadersJson"/> so a spoofed header colliding with a
    /// signed name is masked, not unmasked.
    /// </summary>
    public static PortalEndpointTestResult ToPortalTestResult(
        this EndpointTestResult result,
        string? customHeadersJson)
    {
        var customNames = ParseHeaderNameSet(customHeadersJson);

        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, value) in result.Request.Headers)
        {
            var isSigned = SignedPreviewHeaderNames.Contains(key) && !customNames.Contains(key);
            headers[key] = isSigned ? value : RedactedHeaderValue;
        }

        return new PortalEndpointTestResult
        {
            Success = result.Success,
            StatusCode = result.StatusCode,
            LatencyMs = result.LatencyMs,
            ResponseBody = result.ResponseBody,
            Error = result.Error,
            Request = new PortalEndpointTestRequestPreview
            {
                Url = result.Request.Url,
                Headers = headers,
                Body = result.Request.Body
            }
        };
    }

    private static List<string> ParseHeaderNames(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return [];
        }

        try
        {
            var parsed = JsonSerializer.Deserialize<Dictionary<string, string>>(json);
            return parsed is null ? [] : parsed.Keys.ToList();
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static HashSet<string> ParseHeaderNameSet(string? json)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(json))
        {
            return set;
        }

        try
        {
            var parsed = JsonSerializer.Deserialize<Dictionary<string, string>>(json);
            if (parsed is not null)
            {
                foreach (var key in parsed.Keys)
                {
                    set.Add(key);
                }
            }
        }
        catch (JsonException)
        {
            // Malformed JSON — treat as no custom headers; preview keeps only the signed set.
        }

        return set;
    }
}

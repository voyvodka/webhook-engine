using System.Text.Json;
using WebhookEngine.Core.Entities;
using WebhookEngine.Infrastructure.Repositories;
using EndpointEntity = WebhookEngine.Core.Entities.Endpoint;

namespace WebhookEngine.API.Contracts;

public sealed class EventTypeResponseDto
{
    public Guid Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public string? Description { get; init; }
    public JsonElement? Schema { get; init; }
    public bool IsArchived { get; init; }
    public DateTime CreatedAt { get; init; }
}

public sealed class EndpointResponseDto
{
    public Guid Id { get; init; }
    public Guid AppId { get; init; }
    public string Url { get; init; } = string.Empty;
    public string? Description { get; init; }
    public string Status { get; init; } = string.Empty;
    public JsonElement CustomHeadersJson { get; init; }
    public string? SecretOverride { get; init; }
    public JsonElement MetadataJson { get; init; }
    public string? TransformExpression { get; init; }
    public bool TransformEnabled { get; init; }
    public DateTime? TransformValidatedAt { get; init; }
    public List<Guid> FilterEventTypes { get; init; } = [];
    public DateTime CreatedAt { get; init; }
    public DateTime UpdatedAt { get; init; }
}

public sealed class MessageResponseDto
{
    public Guid Id { get; init; }
    public Guid AppId { get; init; }
    public Guid EndpointId { get; init; }
    public Guid EventTypeId { get; init; }
    public string? EventId { get; init; }
    public string? IdempotencyKey { get; init; }
    public JsonElement Payload { get; init; }
    public string Status { get; init; } = string.Empty;
    public int AttemptCount { get; init; }
    public int MaxRetries { get; init; }
    public DateTime ScheduledAt { get; init; }
    public DateTime? DeliveredAt { get; init; }
    public DateTime CreatedAt { get; init; }
}

public sealed class MessageAttemptResponseDto
{
    public Guid Id { get; init; }
    public Guid MessageId { get; init; }
    public Guid EndpointId { get; init; }
    public int AttemptNumber { get; init; }
    public string Status { get; init; } = string.Empty;
    public int? StatusCode { get; init; }
    public JsonElement? RequestHeadersJson { get; init; }
    public string? ResponseBody { get; init; }
    public string? Error { get; init; }
    public int LatencyMs { get; init; }
    public DateTime CreatedAt { get; init; }
}

public static class ApiResponseMapper
{
    public static EventTypeResponseDto ToDto(this EventType eventType)
    {
        return new EventTypeResponseDto
        {
            Id = eventType.Id,
            Name = eventType.Name,
            Description = eventType.Description,
            Schema = JsonValueParser.ParseOrNull(eventType.SchemaJson),
            IsArchived = eventType.IsArchived,
            CreatedAt = eventType.CreatedAt
        };
    }

    public static EndpointResponseDto ToDto(this EndpointEntity endpoint)
    {
        return new EndpointResponseDto
        {
            Id = endpoint.Id,
            AppId = endpoint.AppId,
            Url = endpoint.Url,
            Description = endpoint.Description,
            Status = endpoint.Status.ToString().ToLowerInvariant(),
            CustomHeadersJson = JsonValueParser.ParseOrEmptyObject(endpoint.CustomHeadersJson),
            SecretOverride = endpoint.SecretOverride,
            MetadataJson = JsonValueParser.ParseOrEmptyObject(endpoint.MetadataJson),
            TransformExpression = endpoint.TransformExpression,
            TransformEnabled = endpoint.TransformEnabled,
            TransformValidatedAt = endpoint.TransformValidatedAt,
            FilterEventTypes = endpoint.EventTypes.Select(et => et.Id).ToList(),
            CreatedAt = endpoint.CreatedAt,
            UpdatedAt = endpoint.UpdatedAt
        };
    }

    public static EndpointResponseDto ToDto(this EndpointListItem endpoint)
    {
        return new EndpointResponseDto
        {
            Id = endpoint.Id,
            AppId = endpoint.AppId,
            Url = endpoint.Url,
            Description = endpoint.Description,
            Status = endpoint.Status.ToString().ToLowerInvariant(),
            CustomHeadersJson = JsonValueParser.ParseOrEmptyObject(endpoint.CustomHeadersJson),
            SecretOverride = endpoint.SecretOverride,
            MetadataJson = JsonValueParser.ParseOrEmptyObject(endpoint.MetadataJson),
            TransformExpression = endpoint.TransformExpression,
            TransformEnabled = endpoint.TransformEnabled,
            TransformValidatedAt = endpoint.TransformValidatedAt,
            FilterEventTypes = endpoint.EventTypeIds,
            CreatedAt = endpoint.CreatedAt,
            UpdatedAt = endpoint.UpdatedAt
        };
    }

    public static MessageResponseDto ToDto(this Message message)
    {
        return new MessageResponseDto
        {
            Id = message.Id,
            AppId = message.AppId,
            EndpointId = message.EndpointId,
            EventTypeId = message.EventTypeId,
            EventId = message.EventId,
            IdempotencyKey = message.IdempotencyKey,
            Payload = JsonValueParser.ParseOrEmptyObject(message.Payload),
            Status = message.Status.ToString().ToLowerInvariant(),
            AttemptCount = message.AttemptCount,
            MaxRetries = message.MaxRetries,
            ScheduledAt = message.ScheduledAt,
            DeliveredAt = message.DeliveredAt,
            CreatedAt = message.CreatedAt
        };
    }

    public static MessageAttemptResponseDto ToDto(this MessageAttempt attempt)
    {
        return new MessageAttemptResponseDto
        {
            Id = attempt.Id,
            MessageId = attempt.MessageId,
            EndpointId = attempt.EndpointId,
            AttemptNumber = attempt.AttemptNumber,
            Status = attempt.Status.ToString().ToLowerInvariant(),
            StatusCode = attempt.StatusCode,
            RequestHeadersJson = JsonValueParser.ParseOrNull(attempt.RequestHeadersJson),
            ResponseBody = attempt.ResponseBody,
            Error = attempt.Error,
            LatencyMs = attempt.LatencyMs,
            CreatedAt = attempt.CreatedAt
        };
    }
}

internal static class JsonValueParser
{
    public static JsonElement ParseOrEmptyObject(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return JsonSerializer.SerializeToElement(new Dictionary<string, string>());

        return ParseOrString(json);
    }

    public static JsonElement? ParseOrNull(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return null;

        return ParseOrString(json);
    }

    private static JsonElement ParseOrString(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            return document.RootElement.Clone();
        }
        catch (JsonException)
        {
            return JsonSerializer.SerializeToElement(json);
        }
    }
}

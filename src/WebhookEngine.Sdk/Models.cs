using System.Text.Json;
using System.Text.Json.Serialization;

namespace WebhookEngine.Sdk;

// ============================================================
// Response envelope
// ============================================================

public class ApiResponse<T>
{
    public T? Data { get; set; }
    public ApiMeta? Meta { get; set; }
    public ApiError? Error { get; set; }
}

public class ApiMeta
{
    public string? RequestId { get; set; }
    public PaginationMeta? Pagination { get; set; }
}

public class PaginationMeta
{
    public int Page { get; set; }
    public int PageSize { get; set; }
    public int? TotalCount { get; set; }
    public int? TotalPages { get; set; }
    public bool? HasNext { get; set; }
    public bool? HasPrev { get; set; }
}

public class ApiError
{
    public string Code { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public List<ApiErrorDetail>? Details { get; set; }
}

public class ApiErrorDetail
{
    public string? Field { get; set; }
    public string? Message { get; set; }
}

// ============================================================
// Event Types
// ============================================================

public class CreateEventTypeRequest
{
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public object? Schema { get; set; }
}

public class UpdateEventTypeRequest
{
    public string? Name { get; set; }
    public string? Description { get; set; }
    public object? Schema { get; set; }
}

public class EventTypeResponse
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public JsonElement? Schema { get; set; }
    public bool IsArchived { get; set; }
    public DateTime CreatedAt { get; set; }
}

// ============================================================
// Endpoints
// ============================================================

public class CreateEndpointRequest
{
    public string Url { get; set; } = string.Empty;
    public string? Description { get; set; }
    public List<Guid>? FilterEventTypes { get; set; }
    public Dictionary<string, string>? CustomHeaders { get; set; }
    public Dictionary<string, string>? Metadata { get; set; }
}

public class UpdateEndpointRequest
{
    public string? Url { get; set; }
    public string? Description { get; set; }
    public List<Guid>? FilterEventTypes { get; set; }
    public Dictionary<string, string>? CustomHeaders { get; set; }
    public Dictionary<string, string>? Metadata { get; set; }
    public string? SecretOverride { get; set; }
}

public class EndpointResponse
{
    public Guid Id { get; set; }
    public Guid AppId { get; set; }
    public string Url { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string Status { get; set; } = string.Empty;
    public Dictionary<string, string>? CustomHeadersJson { get; set; }
    public string? SecretOverride { get; set; }
    public Dictionary<string, string>? MetadataJson { get; set; }
    public List<Guid> FilterEventTypes { get; set; } = [];
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public class EndpointStatsResponse
{
    public Guid EndpointId { get; set; }
    public string Period { get; set; } = string.Empty;
    public int TotalAttempts { get; set; }
    public int Successful { get; set; }
    public int Failed { get; set; }
    public double SuccessRate { get; set; }
    public double AvgLatencyMs { get; set; }
    public double P95LatencyMs { get; set; }
}

// ============================================================
// Messages
// ============================================================

public class SendMessageRequest
{
    public string EventType { get; set; } = string.Empty;
    public Guid? EventTypeId { get; set; }
    public object Payload { get; set; } = new { };
    public string? EventId { get; set; }
    public string? IdempotencyKey { get; set; }
}

public class SendMessageResponse
{
    public List<string> MessageIds { get; set; } = [];
    public int EndpointCount { get; set; }
    public string EventType { get; set; } = string.Empty;
}

public class BatchSendMessagesRequest
{
    public List<SendMessageRequest> Messages { get; set; } = [];
}

public class BatchSendMessagesResponse
{
    public int TotalEvents { get; set; }
    public int AcceptedEvents { get; set; }
    public int RejectedEvents { get; set; }
    public int TotalEnqueuedMessages { get; set; }
    public List<BatchSendResultItem> Results { get; set; } = [];
}

public class BatchSendResultItem
{
    public int Index { get; set; }
    public bool Success { get; set; }
    public string? EventType { get; set; }
    public int? EndpointCount { get; set; }
    public List<string> MessageIds { get; set; } = [];
    public ApiError? Error { get; set; }
}

public class MessageResponse
{
    public Guid Id { get; set; }
    public Guid AppId { get; set; }
    public Guid EndpointId { get; set; }
    public Guid EventTypeId { get; set; }
    public string? EventId { get; set; }
    public string? IdempotencyKey { get; set; }
    public JsonElement? Payload { get; set; }
    public string Status { get; set; } = string.Empty;
    public int AttemptCount { get; set; }
    public int MaxRetries { get; set; }
    public DateTime ScheduledAt { get; set; }
    public DateTime? DeliveredAt { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class RetryMessageResponse
{
    public string MessageId { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public DateTime ScheduledAt { get; set; }
}

public class ReplayMessagesRequest
{
    public string? EventType { get; set; }
    public Guid? EventTypeId { get; set; }
    public Guid? EndpointId { get; set; }
    public DateTime From { get; set; }
    public DateTime To { get; set; }
    public List<string>? Statuses { get; set; }
    public int MaxMessages { get; set; } = 100;
}

public class ReplayMessagesResponse
{
    public int SourceCount { get; set; }
    public int ReplayedCount { get; set; }
    public List<string> MessageIds { get; set; } = [];
    public string EventType { get; set; } = string.Empty;
    public Guid? EndpointId { get; set; }
    public DateTime From { get; set; }
    public DateTime To { get; set; }
    public int MaxMessages { get; set; }
    public List<string> Statuses { get; set; } = [];
}

public class MessageAttemptResponse
{
    public Guid Id { get; set; }
    public Guid MessageId { get; set; }
    public Guid EndpointId { get; set; }
    public int AttemptNumber { get; set; }
    public string Status { get; set; } = string.Empty;
    public int? StatusCode { get; set; }
    public Dictionary<string, string>? RequestHeadersJson { get; set; }
    public string? ResponseBody { get; set; }
    public string? Error { get; set; }
    public int LatencyMs { get; set; }
    public DateTime CreatedAt { get; set; }
}

// ============================================================
// Common query parameters
// ============================================================

public class ListOptions
{
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 20;
}

public class ListMessagesOptions : ListOptions
{
    public string? Status { get; set; }
    public Guid? EndpointId { get; set; }
    public Guid? EventTypeId { get; set; }
    public DateTime? After { get; set; }
    public DateTime? Before { get; set; }
}

public class ListEndpointsOptions : ListOptions
{
    public string? Status { get; set; }
}

public class ListEventTypesOptions : ListOptions
{
    public bool IncludeArchived { get; set; }
}

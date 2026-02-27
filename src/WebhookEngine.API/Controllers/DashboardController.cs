using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WebhookEngine.Core.Enums;
using WebhookEngine.Core.Interfaces;
using WebhookEngine.Infrastructure.Data;
using WebhookEngine.Infrastructure.Repositories;
using Endpoint = WebhookEngine.Core.Entities.Endpoint;

namespace WebhookEngine.API.Controllers;

/// <summary>
/// Dashboard API endpoints — powers the React dashboard.
/// Authenticated via dashboard session cookie (not API key).
/// </summary>
[ApiController]
[Route("api/v1/dashboard")]
[Authorize(AuthenticationSchemes = CookieAuthenticationDefaults.AuthenticationScheme)]
public class DashboardController : ControllerBase
{
    private readonly WebhookDbContext _dbContext;
    private readonly MessageRepository _messageRepository;
    private readonly EndpointRepository _endpointRepository;
    private readonly EventTypeRepository _eventTypeRepository;
    private readonly IMessageQueue _messageQueue;

    public DashboardController(
        WebhookDbContext dbContext,
        MessageRepository messageRepository,
        EndpointRepository endpointRepository,
        EventTypeRepository eventTypeRepository,
        IMessageQueue messageQueue)
    {
        _dbContext = dbContext;
        _messageRepository = messageRepository;
        _endpointRepository = endpointRepository;
        _eventTypeRepository = eventTypeRepository;
        _messageQueue = messageQueue;
    }

    [HttpGet("overview")]
    public async Task<IActionResult> Overview(CancellationToken ct)
    {
        var cutoff = DateTime.UtcNow.AddHours(-24);

        // Last 24h message stats — individual CountAsync calls avoid EF "FirstWithoutOrderBy" warning
        var recentMessages = _dbContext.Messages.AsNoTracking().Where(m => m.CreatedAt >= cutoff);
        var total = await recentMessages.CountAsync(ct);
        var delivered = await recentMessages.CountAsync(m => m.Status == MessageStatus.Delivered, ct);
        var failed = await recentMessages.CountAsync(m => m.Status == MessageStatus.Failed, ct);
        var pending = await recentMessages.CountAsync(m => m.Status == MessageStatus.Pending, ct);
        var deadLetter = await recentMessages.CountAsync(m => m.Status == MessageStatus.DeadLetter, ct);

        // Average latency (last 24h)
        var avgLatency = await _dbContext.MessageAttempts
            .AsNoTracking()
            .Where(a => a.CreatedAt >= cutoff && a.Status == AttemptStatus.Success)
            .AverageAsync(a => (double?)a.LatencyMs, ct) ?? 0;

        // Endpoint health summary — individual counts
        var endpointsQuery = _dbContext.Endpoints.AsNoTracking().Include(e => e.Health);
        var totalEndpoints = await endpointsQuery.CountAsync(ct);
        var healthyEndpoints = await endpointsQuery.CountAsync(e => e.Health == null || e.Health.CircuitState == CircuitState.Closed, ct);
        var degradedEndpoints = await endpointsQuery.CountAsync(e => e.Health != null && e.Health.CircuitState == CircuitState.HalfOpen, ct);
        var failedEndpoints = await endpointsQuery.CountAsync(e => e.Health != null && e.Health.CircuitState == CircuitState.Open, ct);

        // Queue depth (messages currently pending or sending)
        var queueDepth = await _dbContext.Messages
            .AsNoTracking()
            .CountAsync(m => m.Status == MessageStatus.Pending || m.Status == MessageStatus.Sending, ct);

        var successRate = total > 0 ? Math.Round((double)delivered / total * 100, 1) : 0;

        return Ok(new
        {
            data = new
            {
                last24h = new
                {
                    totalMessages = total,
                    delivered,
                    failed,
                    pending,
                    deadLetter,
                    successRate,
                    avgLatencyMs = Math.Round(avgLatency, 0)
                },
                endpoints = new
                {
                    total = totalEndpoints,
                    healthy = healthyEndpoints,
                    degraded = degradedEndpoints,
                    failed = failedEndpoints
                },
                queueDepth
            },
            meta = new { requestId = $"req_{HttpContext.Items["RequestId"]}" }
        });
    }

    [HttpGet("timeline")]
    public async Task<IActionResult> Timeline(
        [FromQuery] string period = "24h",
        [FromQuery] string interval = "1h",
        CancellationToken ct = default)
    {
        var (startTime, intervalMinutes) = ParseTimelineParams(period, interval);

        // Raw query for time-bucketed aggregation — performance-critical, raw SQL is acceptable
        var buckets = await _dbContext.Database
            .SqlQueryRaw<TimelineBucket>(
                """
                SELECT
                    date_bin((@p0 || ' minutes')::interval, created_at, TIMESTAMPTZ '2000-01-01 00:00:00+00') AS timestamp,
                    COUNT(*) FILTER (WHERE status = 'Delivered') AS delivered,
                    COUNT(*) FILTER (WHERE status = 'Failed' OR status = 'DeadLetter') AS failed
                FROM messages
                WHERE created_at >= @p1
                GROUP BY 1
                ORDER BY 1
                """,
                intervalMinutes, startTime)
            .ToListAsync(ct);

        return Ok(new
        {
            data = new { buckets },
            meta = new { requestId = $"req_{HttpContext.Items["RequestId"]}" }
        });
    }

    [HttpPost("messages/{messageId:guid}/retry")]
    public async Task<IActionResult> RetryMessage(Guid messageId, CancellationToken ct)
    {
        var message = await _messageRepository.GetByIdAsync(messageId, ct);
        if (message is null)
        {
            return NotFound(new
            {
                error = new { code = "NOT_FOUND", message = "Message not found." },
                meta = new { requestId = $"req_{HttpContext.Items["RequestId"]}" }
            });
        }

        if (message.Status != MessageStatus.Failed && message.Status != MessageStatus.DeadLetter)
        {
            return UnprocessableEntity(new
            {
                error = new { code = "UNPROCESSABLE", message = "Only failed or dead-letter messages can be retried." },
                meta = new { requestId = $"req_{HttpContext.Items["RequestId"]}" }
            });
        }

        await _messageRepository.RetryAsync(messageId, ct);

        return Ok(new
        {
            data = new { messageId, status = "pending", scheduledAt = DateTime.UtcNow },
            meta = new { requestId = $"req_{HttpContext.Items["RequestId"]}" }
        });
    }

    // ──────────────────────────────────────────────────
    // Endpoints (cross-app, for dashboard admin)
    // ──────────────────────────────────────────────────

    [HttpGet("endpoints")]
    public async Task<IActionResult> ListEndpoints(
        [FromQuery] Guid? appId,
        [FromQuery] EndpointStatus? status,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken ct = default)
    {
        var endpoints = await _endpointRepository.ListAllAsync(appId, status, page, pageSize, ct);
        var totalCount = await _endpointRepository.CountAllAsync(appId, status, ct);
        var totalPages = (int)Math.Ceiling((double)totalCount / pageSize);

        return Ok(new
        {
            data = endpoints.Select(e => new
            {
                id = e.Id,
                appId = e.AppId,
                appName = e.Application?.Name,
                url = e.Url,
                description = e.Description,
                status = e.Status.ToString().ToLowerInvariant(),
                circuitState = e.Health?.CircuitState.ToString().ToLowerInvariant() ?? "closed",
                eventTypes = e.EventTypes.Select(et => et.Name).ToList(),
                eventTypeIds = e.EventTypes.Select(et => et.Id).ToList(),
                createdAt = e.CreatedAt,
                updatedAt = e.UpdatedAt
            }),
            meta = new
            {
                requestId = $"req_{HttpContext.Items["RequestId"]}",
                pagination = new { page, pageSize, totalCount, totalPages, hasNext = page < totalPages, hasPrev = page > 1 }
            }
        });
    }

    [HttpGet("event-types")]
    public async Task<IActionResult> ListEventTypes([FromQuery] Guid appId, CancellationToken ct)
    {
        var appExists = await _dbContext.Applications.AsNoTracking().AnyAsync(a => a.Id == appId, ct);
        if (!appExists)
        {
            return NotFound(new
            {
                error = new { code = "NOT_FOUND", message = "Application not found." },
                meta = new { requestId = $"req_{HttpContext.Items["RequestId"]}" }
            });
        }

        var eventTypes = await _eventTypeRepository.ListByAppIdAsync(appId, includeArchived: false, page: 1, pageSize: 500, ct);

        return Ok(new
        {
            data = eventTypes.Select(et => new
            {
                id = et.Id,
                appId = et.AppId,
                name = et.Name,
                description = et.Description,
                createdAt = et.CreatedAt
            }),
            meta = new { requestId = $"req_{HttpContext.Items["RequestId"]}" }
        });
    }

    [HttpPost("endpoints")]
    public async Task<IActionResult> CreateEndpoint([FromBody] DashboardCreateEndpointRequest request, CancellationToken ct)
    {
        var appExists = await _dbContext.Applications.AsNoTracking().AnyAsync(a => a.Id == request.AppId, ct);
        if (!appExists)
        {
            return NotFound(new
            {
                error = new { code = "NOT_FOUND", message = "Application not found." },
                meta = new { requestId = $"req_{HttpContext.Items["RequestId"]}" }
            });
        }

        var endpoint = new Endpoint
        {
            AppId = request.AppId,
            Url = request.Url,
            Description = request.Description,
            SecretOverride = string.IsNullOrWhiteSpace(request.SecretOverride) ? null : request.SecretOverride,
            CustomHeadersJson = System.Text.Json.JsonSerializer.Serialize(request.CustomHeaders ?? new Dictionary<string, string>()),
            MetadataJson = System.Text.Json.JsonSerializer.Serialize(request.Metadata ?? new Dictionary<string, string>())
        };

        if (request.FilterEventTypes is not null && request.FilterEventTypes.Count > 0)
        {
            foreach (var eventTypeId in request.FilterEventTypes.Distinct())
            {
                var eventType = await _eventTypeRepository.GetByIdAsync(request.AppId, eventTypeId, ct);
                if (eventType is null)
                {
                    return UnprocessableEntity(new
                    {
                        error = new { code = "UNPROCESSABLE", message = $"Event type {eventTypeId} is invalid for this application." },
                        meta = new { requestId = $"req_{HttpContext.Items["RequestId"]}" }
                    });
                }

                endpoint.EventTypes.Add(eventType);
            }
        }

        await _endpointRepository.CreateAsync(endpoint, ct);
        var created = await _endpointRepository.GetByIdAsync(endpoint.Id, ct);

        return Created($"/api/v1/dashboard/endpoints/{endpoint.Id}", new
        {
            data = new
            {
                id = endpoint.Id,
                appId = endpoint.AppId,
                appName = created?.Application?.Name,
                url = endpoint.Url,
                description = endpoint.Description,
                status = endpoint.Status.ToString().ToLowerInvariant(),
                circuitState = created?.Health?.CircuitState.ToString().ToLowerInvariant() ?? "closed",
                eventTypes = created?.EventTypes.Select(et => et.Name).ToList() ?? [],
                eventTypeIds = created?.EventTypes.Select(et => et.Id).ToList() ?? [],
                createdAt = endpoint.CreatedAt,
                updatedAt = endpoint.UpdatedAt
            },
            meta = new { requestId = $"req_{HttpContext.Items["RequestId"]}" }
        });
    }

    [HttpPut("endpoints/{endpointId:guid}")]
    public async Task<IActionResult> UpdateEndpoint(Guid endpointId, [FromBody] DashboardUpdateEndpointRequest request, CancellationToken ct)
    {
        var endpoint = await _endpointRepository.GetByIdAsync(endpointId, ct);
        if (endpoint is null)
        {
            return NotFound(new
            {
                error = new { code = "NOT_FOUND", message = "Endpoint not found." },
                meta = new { requestId = $"req_{HttpContext.Items["RequestId"]}" }
            });
        }

        if (request.Url is not null)
            endpoint.Url = request.Url;

        if (request.Description is not null)
            endpoint.Description = request.Description;

        if (request.SecretOverride is not null)
            endpoint.SecretOverride = string.IsNullOrWhiteSpace(request.SecretOverride) ? null : request.SecretOverride;

        if (request.CustomHeaders is not null)
            endpoint.CustomHeadersJson = System.Text.Json.JsonSerializer.Serialize(request.CustomHeaders);

        if (request.Metadata is not null)
            endpoint.MetadataJson = System.Text.Json.JsonSerializer.Serialize(request.Metadata);

        if (request.FilterEventTypes is not null)
        {
            endpoint.EventTypes.Clear();

            foreach (var eventTypeId in request.FilterEventTypes.Distinct())
            {
                var eventType = await _eventTypeRepository.GetByIdAsync(endpoint.AppId, eventTypeId, ct);
                if (eventType is null)
                {
                    return UnprocessableEntity(new
                    {
                        error = new { code = "UNPROCESSABLE", message = $"Event type {eventTypeId} is invalid for this application." },
                        meta = new { requestId = $"req_{HttpContext.Items["RequestId"]}" }
                    });
                }

                endpoint.EventTypes.Add(eventType);
            }
        }

        await _endpointRepository.UpdateAsync(endpoint, ct);
        var updated = await _endpointRepository.GetByIdAsync(endpoint.Id, ct);

        return Ok(new
        {
            data = new
            {
                id = endpoint.Id,
                appId = endpoint.AppId,
                appName = updated?.Application?.Name,
                url = endpoint.Url,
                description = endpoint.Description,
                status = endpoint.Status.ToString().ToLowerInvariant(),
                circuitState = updated?.Health?.CircuitState.ToString().ToLowerInvariant() ?? "closed",
                eventTypes = updated?.EventTypes.Select(et => et.Name).ToList() ?? [],
                eventTypeIds = updated?.EventTypes.Select(et => et.Id).ToList() ?? [],
                createdAt = endpoint.CreatedAt,
                updatedAt = endpoint.UpdatedAt
            },
            meta = new { requestId = $"req_{HttpContext.Items["RequestId"]}" }
        });
    }

    [HttpPost("endpoints/{endpointId:guid}/disable")]
    public async Task<IActionResult> DisableEndpoint(Guid endpointId, CancellationToken ct)
    {
        var endpoint = await _endpointRepository.GetByIdAsync(endpointId, ct);
        if (endpoint is null)
        {
            return NotFound(new
            {
                error = new { code = "NOT_FOUND", message = "Endpoint not found." },
                meta = new { requestId = $"req_{HttpContext.Items["RequestId"]}" }
            });
        }

        endpoint.Status = EndpointStatus.Disabled;
        await _endpointRepository.UpdateAsync(endpoint, ct);

        return Ok(new
        {
            data = new { id = endpoint.Id, status = endpoint.Status.ToString().ToLowerInvariant() },
            meta = new { requestId = $"req_{HttpContext.Items["RequestId"]}" }
        });
    }

    [HttpPost("endpoints/{endpointId:guid}/enable")]
    public async Task<IActionResult> EnableEndpoint(Guid endpointId, CancellationToken ct)
    {
        var endpoint = await _endpointRepository.GetByIdAsync(endpointId, ct);
        if (endpoint is null)
        {
            return NotFound(new
            {
                error = new { code = "NOT_FOUND", message = "Endpoint not found." },
                meta = new { requestId = $"req_{HttpContext.Items["RequestId"]}" }
            });
        }

        endpoint.Status = EndpointStatus.Active;
        await _endpointRepository.UpdateAsync(endpoint, ct);

        return Ok(new
        {
            data = new { id = endpoint.Id, status = endpoint.Status.ToString().ToLowerInvariant() },
            meta = new { requestId = $"req_{HttpContext.Items["RequestId"]}" }
        });
    }

    [HttpDelete("endpoints/{endpointId:guid}")]
    public async Task<IActionResult> DeleteEndpoint(Guid endpointId, CancellationToken ct)
    {
        var endpoint = await _endpointRepository.GetByIdAsync(endpointId, ct);
        if (endpoint is null)
        {
            return NotFound(new
            {
                error = new { code = "NOT_FOUND", message = "Endpoint not found." },
                meta = new { requestId = $"req_{HttpContext.Items["RequestId"]}" }
            });
        }

        await _endpointRepository.DeleteAsync(endpointId, ct);
        return NoContent();
    }

    // ──────────────────────────────────────────────────
    // Messages (cross-app, for dashboard admin)
    // ──────────────────────────────────────────────────

    [HttpGet("messages")]
    public async Task<IActionResult> ListMessages(
        [FromQuery] Guid? appId,
        [FromQuery] MessageStatus? status,
        [FromQuery] Guid? endpointId,
        [FromQuery] string? eventType,
        [FromQuery] DateTime? after,
        [FromQuery] DateTime? before,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken ct = default)
    {
        var messages = await _messageRepository.ListAllAsync(appId, status, endpointId, eventType, after, before, page, pageSize, ct);
        var totalCount = await _messageRepository.CountAllAsync(appId, status, endpointId, eventType, after, before, ct);
        var totalPages = (int)Math.Ceiling((double)totalCount / pageSize);

        return Ok(new
        {
            data = messages.Select(m => new
            {
                id = m.Id,
                appId = m.AppId,
                endpointId = m.EndpointId,
                endpointUrl = m.Endpoint?.Url,
                eventType = m.EventType?.Name,
                eventTypeId = m.EventTypeId,
                status = m.Status.ToString(),
                attemptCount = m.AttemptCount,
                maxRetries = m.MaxRetries,
                payload = m.Payload,
                eventId = m.EventId,
                scheduledAt = m.ScheduledAt,
                deliveredAt = m.DeliveredAt,
                createdAt = m.CreatedAt
            }),
            meta = new
            {
                requestId = $"req_{HttpContext.Items["RequestId"]}",
                pagination = new { page, pageSize, totalCount, totalPages, hasNext = page < totalPages, hasPrev = page > 1 }
            }
        });
    }

    [HttpPost("messages/send")]
    public async Task<IActionResult> SendMessage([FromBody] DashboardSendMessageRequest request, CancellationToken ct)
    {
        var appExists = await _dbContext.Applications.AsNoTracking().AnyAsync(a => a.Id == request.AppId, ct);
        if (!appExists)
        {
            return NotFound(new
            {
                error = new { code = "NOT_FOUND", message = "Application not found." },
                meta = new { requestId = $"req_{HttpContext.Items["RequestId"]}" }
            });
        }

        Guid eventTypeId;
        string eventTypeName;

        if (request.EventTypeId.HasValue)
        {
            var eventType = await _eventTypeRepository.GetByIdAsync(request.AppId, request.EventTypeId.Value, ct);
            if (eventType is null)
            {
                return UnprocessableEntity(new
                {
                    error = new { code = "UNPROCESSABLE", message = "Event type not found for this application." },
                    meta = new { requestId = $"req_{HttpContext.Items["RequestId"]}" }
                });
            }

            if (!string.IsNullOrWhiteSpace(request.EventType)
                && !string.Equals(request.EventType, eventType.Name, StringComparison.OrdinalIgnoreCase))
            {
                return UnprocessableEntity(new
                {
                    error = new { code = "UNPROCESSABLE", message = "eventType and eventTypeId refer to different event types." },
                    meta = new { requestId = $"req_{HttpContext.Items["RequestId"]}" }
                });
            }

            eventTypeId = eventType.Id;
            eventTypeName = eventType.Name;
        }
        else
        {
            var eventType = await _eventTypeRepository.GetByNameAsync(request.AppId, request.EventType, ct);
            if (eventType is null)
            {
                eventType = new WebhookEngine.Core.Entities.EventType
                {
                    AppId = request.AppId,
                    Name = request.EventType,
                    Description = "Auto-created from dashboard test message"
                };

                await _eventTypeRepository.CreateAsync(eventType, ct);
            }

            eventTypeId = eventType.Id;
            eventTypeName = eventType.Name;
        }

        if (!string.IsNullOrWhiteSpace(request.IdempotencyKey))
        {
            var existingMessages = await _messageRepository.ListByIdempotencyKeyAsync(
                request.AppId,
                request.IdempotencyKey,
                DateTime.UtcNow.AddHours(-24),
                ct);

            if (existingMessages.Count > 0)
            {
                return Accepted(new
                {
                    data = new
                    {
                        messageIds = existingMessages.Select(m => m.Id.ToString()),
                        endpointCount = existingMessages.Count,
                        eventType = eventTypeName
                    },
                    meta = new { requestId = $"req_{HttpContext.Items["RequestId"]}" }
                });
            }
        }

        var endpoints = await _endpointRepository.GetSubscribedEndpointsAsync(request.AppId, eventTypeId, ct);

        if (endpoints.Count == 0)
        {
            return Accepted(new
            {
                data = new { messageIds = Array.Empty<string>(), endpointCount = 0, eventType = eventTypeName },
                meta = new { requestId = $"req_{HttpContext.Items["RequestId"]}" }
            });
        }

        var messageIds = new List<Guid>();

        foreach (var endpoint in endpoints)
        {
            var message = new WebhookEngine.Core.Entities.Message
            {
                AppId = request.AppId,
                EndpointId = endpoint.Id,
                EventTypeId = eventTypeId,
                EventId = request.EventId,
                IdempotencyKey = request.IdempotencyKey,
                Payload = System.Text.Json.JsonSerializer.Serialize(request.Payload),
                Status = MessageStatus.Pending,
                ScheduledAt = DateTime.UtcNow
            };

            await _messageQueue.EnqueueAsync(message, ct);
            messageIds.Add(message.Id);
        }

        return Accepted(new
        {
            data = new
            {
                messageIds = messageIds.Select(id => id.ToString()),
                endpointCount = endpoints.Count,
                eventType = eventTypeName
            },
            meta = new { requestId = $"req_{HttpContext.Items["RequestId"]}" }
        });
    }

    [HttpGet("messages/{messageId:guid}")]
    public async Task<IActionResult> GetMessage(Guid messageId, CancellationToken ct)
    {
        var message = await _messageRepository.GetByIdWithAttemptsAsync(messageId, ct);
        if (message is null)
        {
            return NotFound(new
            {
                error = new { code = "NOT_FOUND", message = "Message not found." },
                meta = new { requestId = $"req_{HttpContext.Items["RequestId"]}" }
            });
        }

        return Ok(new
        {
            data = new
            {
                id = message.Id,
                appId = message.AppId,
                endpointId = message.EndpointId,
                endpointUrl = message.Endpoint?.Url,
                eventType = message.EventType?.Name,
                eventTypeId = message.EventTypeId,
                status = message.Status.ToString(),
                attemptCount = message.AttemptCount,
                maxRetries = message.MaxRetries,
                payload = message.Payload,
                eventId = message.EventId,
                scheduledAt = message.ScheduledAt,
                deliveredAt = message.DeliveredAt,
                createdAt = message.CreatedAt,
                attempts = message.Attempts.Select(a => new
                {
                    id = a.Id,
                    attemptNumber = a.AttemptNumber,
                    status = a.Status.ToString(),
                    statusCode = a.StatusCode,
                    requestHeaders = a.RequestHeadersJson,
                    responseBody = a.ResponseBody,
                    error = a.Error,
                    latencyMs = a.LatencyMs,
                    createdAt = a.CreatedAt
                })
            },
            meta = new { requestId = $"req_{HttpContext.Items["RequestId"]}" }
        });
    }

    // ──────────────────────────────────────────────────
    // Helper
    // ──────────────────────────────────────────────────

    private static (DateTime StartTime, int IntervalMinutes) ParseTimelineParams(string period, string interval)
    {
        var startTime = period switch
        {
            "1h" => DateTime.UtcNow.AddHours(-1),
            "24h" => DateTime.UtcNow.AddHours(-24),
            "7d" => DateTime.UtcNow.AddDays(-7),
            "30d" => DateTime.UtcNow.AddDays(-30),
            _ => DateTime.UtcNow.AddHours(-24)
        };

        var intervalMinutes = interval switch
        {
            "5m" => 5,
            "1h" => 60,
            "1d" => 1440,
            _ => 60
        };

        return (startTime, intervalMinutes);
    }
}

public class TimelineBucket
{
    public DateTime Timestamp { get; set; }
    public int Delivered { get; set; }
    public int Failed { get; set; }
}

public class DashboardCreateEndpointRequest
{
    public Guid AppId { get; set; }
    public string Url { get; set; } = string.Empty;
    public string? Description { get; set; }
    public List<Guid>? FilterEventTypes { get; set; }
    public Dictionary<string, string>? CustomHeaders { get; set; }
    public Dictionary<string, string>? Metadata { get; set; }
    public string? SecretOverride { get; set; }
}

public class DashboardUpdateEndpointRequest
{
    public string? Url { get; set; }
    public string? Description { get; set; }
    public List<Guid>? FilterEventTypes { get; set; }
    public Dictionary<string, string>? CustomHeaders { get; set; }
    public Dictionary<string, string>? Metadata { get; set; }
    public string? SecretOverride { get; set; }
}

public class DashboardSendMessageRequest
{
    public Guid AppId { get; set; }
    public string EventType { get; set; } = string.Empty;
    public Guid? EventTypeId { get; set; }
    public object Payload { get; set; } = new { };
    public string? EventId { get; set; }
    public string? IdempotencyKey { get; set; }
}

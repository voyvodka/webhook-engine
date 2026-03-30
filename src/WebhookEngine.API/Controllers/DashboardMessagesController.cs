using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WebhookEngine.API.Contracts;
using WebhookEngine.Core.Enums;
using WebhookEngine.Core.Interfaces;
using WebhookEngine.Infrastructure.Data;
using WebhookEngine.Infrastructure.Repositories;

namespace WebhookEngine.API.Controllers;

/// <summary>
/// Dashboard message endpoints — listing, detail, send and retry.
/// Authenticated via dashboard session cookie (not API key).
/// </summary>
[ApiController]
[Route("api/v1/dashboard")]
[Authorize(AuthenticationSchemes = CookieAuthenticationDefaults.AuthenticationScheme)]
public class DashboardMessagesController : ControllerBase
{
    private readonly WebhookDbContext _dbContext;
    private readonly MessageRepository _messageRepository;
    private readonly EndpointRepository _endpointRepository;
    private readonly EventTypeRepository _eventTypeRepository;
    private readonly IMessageQueue _messageQueue;

    public DashboardMessagesController(
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
        var pagination = ApiEnvelope.Pagination(page, pageSize, totalCount);

        return Ok(ApiEnvelope.Success(HttpContext,
            messages.Select(m => new
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
            pagination));
    }

    [HttpGet("messages/{messageId:guid}")]
    public async Task<IActionResult> GetMessage(Guid messageId, CancellationToken ct)
    {
        var message = await _messageRepository.GetByIdWithAttemptsAsync(messageId, ct);
        if (message is null)
        {
            return NotFound(ApiEnvelope.Error(HttpContext, "NOT_FOUND", "Message not found."));
        }

        return Ok(ApiEnvelope.Success(HttpContext, new
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
        }));
    }

    [HttpPost("messages/send")]
    public async Task<IActionResult> SendMessage([FromBody] DashboardSendMessageRequest request, CancellationToken ct)
    {
        var appExists = await _dbContext.Applications.AsNoTracking().AnyAsync(a => a.Id == request.AppId, ct);
        if (!appExists)
        {
            return NotFound(ApiEnvelope.Error(HttpContext, "NOT_FOUND", "Application not found."));
        }

        Guid eventTypeId;
        string eventTypeName;

        if (request.EventTypeId.HasValue)
        {
            var eventType = await _eventTypeRepository.GetByIdAsync(request.AppId, request.EventTypeId.Value, ct);
            if (eventType is null)
            {
                return UnprocessableEntity(ApiEnvelope.Error(
                    HttpContext,
                    "UNPROCESSABLE",
                    "Event type not found for this application."));
            }

            if (!string.IsNullOrWhiteSpace(request.EventType)
                && !string.Equals(request.EventType, eventType.Name, StringComparison.OrdinalIgnoreCase))
            {
                return UnprocessableEntity(ApiEnvelope.Error(
                    HttpContext,
                    "UNPROCESSABLE",
                    "eventType and eventTypeId refer to different event types."));
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
                return Accepted(ApiEnvelope.Success(HttpContext, new
                {
                    messageIds = existingMessages.Select(m => m.Id.ToString()),
                    endpointCount = existingMessages.Count,
                    eventType = eventTypeName
                }));
            }
        }

        var endpoints = await _endpointRepository.GetSubscribedEndpointsAsync(request.AppId, eventTypeId, ct);

        if (endpoints.Count == 0)
        {
            return Accepted(ApiEnvelope.Success(HttpContext, new
            {
                messageIds = Array.Empty<string>(),
                endpointCount = 0,
                eventType = eventTypeName
            }));
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

        return Accepted(ApiEnvelope.Success(HttpContext, new
        {
            messageIds = messageIds.Select(id => id.ToString()),
            endpointCount = endpoints.Count,
            eventType = eventTypeName
        }));
    }

    [HttpPost("messages/{messageId:guid}/retry")]
    public async Task<IActionResult> RetryMessage(Guid messageId, CancellationToken ct)
    {
        var message = await _messageRepository.GetByIdAsync(messageId, ct);
        if (message is null)
        {
            return NotFound(ApiEnvelope.Error(HttpContext, "NOT_FOUND", "Message not found."));
        }

        if (message.Status != MessageStatus.Failed && message.Status != MessageStatus.DeadLetter)
        {
            return UnprocessableEntity(ApiEnvelope.Error(
                HttpContext,
                "UNPROCESSABLE",
                "Only failed or dead-letter messages can be retried."));
        }

        await _messageRepository.RetryAsync(messageId, ct);

        return Ok(ApiEnvelope.Success(HttpContext, new
        {
            messageId,
            status = "pending",
            scheduledAt = DateTime.UtcNow
        }));
    }
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

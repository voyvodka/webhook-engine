using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using WebhookEngine.API.Contracts;
using WebhookEngine.Core.Entities;
using WebhookEngine.Core.Enums;
using WebhookEngine.Core.Interfaces;
using WebhookEngine.Infrastructure.Repositories;
using WebhookEngine.Infrastructure.Services;

namespace WebhookEngine.API.Controllers;

[ApiController]
[Produces("application/json")]
[ProducesResponseType<ApiErrorResponse>(StatusCodes.Status401Unauthorized)]
[ProducesResponseType<ApiErrorResponse>(StatusCodes.Status500InternalServerError)]
[Route("api/v1/messages")]
public class MessagesController : ControllerBase
{
    private readonly MessageRepository _messageRepo;
    private readonly EndpointRepository _endpointRepo;
    private readonly EventTypeRepository _eventTypeRepo;
    private readonly IMessageQueue _messageQueue;
    private readonly ApplicationRepository _appRepo;
    private readonly DeliveryLookupCache _lookupCache;

    public MessagesController(
        MessageRepository messageRepo,
        EndpointRepository endpointRepo,
        EventTypeRepository eventTypeRepo,
        IMessageQueue messageQueue,
        ApplicationRepository appRepo,
        DeliveryLookupCache lookupCache)
    {
        _messageRepo = messageRepo;
        _endpointRepo = endpointRepo;
        _eventTypeRepo = eventTypeRepo;
        _messageQueue = messageQueue;
        _appRepo = appRepo;
        _lookupCache = lookupCache;
    }

    [HttpPost]
    [EnableRateLimiting("send-by-appid")]
    public async Task<IActionResult> Send([FromBody] SendMessageRequest request, CancellationToken ct)
    {
        var appId = (Guid)HttpContext.Items["AppId"]!;
        var result = await EnqueueSendRequestAsync(appId, request, ct);
        if (!result.Success)
        {
            return UnprocessableEntity(ApiEnvelope.Error(HttpContext, result.ErrorCode!, result.ErrorMessage!));
        }

        return Accepted(ApiEnvelope.Success(HttpContext, new
        {
            messageIds = result.MessageIds.Select(id => id.ToString()),
            endpointCount = result.EndpointCount,
            eventType = result.EventType
        }));
    }

    [HttpPost("batch")]
    [EnableRateLimiting("send-by-appid")]
    public async Task<IActionResult> BatchSend([FromBody] BatchSendMessagesRequest request, CancellationToken ct)
    {
        var appId = (Guid)HttpContext.Items["AppId"]!;

        var results = new List<object>(request.Messages.Count);
        var totalEnqueuedMessages = 0;
        var acceptedEvents = 0;
        var rejectedEvents = 0;

        for (var index = 0; index < request.Messages.Count; index++)
        {
            var item = request.Messages[index];
            var result = await EnqueueSendRequestAsync(appId, item, ct);

            if (!result.Success)
            {
                rejectedEvents++;
                results.Add(new
                {
                    index,
                    success = false,
                    error = new
                    {
                        code = result.ErrorCode,
                        message = result.ErrorMessage
                    }
                });
                continue;
            }

            acceptedEvents++;
            totalEnqueuedMessages += result.MessageIds.Count;
            results.Add(new
            {
                index,
                success = true,
                eventType = result.EventType,
                endpointCount = result.EndpointCount,
                messageIds = result.MessageIds.Select(id => id.ToString())
            });
        }

        return Accepted(ApiEnvelope.Success(HttpContext, new
        {
            totalEvents = request.Messages.Count,
            acceptedEvents,
            rejectedEvents,
            totalEnqueuedMessages,
            results
        }));
    }

    [HttpPost("replay")]
    public async Task<IActionResult> Replay([FromBody] ReplayMessagesRequest request, CancellationToken ct)
    {
        var appId = (Guid)HttpContext.Items["AppId"]!;

        var eventTypeResolution = await ResolveEventTypeAsync(appId, request.EventType, request.EventTypeId, ct);
        if (!eventTypeResolution.Success)
        {
            return UnprocessableEntity(ApiEnvelope.Error(
                HttpContext,
                eventTypeResolution.ErrorCode!,
                eventTypeResolution.ErrorMessage!));
        }

        var statuses = request.Statuses is { Count: > 0 }
            ? request.Statuses
            : [MessageStatus.Delivered, MessageStatus.Failed, MessageStatus.DeadLetter];

        var sourceMessages = await _messageRepo.ListReplayCandidatesAsync(
            appId,
            eventTypeResolution.EventTypeId,
            request.EndpointId,
            request.From,
            request.To,
            statuses,
            request.MaxMessages,
            ct);

        var replayedMessageIds = new List<string>(sourceMessages.Count);

        foreach (var source in sourceMessages)
        {
            var replayMessage = new Message
            {
                AppId = source.AppId,
                EndpointId = source.EndpointId,
                EventTypeId = source.EventTypeId,
                EventId = source.EventId,
                IdempotencyKey = null,
                Payload = source.Payload,
                Status = MessageStatus.Pending,
                AttemptCount = 0,
                MaxRetries = source.MaxRetries,
                ScheduledAt = DateTime.UtcNow
            };

            await _messageQueue.EnqueueAsync(replayMessage, ct);
            replayedMessageIds.Add(replayMessage.Id.ToString());
        }

        return Accepted(ApiEnvelope.Success(HttpContext, new
        {
            sourceCount = sourceMessages.Count,
            replayedCount = replayedMessageIds.Count,
            messageIds = replayedMessageIds,
            eventType = eventTypeResolution.EventTypeName,
            endpointId = request.EndpointId,
            from = request.From,
            to = request.To,
            maxMessages = request.MaxMessages,
            statuses = statuses.Select(s => s.ToString().ToLowerInvariant())
        }));
    }

    [HttpGet("{messageId:guid}")]
    public async Task<IActionResult> Get(Guid messageId, CancellationToken ct)
    {
        var appId = (Guid)HttpContext.Items["AppId"]!;
        var message = await _messageRepo.GetByIdAsync(appId, messageId, ct);
        if (message is null)
            return NotFound(ApiEnvelope.Error(HttpContext, "NOT_FOUND", "Message not found."));

        return Ok(ApiEnvelope.Success(HttpContext, message.ToDto()));
    }

    [HttpGet]
    public async Task<IActionResult> List(
        [FromQuery] MessageStatus? status,
        [FromQuery] Guid? endpointId,
        [FromQuery] Guid? eventTypeId,
        [FromQuery] DateTime? after,
        [FromQuery] DateTime? before,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken ct = default)
    {
        var appId = (Guid)HttpContext.Items["AppId"]!;
        var messages = await _messageRepo.ListAsync(appId, status, endpointId, eventTypeId, after, before, page, pageSize, ct);
        var totalCount = await _messageRepo.CountAsync(appId, status, endpointId, eventTypeId, after, before, ct);
        var pagination = ApiEnvelope.Pagination(page, pageSize, totalCount);

        return Ok(ApiEnvelope.Success(HttpContext, messages.Select(m => m.ToDto()), pagination));
    }

    [HttpGet("{messageId:guid}/attempts")]
    public async Task<IActionResult> ListAttempts(
        Guid messageId,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken ct = default)
    {
        var appId = (Guid)HttpContext.Items["AppId"]!;
        var message = await _messageRepo.GetByIdAsync(appId, messageId, ct);
        if (message is null)
            return NotFound(ApiEnvelope.Error(HttpContext, "NOT_FOUND", "Message not found."));

        var attempts = await _messageRepo.ListAttemptsAsync(appId, messageId, page, pageSize, ct);
        var totalCount = await _messageRepo.CountAttemptsAsync(appId, messageId, ct);
        var pagination = ApiEnvelope.Pagination(page, pageSize, totalCount);

        return Ok(ApiEnvelope.Success(HttpContext, attempts.Select(a => a.ToDto()), pagination));
    }

    [HttpPost("{messageId:guid}/retry")]
    public async Task<IActionResult> Retry(Guid messageId, CancellationToken ct)
    {
        var appId = (Guid)HttpContext.Items["AppId"]!;
        var message = await _messageRepo.GetByIdAsync(appId, messageId, ct);
        if (message is null)
            return NotFound(ApiEnvelope.Error(HttpContext, "NOT_FOUND", "Message not found."));

        if (message.Status != MessageStatus.Failed && message.Status != MessageStatus.DeadLetter)
        {
            return UnprocessableEntity(ApiEnvelope.Error(
                HttpContext,
                "UNPROCESSABLE",
                "Only failed or dead-letter messages can be retried."));
        }

        await _messageRepo.RetryAsync(messageId, ct);

        return Ok(ApiEnvelope.Success(HttpContext, new
        {
            messageId,
            status = "pending",
            scheduledAt = DateTime.UtcNow
        }));
    }

    private async Task<SendOperationResult> EnqueueSendRequestAsync(Guid appId, SendMessageRequest request, CancellationToken ct)
    {
        var eventTypeResolution = await ResolveEventTypeAsync(appId, request.EventType, request.EventTypeId, ct);
        if (!eventTypeResolution.Success)
            return SendOperationResult.Failure(eventTypeResolution.ErrorCode!, eventTypeResolution.ErrorMessage!);

        if (!string.IsNullOrWhiteSpace(request.IdempotencyKey))
        {
            var app = await _appRepo.GetByIdAsync(appId, ct);
            var windowMinutes = app?.IdempotencyWindowMinutes ?? 1440;
            var existingMessages = await _messageRepo.ListByIdempotencyKeyAsync(
                appId,
                request.IdempotencyKey,
                DateTime.UtcNow.AddMinutes(-windowMinutes),
                ct);

            if (existingMessages.Count > 0)
            {
                return SendOperationResult.Ok(
                    eventTypeResolution.EventTypeName!,
                    existingMessages.Select(m => m.Id).ToList(),
                    existingMessages.Count);
            }
        }

        var endpoints = await _lookupCache.GetSubscribedEndpointsAsync(appId, eventTypeResolution.EventTypeId!.Value, ct);

        if (endpoints.Count == 0)
        {
            return SendOperationResult.Ok(eventTypeResolution.EventTypeName!, [], 0);
        }

        var messageIds = new List<Guid>(endpoints.Count);

        foreach (var endpoint in endpoints)
        {
            var message = new Message
            {
                AppId = appId,
                EndpointId = endpoint.Id,
                EventTypeId = eventTypeResolution.EventTypeId.Value,
                EventId = request.EventId,
                IdempotencyKey = request.IdempotencyKey,
                Payload = System.Text.Json.JsonSerializer.Serialize(request.Payload),
                Status = MessageStatus.Pending,
                ScheduledAt = DateTime.UtcNow
            };

            try
            {
                await _messageQueue.EnqueueAsync(message, ct);
                messageIds.Add(message.Id);
            }
            catch (DbUpdateException ex) when (IsIdempotencyConflict(ex))
            {
                // Concurrent request raced with us on the same (app, endpoint,
                // idempotency_key). Stripe-style replay: fetch the winning row
                // and return its id as if we'd enqueued it ourselves.
                if (!string.IsNullOrWhiteSpace(request.IdempotencyKey))
                {
                    var winner = await _messageRepo.GetByEndpointAndIdempotencyKeyAsync(
                        appId, endpoint.Id, request.IdempotencyKey, ct);
                    if (winner is not null)
                        messageIds.Add(winner.Id);
                }
            }
        }

        return SendOperationResult.Ok(eventTypeResolution.EventTypeName!, messageIds, endpoints.Count);
    }

    private static bool IsIdempotencyConflict(DbUpdateException ex)
        => ex.InnerException is PostgresException
        {
            SqlState: "23505",
            ConstraintName: "idx_messages_app_endpoint_idempotency"
        };

    private async Task<EventTypeResolutionResult> ResolveEventTypeAsync(
        Guid appId,
        string? eventTypeName,
        Guid? eventTypeId,
        CancellationToken ct)
    {
        if (eventTypeId.HasValue)
        {
            var eventType = await _lookupCache.GetEventTypeByIdAsync(appId, eventTypeId.Value, ct);
            if (eventType is null)
                return EventTypeResolutionResult.Failure("UNPROCESSABLE", "Event type not found for this application.");

            if (!string.IsNullOrWhiteSpace(eventTypeName)
                && !string.Equals(eventTypeName, eventType.Name, StringComparison.OrdinalIgnoreCase))
            {
                return EventTypeResolutionResult.Failure("UNPROCESSABLE", "eventType and eventTypeId refer to different event types.");
            }

            return EventTypeResolutionResult.Ok(eventType.Id, eventType.Name);
        }

        var resolvedByName = await _lookupCache.GetEventTypeByNameAsync(appId, eventTypeName ?? string.Empty, ct);
        if (resolvedByName is null)
            return EventTypeResolutionResult.Failure("UNPROCESSABLE", "Event type not found for this application.");

        return EventTypeResolutionResult.Ok(resolvedByName.Id, resolvedByName.Name);
    }

    private sealed record EventTypeResolutionResult(
        bool Success,
        Guid? EventTypeId,
        string? EventTypeName,
        string? ErrorCode,
        string? ErrorMessage)
    {
        public static EventTypeResolutionResult Ok(Guid eventTypeId, string eventTypeName)
            => new(true, eventTypeId, eventTypeName, null, null);

        public static EventTypeResolutionResult Failure(string errorCode, string errorMessage)
            => new(false, null, null, errorCode, errorMessage);
    }

    private sealed record SendOperationResult(
        bool Success,
        string? EventType,
        List<Guid> MessageIds,
        int EndpointCount,
        string? ErrorCode,
        string? ErrorMessage)
    {
        public static SendOperationResult Ok(string eventType, List<Guid> messageIds, int endpointCount)
            => new(true, eventType, messageIds, endpointCount, null, null);

        public static SendOperationResult Failure(string errorCode, string errorMessage)
            => new(false, null, [], 0, errorCode, errorMessage);
    }
}

public class SendMessageRequest
{
    public string EventType { get; set; } = string.Empty;
    public Guid? EventTypeId { get; set; }
    public object Payload { get; set; } = new { };
    public string? EventId { get; set; }
    public string? IdempotencyKey { get; set; }
}

public class ReplayMessagesRequest
{
    public string? EventType { get; set; }
    public Guid? EventTypeId { get; set; }
    public Guid? EndpointId { get; set; }
    public DateTime From { get; set; }
    public DateTime To { get; set; }
    public List<MessageStatus>? Statuses { get; set; }
    public int MaxMessages { get; set; } = 100;
}

public class BatchSendMessagesRequest
{
    public List<SendMessageRequest> Messages { get; set; } = [];
}

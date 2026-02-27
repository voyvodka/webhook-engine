using Microsoft.AspNetCore.Mvc;
using WebhookEngine.Core.Entities;
using WebhookEngine.Core.Enums;
using WebhookEngine.Core.Interfaces;
using WebhookEngine.Infrastructure.Repositories;

namespace WebhookEngine.API.Controllers;

[ApiController]
[Route("api/v1/messages")]
public class MessagesController : ControllerBase
{
    private readonly MessageRepository _messageRepo;
    private readonly EndpointRepository _endpointRepo;
    private readonly EventTypeRepository _eventTypeRepo;
    private readonly IMessageQueue _messageQueue;

    public MessagesController(
        MessageRepository messageRepo,
        EndpointRepository endpointRepo,
        EventTypeRepository eventTypeRepo,
        IMessageQueue messageQueue)
    {
        _messageRepo = messageRepo;
        _endpointRepo = endpointRepo;
        _eventTypeRepo = eventTypeRepo;
        _messageQueue = messageQueue;
    }

    [HttpPost]
    public async Task<IActionResult> Send([FromBody] SendMessageRequest request, CancellationToken ct)
    {
        var appId = (Guid)HttpContext.Items["AppId"]!;

        Guid eventTypeId;
        string eventTypeName;

        if (request.EventTypeId.HasValue)
        {
            var eventType = await _eventTypeRepo.GetByIdAsync(appId, request.EventTypeId.Value, ct);
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
            var eventType = await _eventTypeRepo.GetByNameAsync(appId, request.EventType, ct);
            if (eventType is null)
            {
                return UnprocessableEntity(new
                {
                    error = new { code = "UNPROCESSABLE", message = "Event type not found for this application." },
                    meta = new { requestId = $"req_{HttpContext.Items["RequestId"]}" }
                });
            }

            eventTypeId = eventType.Id;
            eventTypeName = eventType.Name;
        }

        if (!string.IsNullOrWhiteSpace(request.IdempotencyKey))
        {
            var existingMessages = await _messageRepo.ListByIdempotencyKeyAsync(
                appId,
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

        // Find subscribed endpoints
        var endpoints = await _endpointRepo.GetSubscribedEndpointsAsync(appId, eventTypeId, ct);

        if (endpoints.Count == 0)
            return Accepted(new { data = new { messageIds = Array.Empty<string>(), endpointCount = 0, eventType = eventTypeName } });

        var messageIds = new List<Guid>();

        foreach (var endpoint in endpoints)
        {
            var message = new Message
            {
                AppId = appId,
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

    [HttpGet("{messageId:guid}")]
    public async Task<IActionResult> Get(Guid messageId, CancellationToken ct)
    {
        var appId = (Guid)HttpContext.Items["AppId"]!;
        var message = await _messageRepo.GetByIdAsync(appId, messageId, ct);
        if (message is null)
            return NotFound(new { error = new { code = "NOT_FOUND", message = "Message not found." } });

        return Ok(new { data = message, meta = new { requestId = $"req_{HttpContext.Items["RequestId"]}" } });
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

        return Ok(new
        {
            data = messages,
            meta = new
            {
                requestId = $"req_{HttpContext.Items["RequestId"]}",
                pagination = new { page, pageSize }
            }
        });
    }

    [HttpGet("{messageId:guid}/attempts")]
    public async Task<IActionResult> ListAttempts(Guid messageId, [FromQuery] int page = 1, [FromQuery] int pageSize = 20, CancellationToken ct = default)
    {
        var appId = (Guid)HttpContext.Items["AppId"]!;
        var attempts = await _messageRepo.ListAttemptsAsync(appId, messageId, page, pageSize, ct);
        return Ok(new { data = attempts, meta = new { requestId = $"req_{HttpContext.Items["RequestId"]}" } });
    }

    [HttpPost("{messageId:guid}/retry")]
    public async Task<IActionResult> Retry(Guid messageId, CancellationToken ct)
    {
        var appId = (Guid)HttpContext.Items["AppId"]!;
        var message = await _messageRepo.GetByIdAsync(appId, messageId, ct);
        if (message is null)
            return NotFound(new { error = new { code = "NOT_FOUND", message = "Message not found." } });

        if (message.Status != MessageStatus.Failed && message.Status != MessageStatus.DeadLetter)
            return UnprocessableEntity(new { error = new { code = "UNPROCESSABLE", message = "Only failed or dead-letter messages can be retried." } });

        await _messageRepo.RetryAsync(messageId, ct);

        return Ok(new
        {
            data = new { messageId, status = "pending", scheduledAt = DateTime.UtcNow },
            meta = new { requestId = $"req_{HttpContext.Items["RequestId"]}" }
        });
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

using Microsoft.AspNetCore.Mvc;
using WebhookEngine.API.Contracts;
using WebhookEngine.Core.Entities;
using WebhookEngine.Infrastructure.Repositories;

namespace WebhookEngine.API.Controllers;

[ApiController]
[Route("api/v1/event-types")]
public class EventTypesController : ControllerBase
{
    private readonly EventTypeRepository _eventTypeRepo;

    public EventTypesController(EventTypeRepository eventTypeRepo)
    {
        _eventTypeRepo = eventTypeRepo;
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateEventTypeRequest request, CancellationToken ct)
    {
        var appId = (Guid)HttpContext.Items["AppId"]!;

        var existing = await _eventTypeRepo.GetByNameAsync(appId, request.Name, ct);
        if (existing is not null)
        {
            return Conflict(ApiEnvelope.Error(
                HttpContext,
                "CONFLICT",
                $"Event type '{request.Name}' already exists for this application."));
        }

        var eventType = new EventType
        {
            AppId = appId,
            Name = request.Name,
            Description = request.Description,
            SchemaJson = request.Schema is not null
                ? System.Text.Json.JsonSerializer.Serialize(request.Schema)
                : null
        };

        await _eventTypeRepo.CreateAsync(eventType, ct);

        return Created(
            $"/api/v1/event-types/{eventType.Id}",
            ApiEnvelope.Success(HttpContext, eventType.ToDto()));
    }

    [HttpGet]
    public async Task<IActionResult> List(
        [FromQuery] bool includeArchived = false,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken ct = default)
    {
        var appId = (Guid)HttpContext.Items["AppId"]!;
        var eventTypes = await _eventTypeRepo.ListByAppIdAsync(appId, includeArchived, page, pageSize, ct);
        var totalCount = await _eventTypeRepo.CountByAppIdAsync(appId, includeArchived, ct);
        var pagination = ApiEnvelope.Pagination(page, pageSize, totalCount);

        return Ok(ApiEnvelope.Success(HttpContext, eventTypes.Select(et => et.ToDto()), pagination));
    }

    [HttpGet("{eventTypeId:guid}")]
    public async Task<IActionResult> Get(Guid eventTypeId, CancellationToken ct)
    {
        var appId = (Guid)HttpContext.Items["AppId"]!;
        var eventType = await _eventTypeRepo.GetByIdAsync(appId, eventTypeId, ct);
        if (eventType is null)
            return NotFound(ApiEnvelope.Error(HttpContext, "NOT_FOUND", "Event type not found."));

        return Ok(ApiEnvelope.Success(HttpContext, eventType.ToDto()));
    }

    [HttpPut("{eventTypeId:guid}")]
    public async Task<IActionResult> Update(Guid eventTypeId, [FromBody] UpdateEventTypeRequest request, CancellationToken ct)
    {
        var appId = (Guid)HttpContext.Items["AppId"]!;
        var eventType = await _eventTypeRepo.GetByIdAsync(appId, eventTypeId, ct);
        if (eventType is null)
            return NotFound(ApiEnvelope.Error(HttpContext, "NOT_FOUND", "Event type not found."));

        if (eventType.IsArchived)
        {
            return UnprocessableEntity(ApiEnvelope.Error(
                HttpContext,
                "UNPROCESSABLE",
                "Cannot update an archived event type."));
        }

        eventType.Name = request.Name ?? eventType.Name;
        eventType.Description = request.Description ?? eventType.Description;

        if (request.Schema is not null)
            eventType.SchemaJson = System.Text.Json.JsonSerializer.Serialize(request.Schema);

        await _eventTypeRepo.UpdateAsync(eventType, ct);

        return Ok(ApiEnvelope.Success(HttpContext, eventType.ToDto()));
    }

    [HttpDelete("{eventTypeId:guid}")]
    public async Task<IActionResult> Archive(Guid eventTypeId, CancellationToken ct)
    {
        var appId = (Guid)HttpContext.Items["AppId"]!;
        var eventType = await _eventTypeRepo.GetByIdAsync(appId, eventTypeId, ct);
        if (eventType is null)
            return NotFound(ApiEnvelope.Error(HttpContext, "NOT_FOUND", "Event type not found."));

        await _eventTypeRepo.ArchiveAsync(appId, eventTypeId, ct);
        return NoContent();
    }
}

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

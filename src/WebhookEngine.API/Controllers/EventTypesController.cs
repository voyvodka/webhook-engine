using Microsoft.AspNetCore.Mvc;
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

        // Check for duplicate name within same application
        var existing = await _eventTypeRepo.GetByNameAsync(appId, request.Name, ct);
        if (existing is not null)
        {
            return Conflict(new
            {
                error = new { code = "CONFLICT", message = $"Event type '{request.Name}' already exists for this application." },
                meta = new { requestId = $"req_{HttpContext.Items["RequestId"]}" }
            });
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

        return Created($"/api/v1/event-types/{eventType.Id}", new
        {
            data = new
            {
                id = eventType.Id,
                name = eventType.Name,
                description = eventType.Description,
                createdAt = eventType.CreatedAt
            },
            meta = new { requestId = $"req_{HttpContext.Items["RequestId"]}" }
        });
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
        var totalPages = (int)Math.Ceiling((double)totalCount / pageSize);

        return Ok(new
        {
            data = eventTypes.Select(et => new
            {
                id = et.Id,
                name = et.Name,
                description = et.Description,
                isArchived = et.IsArchived,
                createdAt = et.CreatedAt
            }),
            meta = new
            {
                requestId = $"req_{HttpContext.Items["RequestId"]}",
                pagination = new
                {
                    page,
                    pageSize,
                    totalCount,
                    totalPages,
                    hasNext = page < totalPages,
                    hasPrev = page > 1
                }
            }
        });
    }

    [HttpGet("{eventTypeId:guid}")]
    public async Task<IActionResult> Get(Guid eventTypeId, CancellationToken ct)
    {
        var appId = (Guid)HttpContext.Items["AppId"]!;
        var eventType = await _eventTypeRepo.GetByIdAsync(appId, eventTypeId, ct);
        if (eventType is null)
            return NotFound(new { error = new { code = "NOT_FOUND", message = "Event type not found." } });

        return Ok(new
        {
            data = new
            {
                id = eventType.Id,
                name = eventType.Name,
                description = eventType.Description,
                schema = eventType.SchemaJson,
                isArchived = eventType.IsArchived,
                createdAt = eventType.CreatedAt
            },
            meta = new { requestId = $"req_{HttpContext.Items["RequestId"]}" }
        });
    }

    [HttpPut("{eventTypeId:guid}")]
    public async Task<IActionResult> Update(Guid eventTypeId, [FromBody] UpdateEventTypeRequest request, CancellationToken ct)
    {
        var appId = (Guid)HttpContext.Items["AppId"]!;
        var eventType = await _eventTypeRepo.GetByIdAsync(appId, eventTypeId, ct);
        if (eventType is null)
            return NotFound(new { error = new { code = "NOT_FOUND", message = "Event type not found." } });

        if (eventType.IsArchived)
            return UnprocessableEntity(new { error = new { code = "UNPROCESSABLE", message = "Cannot update an archived event type." } });

        eventType.Name = request.Name ?? eventType.Name;
        eventType.Description = request.Description ?? eventType.Description;

        if (request.Schema is not null)
            eventType.SchemaJson = System.Text.Json.JsonSerializer.Serialize(request.Schema);

        await _eventTypeRepo.UpdateAsync(eventType, ct);

        return Ok(new
        {
            data = new
            {
                id = eventType.Id,
                name = eventType.Name,
                description = eventType.Description,
                isArchived = eventType.IsArchived,
                createdAt = eventType.CreatedAt
            },
            meta = new { requestId = $"req_{HttpContext.Items["RequestId"]}" }
        });
    }

    /// <summary>
    /// Soft delete (archive). Existing messages referencing this type are unaffected.
    /// </summary>
    [HttpDelete("{eventTypeId:guid}")]
    public async Task<IActionResult> Archive(Guid eventTypeId, CancellationToken ct)
    {
        var appId = (Guid)HttpContext.Items["AppId"]!;
        var eventType = await _eventTypeRepo.GetByIdAsync(appId, eventTypeId, ct);
        if (eventType is null)
            return NotFound(new { error = new { code = "NOT_FOUND", message = "Event type not found." } });

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

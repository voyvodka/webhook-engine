using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WebhookEngine.API.Contracts;
using WebhookEngine.Core.Enums;
using WebhookEngine.Core.Interfaces;
using WebhookEngine.Infrastructure.Data;
using WebhookEngine.Infrastructure.Repositories;
using Endpoint = WebhookEngine.Core.Entities.Endpoint;

namespace WebhookEngine.API.Controllers;

/// <summary>
/// Dashboard endpoint and event-type CRUD — powers the React dashboard.
/// Authenticated via dashboard session cookie (not API key).
/// </summary>
[ApiController]
[Route("api/v1/dashboard")]
[Authorize(AuthenticationSchemes = CookieAuthenticationDefaults.AuthenticationScheme)]
public class DashboardEndpointController : ControllerBase
{
    private readonly WebhookDbContext _dbContext;
    private readonly EndpointRepository _endpointRepository;
    private readonly EventTypeRepository _eventTypeRepository;
    private readonly IPayloadTransformer _payloadTransformer;

    public DashboardEndpointController(
        WebhookDbContext dbContext,
        EndpointRepository endpointRepository,
        EventTypeRepository eventTypeRepository,
        IPayloadTransformer payloadTransformer)
    {
        _dbContext = dbContext;
        _endpointRepository = endpointRepository;
        _eventTypeRepository = eventTypeRepository;
        _payloadTransformer = payloadTransformer;
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
        var pagination = ApiEnvelope.Pagination(page, pageSize, totalCount);

        return Ok(ApiEnvelope.Success(HttpContext,
            endpoints.Select(e => new
            {
                id = e.Id,
                appId = e.AppId,
                appName = e.AppName,
                url = e.Url,
                description = e.Description,
                status = e.Status.ToString().ToLowerInvariant(),
                circuitState = (e.CircuitState ?? "closed").ToLowerInvariant(),
                eventTypes = e.EventTypeNames,
                eventTypeIds = e.EventTypeIds,
                createdAt = e.CreatedAt,
                updatedAt = e.UpdatedAt
            }),
            pagination));
    }

    [HttpPost("endpoints")]
    public async Task<IActionResult> CreateEndpoint([FromBody] DashboardCreateEndpointRequest request, CancellationToken ct)
    {
        var appExists = await _dbContext.Applications.AsNoTracking().AnyAsync(a => a.Id == request.AppId, ct);
        if (!appExists)
        {
            return NotFound(ApiEnvelope.Error(HttpContext, "NOT_FOUND", "Application not found."));
        }

        var endpoint = new Endpoint
        {
            AppId = request.AppId,
            Url = request.Url,
            Description = request.Description,
            SecretOverride = string.IsNullOrWhiteSpace(request.SecretOverride) ? null : request.SecretOverride,
            CustomHeadersJson = System.Text.Json.JsonSerializer.Serialize(request.CustomHeaders ?? new Dictionary<string, string>()),
            MetadataJson = System.Text.Json.JsonSerializer.Serialize(request.Metadata ?? new Dictionary<string, string>()),
            TransformExpression = string.IsNullOrWhiteSpace(request.TransformExpression) ? null : request.TransformExpression,
            TransformEnabled = request.TransformEnabled ?? false
        };

        if (request.FilterEventTypes is not null && request.FilterEventTypes.Count > 0)
        {
            foreach (var eventTypeId in request.FilterEventTypes.Distinct())
            {
                var eventType = await _eventTypeRepository.GetByIdAsync(request.AppId, eventTypeId, ct);
                if (eventType is null)
                {
                    return UnprocessableEntity(ApiEnvelope.Error(
                        HttpContext,
                        "UNPROCESSABLE",
                        $"Event type {eventTypeId} is invalid for this application."));
                }

                endpoint.EventTypes.Add(eventType);
            }
        }

        await _endpointRepository.CreateAsync(endpoint, ct);
        var created = await _endpointRepository.GetByIdAsync(endpoint.Id, ct);

        return Created($"/api/v1/dashboard/endpoints/{endpoint.Id}", ApiEnvelope.Success(HttpContext, new
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
            transformExpression = endpoint.TransformExpression,
            transformEnabled = endpoint.TransformEnabled,
            transformValidatedAt = endpoint.TransformValidatedAt,
            createdAt = endpoint.CreatedAt,
            updatedAt = endpoint.UpdatedAt
        }));

    }

    [HttpPut("endpoints/{endpointId:guid}")]
    public async Task<IActionResult> UpdateEndpoint(Guid endpointId, [FromBody] DashboardUpdateEndpointRequest request, CancellationToken ct)
    {
        var endpoint = await _endpointRepository.GetByIdAsync(endpointId, ct);
        if (endpoint is null)
        {
            return NotFound(ApiEnvelope.Error(HttpContext, "NOT_FOUND", "Endpoint not found."));
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

        if (request.TransformExpression is not null)
        {
            // Empty/whitespace clears the expression; any new expression resets validation timestamp.
            endpoint.TransformExpression = string.IsNullOrWhiteSpace(request.TransformExpression) ? null : request.TransformExpression;
            endpoint.TransformValidatedAt = null;
        }

        if (request.TransformEnabled is not null)
            endpoint.TransformEnabled = request.TransformEnabled.Value;

        if (request.FilterEventTypes is not null)
        {
            endpoint.EventTypes.Clear();

            foreach (var eventTypeId in request.FilterEventTypes.Distinct())
            {
                var eventType = await _eventTypeRepository.GetByIdAsync(endpoint.AppId, eventTypeId, ct);
                if (eventType is null)
                {
                    return UnprocessableEntity(ApiEnvelope.Error(
                        HttpContext,
                        "UNPROCESSABLE",
                        $"Event type {eventTypeId} is invalid for this application."));
                }

                endpoint.EventTypes.Add(eventType);
            }
        }

        await _endpointRepository.UpdateAsync(endpoint, ct);
        var updated = await _endpointRepository.GetByIdAsync(endpoint.Id, ct);

        return Ok(ApiEnvelope.Success(HttpContext, new
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
            transformExpression = endpoint.TransformExpression,
            transformEnabled = endpoint.TransformEnabled,
            transformValidatedAt = endpoint.TransformValidatedAt,
            createdAt = endpoint.CreatedAt,
            updatedAt = endpoint.UpdatedAt
        }));
    }

    [HttpPost("endpoints/{endpointId:guid}/disable")]
    public async Task<IActionResult> DisableEndpoint(Guid endpointId, CancellationToken ct)
    {
        var endpoint = await _endpointRepository.GetByIdAsync(endpointId, ct);
        if (endpoint is null)
        {
            return NotFound(ApiEnvelope.Error(HttpContext, "NOT_FOUND", "Endpoint not found."));
        }

        endpoint.Status = EndpointStatus.Disabled;
        await _endpointRepository.UpdateAsync(endpoint, ct);

        return Ok(ApiEnvelope.Success(HttpContext, new
        {
            id = endpoint.Id,
            status = endpoint.Status.ToString().ToLowerInvariant()
        }));
    }

    [HttpPost("endpoints/{endpointId:guid}/enable")]
    public async Task<IActionResult> EnableEndpoint(Guid endpointId, CancellationToken ct)
    {
        var endpoint = await _endpointRepository.GetByIdAsync(endpointId, ct);
        if (endpoint is null)
        {
            return NotFound(ApiEnvelope.Error(HttpContext, "NOT_FOUND", "Endpoint not found."));
        }

        endpoint.Status = EndpointStatus.Active;
        await _endpointRepository.UpdateAsync(endpoint, ct);

        return Ok(ApiEnvelope.Success(HttpContext, new
        {
            id = endpoint.Id,
            status = endpoint.Status.ToString().ToLowerInvariant()
        }));
    }

    [HttpDelete("endpoints/{endpointId:guid}")]
    public async Task<IActionResult> DeleteEndpoint(Guid endpointId, CancellationToken ct)
    {
        var endpoint = await _endpointRepository.GetByIdAsync(endpointId, ct);
        if (endpoint is null)
        {
            return NotFound(ApiEnvelope.Error(HttpContext, "NOT_FOUND", "Endpoint not found."));
        }

        await _endpointRepository.DeleteAsync(endpointId, ct);
        return NoContent();
    }

    // ──────────────────────────────────────────────────
    // Payload Transformation
    // ──────────────────────────────────────────────────

    [HttpPost("transform/validate")]
    public IActionResult ValidateTransform([FromBody] ValidateTransformRequest request)
    {
        var result = _payloadTransformer.Transform(request.Expression, request.SamplePayload);

        return Ok(ApiEnvelope.Success(HttpContext, new
        {
            success = result.IsSuccess,
            transformed = result.TransformedPayload,
            error = result.Error
        }));
    }

    // ──────────────────────────────────────────────────
    // Event Types
    // ──────────────────────────────────────────────────

    [HttpGet("event-types")]
    public async Task<IActionResult> ListEventTypes(
        [FromQuery] Guid appId,
        [FromQuery] bool includeArchived = false,
        CancellationToken ct = default)
    {
        var appExists = await _dbContext.Applications.AsNoTracking().AnyAsync(a => a.Id == appId, ct);
        if (!appExists)
        {
            return NotFound(ApiEnvelope.Error(HttpContext, "NOT_FOUND", "Application not found."));
        }

        var eventTypes = await _eventTypeRepository.ListByAppIdAsync(appId, includeArchived, page: 1, pageSize: 500, ct);

        return Ok(ApiEnvelope.Success(HttpContext,
            eventTypes.Select(et => new
            {
                id = et.Id,
                appId = et.AppId,
                name = et.Name,
                description = et.Description,
                isArchived = et.IsArchived,
                createdAt = et.CreatedAt
            })));
    }

    [HttpPost("event-types")]
    public async Task<IActionResult> CreateEventType([FromBody] DashboardCreateEventTypeRequest request, CancellationToken ct)
    {
        var appExists = await _dbContext.Applications.AsNoTracking().AnyAsync(a => a.Id == request.AppId, ct);
        if (!appExists)
        {
            return NotFound(ApiEnvelope.Error(HttpContext, "NOT_FOUND", "Application not found."));
        }

        var existing = await _eventTypeRepository.GetByNameAsync(request.AppId, request.Name, ct);
        if (existing is not null)
        {
            return Conflict(ApiEnvelope.Error(
                HttpContext,
                "CONFLICT",
                $"Event type '{request.Name}' already exists for this application."));
        }

        var eventType = new WebhookEngine.Core.Entities.EventType
        {
            AppId = request.AppId,
            Name = request.Name,
            Description = request.Description
        };

        await _eventTypeRepository.CreateAsync(eventType, ct);

        return Created($"/api/v1/dashboard/event-types/{eventType.Id}", ApiEnvelope.Success(HttpContext, new
        {
            id = eventType.Id,
            appId = eventType.AppId,
            name = eventType.Name,
            description = eventType.Description,
            isArchived = eventType.IsArchived,
            createdAt = eventType.CreatedAt
        }));
    }

    [HttpPut("event-types/{eventTypeId:guid}")]
    public async Task<IActionResult> UpdateEventType(Guid eventTypeId, [FromBody] DashboardUpdateEventTypeRequest request, CancellationToken ct)
    {
        var eventType = await _eventTypeRepository.GetByIdAsync(eventTypeId, ct);
        if (eventType is null)
        {
            return NotFound(ApiEnvelope.Error(HttpContext, "NOT_FOUND", "Event type not found."));
        }

        if (eventType.IsArchived)
        {
            return UnprocessableEntity(ApiEnvelope.Error(
                HttpContext,
                "UNPROCESSABLE",
                "Cannot update an archived event type."));
        }

        if (request.Name is not null
            && !string.Equals(request.Name, eventType.Name, StringComparison.OrdinalIgnoreCase))
        {
            var existing = await _eventTypeRepository.GetByNameAsync(eventType.AppId, request.Name, ct);
            if (existing is not null && existing.Id != eventType.Id)
            {
                return Conflict(ApiEnvelope.Error(
                    HttpContext,
                    "CONFLICT",
                    $"Event type '{request.Name}' already exists for this application."));
            }

            eventType.Name = request.Name;
        }

        if (request.Description is not null)
            eventType.Description = request.Description;

        await _eventTypeRepository.UpdateAsync(eventType, ct);

        return Ok(ApiEnvelope.Success(HttpContext, new
        {
            id = eventType.Id,
            appId = eventType.AppId,
            name = eventType.Name,
            description = eventType.Description,
            isArchived = eventType.IsArchived,
            createdAt = eventType.CreatedAt
        }));
    }

    [HttpDelete("event-types/{eventTypeId:guid}")]
    public async Task<IActionResult> ArchiveEventType(Guid eventTypeId, CancellationToken ct)
    {
        var eventType = await _eventTypeRepository.GetByIdAsync(eventTypeId, ct);
        if (eventType is null)
        {
            return NotFound(ApiEnvelope.Error(HttpContext, "NOT_FOUND", "Event type not found."));
        }

        await _eventTypeRepository.ArchiveAsync(eventTypeId, ct);
        return NoContent();
    }
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
    public string? TransformExpression { get; set; }
    public bool? TransformEnabled { get; set; }
}

public class DashboardUpdateEndpointRequest
{
    public string? Url { get; set; }
    public string? Description { get; set; }
    public List<Guid>? FilterEventTypes { get; set; }
    public Dictionary<string, string>? CustomHeaders { get; set; }
    public Dictionary<string, string>? Metadata { get; set; }
    public string? SecretOverride { get; set; }
    public string? TransformExpression { get; set; }
    public bool? TransformEnabled { get; set; }
}

public class DashboardCreateEventTypeRequest
{
    public Guid AppId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
}

public class DashboardUpdateEventTypeRequest
{
    public string? Name { get; set; }
    public string? Description { get; set; }
}

public class ValidateTransformRequest
{
    public string Expression { get; set; } = string.Empty;
    public string SamplePayload { get; set; } = string.Empty;
}

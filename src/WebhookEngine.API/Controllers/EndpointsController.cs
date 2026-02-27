using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WebhookEngine.Core.Enums;
using WebhookEngine.Infrastructure.Data;
using WebhookEngine.Infrastructure.Repositories;
using Endpoint = WebhookEngine.Core.Entities.Endpoint;

namespace WebhookEngine.API.Controllers;

[ApiController]
[Route("api/v1/endpoints")]
public class EndpointsController : ControllerBase
{
    private readonly EndpointRepository _endpointRepo;
    private readonly EventTypeRepository _eventTypeRepo;
    private readonly WebhookDbContext _dbContext;

    public EndpointsController(
        EndpointRepository endpointRepo,
        EventTypeRepository eventTypeRepo,
        WebhookDbContext dbContext)
    {
        _endpointRepo = endpointRepo;
        _eventTypeRepo = eventTypeRepo;
        _dbContext = dbContext;
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateEndpointRequest request, CancellationToken ct)
    {
        var appId = (Guid)HttpContext.Items["AppId"]!;

        var endpoint = new Endpoint
        {
            AppId = appId,
            Url = request.Url,
            Description = request.Description,
            CustomHeadersJson = System.Text.Json.JsonSerializer.Serialize(request.CustomHeaders ?? new Dictionary<string, string>()),
            MetadataJson = System.Text.Json.JsonSerializer.Serialize(request.Metadata ?? new Dictionary<string, string>())
        };

        if (request.FilterEventTypes is not null && request.FilterEventTypes.Count > 0)
        {
            foreach (var eventTypeId in request.FilterEventTypes.Distinct())
            {
                var eventType = await _eventTypeRepo.GetByIdAsync(appId, eventTypeId, ct);
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

        await _endpointRepo.CreateAsync(endpoint, ct);

        return Created($"/api/v1/endpoints/{endpoint.Id}", new
        {
            data = endpoint,
            meta = new { requestId = $"req_{HttpContext.Items["RequestId"]}" }
        });
    }

    [HttpGet]
    public async Task<IActionResult> List(
        [FromQuery] EndpointStatus? status,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken ct = default)
    {
        var appId = (Guid)HttpContext.Items["AppId"]!;
        var endpoints = await _endpointRepo.ListByAppIdAsync(appId, status, page, pageSize, ct);

        return Ok(new
        {
            data = endpoints,
            meta = new { requestId = $"req_{HttpContext.Items["RequestId"]}", pagination = new { page, pageSize } }
        });
    }

    [HttpGet("{endpointId:guid}")]
    public async Task<IActionResult> Get(Guid endpointId, CancellationToken ct)
    {
        var appId = (Guid)HttpContext.Items["AppId"]!;
        var endpoint = await _endpointRepo.GetByIdAsync(appId, endpointId, ct);
        if (endpoint is null)
            return NotFound(new { error = new { code = "NOT_FOUND", message = "Endpoint not found." } });

        return Ok(new { data = endpoint, meta = new { requestId = $"req_{HttpContext.Items["RequestId"]}" } });
    }

    [HttpPut("{endpointId:guid}")]
    public async Task<IActionResult> Update(Guid endpointId, [FromBody] UpdateEndpointRequest request, CancellationToken ct)
    {
        var appId = (Guid)HttpContext.Items["AppId"]!;
        var endpoint = await _endpointRepo.GetByIdAsync(appId, endpointId, ct);
        if (endpoint is null)
            return NotFound(new { error = new { code = "NOT_FOUND", message = "Endpoint not found." } });

        if (request.Url is not null)
            endpoint.Url = request.Url;

        if (request.Description is not null)
            endpoint.Description = request.Description;

        if (request.CustomHeaders is not null)
            endpoint.CustomHeadersJson = System.Text.Json.JsonSerializer.Serialize(request.CustomHeaders);

        if (request.Metadata is not null)
            endpoint.MetadataJson = System.Text.Json.JsonSerializer.Serialize(request.Metadata);

        if (request.SecretOverride is not null)
            endpoint.SecretOverride = string.IsNullOrWhiteSpace(request.SecretOverride) ? null : request.SecretOverride;

        if (request.FilterEventTypes is not null)
        {
            endpoint.EventTypes.Clear();

            foreach (var eventTypeId in request.FilterEventTypes.Distinct())
            {
                var eventType = await _eventTypeRepo.GetByIdAsync(appId, eventTypeId, ct);
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

        await _endpointRepo.UpdateAsync(endpoint, ct);

        return Ok(new { data = endpoint, meta = new { requestId = $"req_{HttpContext.Items["RequestId"]}" } });
    }

    [HttpPost("{endpointId:guid}/disable")]
    public async Task<IActionResult> Disable(Guid endpointId, CancellationToken ct)
    {
        var appId = (Guid)HttpContext.Items["AppId"]!;
        var endpoint = await _endpointRepo.GetByIdAsync(appId, endpointId, ct);
        if (endpoint is null)
            return NotFound(new { error = new { code = "NOT_FOUND", message = "Endpoint not found." } });

        endpoint.Status = EndpointStatus.Disabled;
        await _endpointRepo.UpdateAsync(endpoint, ct);

        return Ok(new { data = endpoint, meta = new { requestId = $"req_{HttpContext.Items["RequestId"]}" } });
    }

    [HttpPost("{endpointId:guid}/enable")]
    public async Task<IActionResult> Enable(Guid endpointId, CancellationToken ct)
    {
        var appId = (Guid)HttpContext.Items["AppId"]!;
        var endpoint = await _endpointRepo.GetByIdAsync(appId, endpointId, ct);
        if (endpoint is null)
            return NotFound(new { error = new { code = "NOT_FOUND", message = "Endpoint not found." } });

        endpoint.Status = EndpointStatus.Active;
        await _endpointRepo.UpdateAsync(endpoint, ct);

        return Ok(new { data = endpoint, meta = new { requestId = $"req_{HttpContext.Items["RequestId"]}" } });
    }

    [HttpDelete("{endpointId:guid}")]
    public async Task<IActionResult> Delete(Guid endpointId, CancellationToken ct)
    {
        var appId = (Guid)HttpContext.Items["AppId"]!;
        await _endpointRepo.DeleteAsync(appId, endpointId, ct);
        return NoContent();
    }

    [HttpGet("{endpointId:guid}/stats")]
    public async Task<IActionResult> Stats(Guid endpointId, [FromQuery] string period = "24h", CancellationToken ct = default)
    {
        var appId = (Guid)HttpContext.Items["AppId"]!;
        var endpoint = await _endpointRepo.GetByIdAsync(appId, endpointId, ct);
        if (endpoint is null)
            return NotFound(new { error = new { code = "NOT_FOUND", message = "Endpoint not found." } });

        var startAt = period switch
        {
            "1h" => DateTime.UtcNow.AddHours(-1),
            "24h" => DateTime.UtcNow.AddHours(-24),
            "7d" => DateTime.UtcNow.AddDays(-7),
            "30d" => DateTime.UtcNow.AddDays(-30),
            _ => DateTime.UtcNow.AddHours(-24)
        };

        var attemptsQuery = _dbContext.MessageAttempts
            .AsNoTracking()
            .Where(a => a.EndpointId == endpointId && a.CreatedAt >= startAt);

        var totalAttempts = await attemptsQuery.CountAsync(ct);
        var successful = await attemptsQuery.CountAsync(a => a.Status == AttemptStatus.Success, ct);
        var failed = totalAttempts - successful;
        var successRate = totalAttempts > 0 ? Math.Round((double)successful / totalAttempts * 100, 1) : 0;
        var avgLatencyMs = await attemptsQuery.AverageAsync(a => (double?)a.LatencyMs, ct) ?? 0;

        var latencies = await attemptsQuery
            .OrderBy(a => a.LatencyMs)
            .Select(a => a.LatencyMs)
            .ToListAsync(ct);

        var p95LatencyMs = 0;
        if (latencies.Count > 0)
        {
            var percentileIndex = (int)Math.Ceiling(latencies.Count * 0.95) - 1;
            percentileIndex = Math.Clamp(percentileIndex, 0, latencies.Count - 1);
            p95LatencyMs = latencies[percentileIndex];
        }

        return Ok(new
        {
            data = new
            {
                endpointId,
                period,
                totalAttempts,
                successful,
                failed,
                successRate,
                avgLatencyMs = Math.Round(avgLatencyMs, 0),
                p95LatencyMs
            },
            meta = new { requestId = $"req_{HttpContext.Items["RequestId"]}" }
        });
    }
}

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

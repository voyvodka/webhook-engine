using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using WebhookEngine.API.Contracts;
using WebhookEngine.API.Contracts.Portal;
using WebhookEngine.Core.Enums;
using WebhookEngine.Core.Interfaces;
using WebhookEngine.Core.Models;
using WebhookEngine.Infrastructure.Repositories;
using WebhookEngine.Infrastructure.Services;
using EndpointEntity = WebhookEngine.Core.Entities.Endpoint;

namespace WebhookEngine.API.Controllers;

/// <summary>
/// Narrow customer-facing mirror of <see cref="EndpointsController"/> for the
/// embeddable portal. Auth is handled upstream by
/// <see cref="Middleware.PortalTokenAuthMiddleware"/>: by the time any action
/// executes the AppId is in <c>HttpContext.Items["AppId"]</c> and the granted
/// capabilities are in <c>HttpContext.Items["PortalCapabilities"]</c>. We never
/// touch the raw JWT here and never accept an appId from the request.
///
/// Cross-tenant lookups deliberately surface as 404 rather than 403 — returning
/// 403 would leak the existence of resources owned by other applications.
/// </summary>
[ApiController]
[Produces("application/json")]
[ProducesResponseType<ApiErrorResponse>(StatusCodes.Status401Unauthorized)]
[ProducesResponseType<ApiErrorResponse>(StatusCodes.Status403Forbidden)]
[ProducesResponseType<ApiErrorResponse>(StatusCodes.Status404NotFound)]
[ProducesResponseType<ApiErrorResponse>(StatusCodes.Status500InternalServerError)]
[Route("api/v1/portal")]
// Portal tokens are short-lived but the host SaaS can mint one per page
// render — without rate limiting, a leaked token (or a misbehaving host)
// could spam mutating routes (notably /test, which fires a real HTTP POST
// out of the engine). Reuses the existing send-by-appid partition so the
// portal shares the same per-tenant budget as the public API.
[EnableRateLimiting("send-by-appid")]
public class PortalEndpointsController : ControllerBase
{
    private readonly EndpointRepository _endpointRepo;
    private readonly EventTypeRepository _eventTypeRepo;
    private readonly ApplicationRepository _appRepo;
    private readonly MessageRepository _messageRepo;
    private readonly IEndpointTester _endpointTester;

    public PortalEndpointsController(
        EndpointRepository endpointRepo,
        EventTypeRepository eventTypeRepo,
        ApplicationRepository appRepo,
        MessageRepository messageRepo,
        IEndpointTester endpointTester)
    {
        _endpointRepo = endpointRepo;
        _eventTypeRepo = eventTypeRepo;
        _appRepo = appRepo;
        _messageRepo = messageRepo;
        _endpointTester = endpointTester;
    }

    // The auth middleware guarantees these keys exist before any action runs;
    // the casts therefore fail loudly if anyone re-routes without going through
    // the middleware (defense-in-depth, not silent fallback).
    private Guid AppId => (Guid)HttpContext.Items["AppId"]!;
    private HashSet<PortalCapability> Capabilities =>
        (HashSet<PortalCapability>)HttpContext.Items["PortalCapabilities"]!;

    private bool HasCapability(PortalCapability cap) => Capabilities.Contains(cap);

    private IActionResult Forbidden() => StatusCode(
        StatusCodes.Status403Forbidden,
        ApiEnvelope.Error(HttpContext, "PORTAL_INSUFFICIENT_CAPABILITY",
            "The portal token does not grant the required capability."));

    private IActionResult PortalNotFound() => NotFound(
        ApiEnvelope.Error(HttpContext, "PORTAL_NOT_FOUND", "Endpoint not found."));

    // ── Endpoints ──────────────────────────────────────

    [HttpGet("endpoints")]
    public async Task<IActionResult> List(
        [FromQuery] EndpointStatus? status,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken ct = default)
    {
        if (!HasCapability(PortalCapability.EndpointsRead))
            return Forbidden();

        var endpoints = await _endpointRepo.ListByAppIdAsync(AppId, status, page, pageSize, ct);
        var totalCount = await _endpointRepo.CountByAppIdAsync(AppId, status, ct);
        var pagination = ApiEnvelope.Pagination(page, pageSize, totalCount);

        return Ok(ApiEnvelope.Success(
            HttpContext,
            endpoints.Select(e => e.ToPortalListItem()),
            pagination));
    }

    [HttpGet("endpoints/{endpointId:guid}")]
    public async Task<IActionResult> Get(Guid endpointId, CancellationToken ct)
    {
        if (!HasCapability(PortalCapability.EndpointsRead))
            return Forbidden();

        var endpoint = await _endpointRepo.GetByIdAsync(AppId, endpointId, ct);
        if (endpoint is null)
            return PortalNotFound();

        return Ok(ApiEnvelope.Success(HttpContext, endpoint.ToPortalDetail()));
    }

    [HttpPost("endpoints")]
    public async Task<IActionResult> Create(
        [FromBody] PortalCreateEndpointRequest request,
        CancellationToken ct)
    {
        if (!HasCapability(PortalCapability.EndpointsWrite))
            return Forbidden();

        var endpoint = new EndpointEntity
        {
            AppId = AppId,
            Url = request.Url,
            Description = request.Description,
            CustomHeadersJson = System.Text.Json.JsonSerializer.Serialize(request.CustomHeaders ?? new Dictionary<string, string>()),
            MetadataJson = System.Text.Json.JsonSerializer.Serialize(request.Metadata ?? new Dictionary<string, string>()),
            SecretOverride = string.IsNullOrWhiteSpace(request.SecretOverride) ? null : request.SecretOverride
            // Transform fields and AllowedIpsJson are intentionally left at default.
            // The portal DTO does not expose them; even if a caller smuggles the
            // properties through, model binding drops the unknown keys before the
            // request reaches this point.
        };

        if (request.FilterEventTypes is { Count: > 0 })
        {
            foreach (var eventTypeId in request.FilterEventTypes.Distinct())
            {
                var eventType = await _eventTypeRepo.GetByIdAsync(AppId, eventTypeId, ct);
                if (eventType is null)
                {
                    return UnprocessableEntity(ApiEnvelope.Error(
                        HttpContext,
                        "PORTAL_VALIDATION_FAILED",
                        $"Event type {eventTypeId} is invalid for this application."));
                }

                endpoint.EventTypes.Add(eventType);
            }
        }

        await _endpointRepo.CreateAsync(endpoint, ct);
        DeliveryLookupCache.InvalidateApplication(AppId);
        var created = await _endpointRepo.GetByIdAsync(AppId, endpoint.Id, ct) ?? endpoint;

        return Created(
            $"/api/v1/portal/endpoints/{endpoint.Id}",
            ApiEnvelope.Success(HttpContext, created.ToPortalDetail()));
    }

    [HttpPatch("endpoints/{endpointId:guid}")]
    public async Task<IActionResult> Update(
        Guid endpointId,
        [FromBody] PortalUpdateEndpointRequest request,
        CancellationToken ct)
    {
        if (!HasCapability(PortalCapability.EndpointsWrite))
            return Forbidden();

        var endpoint = await _endpointRepo.GetByIdAsync(AppId, endpointId, ct);
        if (endpoint is null)
            return PortalNotFound();

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
                var eventType = await _eventTypeRepo.GetByIdAsync(AppId, eventTypeId, ct);
                if (eventType is null)
                {
                    return UnprocessableEntity(ApiEnvelope.Error(
                        HttpContext,
                        "PORTAL_VALIDATION_FAILED",
                        $"Event type {eventTypeId} is invalid for this application."));
                }

                endpoint.EventTypes.Add(eventType);
            }
        }

        await _endpointRepo.UpdateAsync(endpoint, ct);
        DeliveryLookupCache.InvalidateApplication(AppId);
        var updated = await _endpointRepo.GetByIdAsync(AppId, endpoint.Id, ct) ?? endpoint;

        return Ok(ApiEnvelope.Success(HttpContext, updated.ToPortalDetail()));
    }

    [HttpPost("endpoints/{endpointId:guid}/enable")]
    public async Task<IActionResult> Enable(Guid endpointId, CancellationToken ct)
    {
        if (!HasCapability(PortalCapability.EndpointsWrite))
            return Forbidden();

        var endpoint = await _endpointRepo.GetByIdAsync(AppId, endpointId, ct);
        if (endpoint is null)
            return PortalNotFound();

        endpoint.Status = EndpointStatus.Active;
        await _endpointRepo.UpdateAsync(endpoint, ct);
        DeliveryLookupCache.InvalidateApplication(AppId);

        return Ok(ApiEnvelope.Success(HttpContext, endpoint.ToPortalDetail()));
    }

    [HttpPost("endpoints/{endpointId:guid}/disable")]
    public async Task<IActionResult> Disable(Guid endpointId, CancellationToken ct)
    {
        if (!HasCapability(PortalCapability.EndpointsWrite))
            return Forbidden();

        var endpoint = await _endpointRepo.GetByIdAsync(AppId, endpointId, ct);
        if (endpoint is null)
            return PortalNotFound();

        endpoint.Status = EndpointStatus.Disabled;
        await _endpointRepo.UpdateAsync(endpoint, ct);
        DeliveryLookupCache.InvalidateApplication(AppId);

        return Ok(ApiEnvelope.Success(HttpContext, endpoint.ToPortalDetail()));
    }

    [HttpDelete("endpoints/{endpointId:guid}")]
    public async Task<IActionResult> Delete(Guid endpointId, CancellationToken ct)
    {
        if (!HasCapability(PortalCapability.EndpointsWrite))
            return Forbidden();

        // App-scoped existence check first — without it, a delete of someone
        // else's endpoint would succeed silently (no rows updated, NoContent
        // returned) which is a quieter but equally bad cross-tenant leak.
        var endpoint = await _endpointRepo.GetByIdAsync(AppId, endpointId, ct);
        if (endpoint is null)
            return PortalNotFound();

        await _endpointRepo.DeleteAsync(AppId, endpointId, ct);
        DeliveryLookupCache.InvalidateApplication(AppId);
        return NoContent();
    }

    [HttpPost("endpoints/{endpointId:guid}/test")]
    public async Task<IActionResult> SendTest(
        Guid endpointId,
        [FromBody] PortalEndpointTestRequest request,
        CancellationToken ct)
    {
        if (!HasCapability(PortalCapability.EndpointsTest))
            return Forbidden();

        var endpoint = await _endpointRepo.GetByIdAsync(AppId, endpointId, ct);
        if (endpoint is null)
            return PortalNotFound();

        var application = await _appRepo.GetByIdAsync(AppId, ct);
        if (application is null)
            return PortalNotFound();

        var context = new EndpointTestContext
        {
            Endpoint = endpoint,
            Application = application,
            Request = new EndpointTestRequest
            {
                EventTypeName = request.EventType ?? string.Empty,
                Payload = request.Payload
            }
        };

        var result = await _endpointTester.ExecuteAsync(context, ct);
        // Portal-specific redaction: the shared tester returns the operator's custom
        // header VALUES verbatim (correct for the owner-facing API/dashboard callers),
        // but the portal must never surface them to end-customers.
        return Ok(ApiEnvelope.Success(HttpContext, result.ToPortalTestResult(endpoint.CustomHeadersJson)));
    }

    [HttpGet("endpoints/{endpointId:guid}/attempts")]
    public async Task<IActionResult> Attempts(
        Guid endpointId,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken ct = default)
    {
        if (!HasCapability(PortalCapability.AttemptsRead))
            return Forbidden();

        // Existence check is app-scoped so cross-tenant probes return 404,
        // and so a missing endpoint never reads as "valid id, zero attempts".
        var endpoint = await _endpointRepo.GetByIdAsync(AppId, endpointId, ct);
        if (endpoint is null)
            return PortalNotFound();

        var attempts = await _messageRepo.ListAttemptsByEndpointAsync(AppId, endpointId, page, pageSize, ct);
        var totalCount = await _messageRepo.CountAttemptsByEndpointAsync(AppId, endpointId, ct);
        var pagination = ApiEnvelope.Pagination(page, pageSize, totalCount);

        return Ok(ApiEnvelope.Success(
            HttpContext,
            attempts.Select(a => a.ToPortalRow()),
            pagination));
    }

    // ── Event types (read-only dropdown source) ────────

    [HttpGet("event-types")]
    public async Task<IActionResult> EventTypes(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 100,
        CancellationToken ct = default)
    {
        if (!HasCapability(PortalCapability.EndpointsRead))
            return Forbidden();

        // includeArchived: false — archived event types are admin-only history.
        var eventTypes = await _eventTypeRepo.ListByAppIdAsync(AppId, includeArchived: false, page, pageSize, ct);
        var totalCount = await _eventTypeRepo.CountByAppIdAsync(AppId, includeArchived: false, ct);
        var pagination = ApiEnvelope.Pagination(page, pageSize, totalCount);

        return Ok(ApiEnvelope.Success(
            HttpContext,
            eventTypes.Select(et => et.ToPortalListItem()),
            pagination));
    }
}

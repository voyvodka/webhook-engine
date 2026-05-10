using System.Text.Json;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WebhookEngine.API.Contracts;
using WebhookEngine.Infrastructure.Repositories;

namespace WebhookEngine.API.Controllers;

/// <summary>
/// Read-only access to the append-only <c>audit_logs</c> table. Dashboard
/// users can scan recent admin actions, filter by app / action / resource,
/// and pull the before/after snapshots for a row to diff a regression.
/// </summary>
[ApiController]
[Produces("application/json")]
[ProducesResponseType<ApiErrorResponse>(StatusCodes.Status401Unauthorized)]
[ProducesResponseType<ApiErrorResponse>(StatusCodes.Status500InternalServerError)]
[Route("api/v1/dashboard/audit-logs")]
[Authorize(AuthenticationSchemes = CookieAuthenticationDefaults.AuthenticationScheme)]
public class AuditLogsController : ControllerBase
{
    private const int MaxPageSize = 100;

    private readonly AuditLogRepository _auditLogRepo;

    public AuditLogsController(AuditLogRepository auditLogRepo)
    {
        _auditLogRepo = auditLogRepo;
    }

    [HttpGet]
    public async Task<IActionResult> List(
        [FromQuery] Guid? appId,
        [FromQuery] string? action,
        [FromQuery] string? resourceType,
        [FromQuery] Guid? resourceId,
        [FromQuery] DateTime? from,
        [FromQuery] DateTime? to,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken ct = default)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, MaxPageSize);

        var (rows, total) = await _auditLogRepo.ListAsync(
            appId, action, resourceType, resourceId, from, to, page, pageSize, ct);

        // Re-shape before/after as JsonElement so the client can render them
        // as structured JSON instead of escaped strings.
        var hydrated = rows.Select(r => new
        {
            id = r.Id,
            appId = r.AppId,
            userId = r.UserId,
            action = r.Action,
            resourceType = r.ResourceType,
            resourceId = r.ResourceId,
            before = ParseJson(r.BeforeJson),
            after = ParseJson(r.AfterJson),
            ipAddress = r.IpAddress,
            userAgent = r.UserAgent,
            createdAt = r.CreatedAt
        });

        return Ok(ApiEnvelope.Success(HttpContext, hydrated, ApiEnvelope.Pagination(page, pageSize, total)));
    }

    private static JsonElement? ParseJson(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.Clone();
        }
        catch (JsonException)
        {
            return null;
        }
    }
}

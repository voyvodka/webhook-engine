using System.Text.Json;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WebhookEngine.API.Contracts;
using WebhookEngine.Infrastructure.Data;

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

    private readonly WebhookDbContext _dbContext;

    public AuditLogsController(WebhookDbContext dbContext)
    {
        _dbContext = dbContext;
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

        var query = _dbContext.AuditLogs.AsNoTracking().AsQueryable();

        if (appId.HasValue) query = query.Where(l => l.AppId == appId);
        if (!string.IsNullOrWhiteSpace(action)) query = query.Where(l => l.Action == action);
        if (!string.IsNullOrWhiteSpace(resourceType)) query = query.Where(l => l.ResourceType == resourceType);
        if (resourceId.HasValue) query = query.Where(l => l.ResourceId == resourceId);
        if (from.HasValue) query = query.Where(l => l.CreatedAt >= from);
        if (to.HasValue) query = query.Where(l => l.CreatedAt <= to);

        var total = await query.CountAsync(ct);
        var rows = await query
            .OrderByDescending(l => l.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(l => new
            {
                id = l.Id,
                appId = l.AppId,
                userId = l.UserId,
                action = l.Action,
                resourceType = l.ResourceType,
                resourceId = l.ResourceId,
                before = l.BeforeJson,
                after = l.AfterJson,
                ipAddress = l.IpAddress,
                userAgent = l.UserAgent,
                createdAt = l.CreatedAt
            })
            .ToListAsync(ct);

        // Re-shape before/after as JsonElement so the client can render them
        // as structured JSON instead of escaped strings.
        var hydrated = rows.Select(r => new
        {
            r.id,
            r.appId,
            r.userId,
            r.action,
            r.resourceType,
            r.resourceId,
            before = ParseJson(r.before),
            after = ParseJson(r.after),
            r.ipAddress,
            r.userAgent,
            r.createdAt
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

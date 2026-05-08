using System.Security.Claims;
using WebhookEngine.Core.Interfaces;

namespace WebhookEngine.API.Audit;

/// <summary>
/// Controller-side helpers that pull the acting user, IP, and user-agent
/// off <see cref="HttpContext"/> and call <see cref="IAuditLogger"/> with a
/// fully-populated <see cref="AuditLogEntry"/>. Keeps each call site to a
/// single line so adding audit coverage to a new action is friction-free.
/// </summary>
public static class AuditContextExtensions
{
    public static Task LogActionAsync(
        this IAuditLogger logger,
        HttpContext httpContext,
        string action,
        string resourceType,
        Guid? resourceId,
        Guid? appId = null,
        object? before = null,
        object? after = null,
        CancellationToken ct = default)
    {
        return logger.LogAsync(new AuditLogEntry
        {
            Action = action,
            ResourceType = resourceType,
            ResourceId = resourceId,
            AppId = appId,
            UserId = ResolveUserId(httpContext),
            Before = before,
            After = after,
            IpAddress = ResolveIpAddress(httpContext),
            UserAgent = ResolveUserAgent(httpContext)
        }, ct);
    }

    private static Guid? ResolveUserId(HttpContext httpContext)
    {
        var raw = httpContext.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        return Guid.TryParse(raw, out var id) ? id : null;
    }

    private static string? ResolveIpAddress(HttpContext httpContext)
    {
        // Prefer the proxied client IP when the deployment runs behind a
        // reverse proxy that sets X-Forwarded-For; fall back to the socket
        // peer otherwise.
        var forwarded = httpContext.Request.Headers["X-Forwarded-For"].FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(forwarded))
        {
            // Take the first hop only — that's the real client; the rest are
            // proxies between us and them.
            var firstHop = forwarded.Split(',', StringSplitOptions.RemoveEmptyEntries)[0].Trim();
            if (!string.IsNullOrWhiteSpace(firstHop))
            {
                return firstHop;
            }
        }

        return httpContext.Connection.RemoteIpAddress?.ToString();
    }

    private static string? ResolveUserAgent(HttpContext httpContext)
    {
        return httpContext.Request.Headers.UserAgent.FirstOrDefault();
    }
}

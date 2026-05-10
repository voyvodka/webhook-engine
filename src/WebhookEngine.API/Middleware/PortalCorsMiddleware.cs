using WebhookEngine.Core.Utilities;
using WebhookEngine.Infrastructure.Repositories;
using WebhookEngine.Infrastructure.Services;

namespace WebhookEngine.API.Middleware;

/// <summary>
/// Per-app CORS for the embeddable customer portal. Only touches paths under
/// <c>/api/v1/portal/</c>. Allowed origins are stored on each
/// <c>Application.AllowedPortalOriginsJson</c> — wildcards are intentionally
/// not supported.
///
/// Ordering rule (load-bearing): for non-OPTIONS requests this middleware
/// MUST run AFTER <see cref="PortalTokenAuthMiddleware"/>, because it reads
/// <c>HttpContext.Items["PortalAppLookup"]</c> populated by the token
/// validator. OPTIONS preflight has no token (browsers don't send one), so
/// this middleware does its own bounded "any-app-allows-this-origin" lookup
/// against <see cref="ApplicationRepository.AnyAllowsPortalOriginAsync"/>.
/// </summary>
public class PortalCorsMiddleware
{
    private const string PortalPathPrefix = "/api/v1/portal/";
    private const string AllowedMethods = "GET,POST,PUT,DELETE,OPTIONS";
    private const string AllowedHeaders = "Authorization,Content-Type";
    private const string PreflightMaxAge = "600";

    private readonly RequestDelegate _next;

    public PortalCorsMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var path = context.Request.Path.Value ?? string.Empty;
        if (!path.StartsWith(PortalPathPrefix, StringComparison.Ordinal))
        {
            await _next(context);
            return;
        }

        var origin = context.Request.Headers.Origin.FirstOrDefault();
        if (string.IsNullOrEmpty(origin))
        {
            await _next(context);
            return;
        }

        if (HttpMethods.IsOptions(context.Request.Method))
        {
            await HandlePreflightAsync(context, origin);
            return;
        }

        // Real request: token middleware should have populated the lookup
        // in Items. If it didn't, the request is already on its way to a
        // 401 from the token middleware — don't add CORS headers since the
        // browser would surface a confusing CORS error rather than the
        // intended 401.
        if (context.Items[PortalTokenAuthMiddleware.AppLookupItemKey] is not PortalAppLookup lookup)
        {
            await _next(context);
            return;
        }

        // RFC 6454 §4 makes scheme + host case-insensitive. Browsers send a
        // lowercased Origin but a stored allowlist entry may have drifted.
        if (lookup.AllowedOrigins.Any(o => string.Equals(o, origin, StringComparison.OrdinalIgnoreCase)))
        {
            ApplyCorsHeaders(context, origin);
        }

        await _next(context);
    }

    private static async Task HandlePreflightAsync(HttpContext context, string origin)
    {
        var appRepo = context.RequestServices.GetRequiredService<ApplicationRepository>();
        var allowed = await appRepo.AnyAllowsPortalOriginAsync(origin, context.RequestAborted);

        var logger = context.RequestServices
            .GetRequiredService<ILoggerFactory>()
            .CreateLogger("WebhookEngine.API.Middleware.PortalCorsMiddleware");

        if (!allowed)
        {
            logger.LogInformation("Portal CORS preflight rejected for origin {Origin}.",
                LogSanitizer.ForLog(origin));
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            return;
        }

        ApplyCorsHeaders(context, origin);
        context.Response.Headers.Append("Access-Control-Allow-Methods", AllowedMethods);
        context.Response.Headers.Append("Access-Control-Allow-Headers", AllowedHeaders);
        context.Response.Headers.Append("Access-Control-Max-Age", PreflightMaxAge);
        context.Response.StatusCode = StatusCodes.Status204NoContent;
    }

    private static void ApplyCorsHeaders(HttpContext context, string origin)
    {
        context.Response.Headers.Append("Access-Control-Allow-Origin", origin);
        context.Response.Headers.Append("Vary", "Origin");
    }
}

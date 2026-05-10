using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using WebhookEngine.Core.Enums;
using WebhookEngine.Core.Options;
using WebhookEngine.Core.Utilities;
using WebhookEngine.Infrastructure.Services;

namespace WebhookEngine.API.Middleware;

/// <summary>
/// Validates short-lived HS256 JWTs minted by host SaaS backends for the
/// embeddable customer portal. The host signs with the per-app
/// <c>PortalSigningKey</c>; this middleware only verifies — it never mints
/// and never returns the signing key (that lives on the rotation endpoint).
///
/// Only paths under <c>/api/v1/portal/</c> are touched; CORS preflight
/// (<c>OPTIONS</c>) is left to <see cref="PortalCorsMiddleware"/>.
///
/// On success: populates <c>HttpContext.Items["AppId"]</c>,
/// <c>["PortalCapabilities"]</c>, and <c>["PortalAppLookup"]</c>.
/// </summary>
public class PortalTokenAuthMiddleware
{
    public const string AppIdItemKey = "AppId";
    public const string CapabilitiesItemKey = "PortalCapabilities";
    public const string AppLookupItemKey = "PortalAppLookup";

    private const string PortalPathPrefix = "/api/v1/portal/";
    private const string AppIdClaim = "appId";
    private const string CapabilitiesClaim = "capabilities";

    private static readonly JwtSecurityTokenHandler TokenHandler = new();

    private readonly RequestDelegate _next;

    public PortalTokenAuthMiddleware(RequestDelegate next)
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

        if (HttpMethods.IsOptions(context.Request.Method))
        {
            await _next(context);
            return;
        }

        var logger = context.RequestServices
            .GetRequiredService<ILoggerFactory>()
            .CreateLogger("WebhookEngine.API.Middleware.PortalTokenAuthMiddleware");

        var authHeader = context.Request.Headers.Authorization.FirstOrDefault();
        if (string.IsNullOrEmpty(authHeader) || !authHeader.StartsWith("Bearer ", StringComparison.Ordinal))
        {
            await WriteUnauthorizedAsync(context, "PORTAL_AUTH_REQUIRED",
                "Portal endpoints require a Bearer JWT minted by the host application.");
            return;
        }

        var rawToken = authHeader["Bearer ".Length..].Trim();
        if (string.IsNullOrEmpty(rawToken))
        {
            await WriteUnauthorizedAsync(context, "PORTAL_AUTH_REQUIRED",
                "Portal endpoints require a Bearer JWT minted by the host application.");
            return;
        }

        // Read the token without validation first to extract the appId claim
        // — needed to find the per-app signing key. The signature check
        // happens in step 6; until then this MUST NOT trust any other claim.
        JwtSecurityToken unverified;
        try
        {
            unverified = TokenHandler.ReadJwtToken(rawToken);
        }
        catch (Exception)
        {
            await WriteUnauthorizedAsync(context, "PORTAL_AUTH_INVALID_TOKEN",
                "Portal token is malformed.");
            return;
        }

        var appIdClaimValue = unverified.Claims.FirstOrDefault(c => c.Type == AppIdClaim)?.Value;
        if (string.IsNullOrEmpty(appIdClaimValue) || !Guid.TryParse(appIdClaimValue, out var appId))
        {
            await WriteUnauthorizedAsync(context, "PORTAL_AUTH_INVALID_TOKEN",
                "Portal token is missing a valid appId claim.");
            return;
        }

        var lookupCache = context.RequestServices.GetRequiredService<PortalLookupCache>();
        var lookup = await lookupCache.GetAsync(appId, context.RequestAborted);
        if (lookup is null)
        {
            logger.LogWarning("Portal auth rejected for app {AppId}: portal not enabled.",
                LogSanitizer.ForLog(appId.ToString()));
            await WriteUnauthorizedAsync(context, "PORTAL_NOT_ENABLED",
                "The portal is not enabled for this application.");
            return;
        }

        var options = context.RequestServices.GetRequiredService<IOptions<PortalAuthOptions>>().Value;

        var validationParameters = new TokenValidationParameters
        {
            ValidateIssuer = false,
            ValidateAudience = false,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            RequireSignedTokens = true,
            RequireExpirationTime = true,
            ClockSkew = TimeSpan.FromSeconds(options.ClockSkewSeconds),
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(lookup.PortalSigningKey)),
            // HS256-only allowlist — defends against algorithm confusion
            // (e.g. unsigned "alg=none" tokens, or RS256 tokens whose public
            // key is interpreted as an HMAC secret).
            ValidAlgorithms = new[] { SecurityAlgorithms.HmacSha256 }
        };

        ClaimsPrincipal principal;
        SecurityToken validatedToken;
        try
        {
            principal = TokenHandler.ValidateToken(rawToken, validationParameters, out validatedToken);
        }
        catch (SecurityTokenExpiredException)
        {
            await WriteUnauthorizedAsync(context, "PORTAL_AUTH_TOKEN_EXPIRED",
                "Portal token has expired.");
            return;
        }
        catch (SecurityTokenSignatureKeyNotFoundException)
        {
            await WriteUnauthorizedAsync(context, "PORTAL_AUTH_INVALID_SIGNATURE",
                "Portal token signature could not be verified.");
            return;
        }
        catch (SecurityTokenInvalidSignatureException)
        {
            await WriteUnauthorizedAsync(context, "PORTAL_AUTH_INVALID_SIGNATURE",
                "Portal token signature could not be verified.");
            return;
        }
        catch (Exception)
        {
            // Catch-all (algorithm rejected, malformed claims, lifetime
            // missing, etc.). NEVER echo the inner exception — the message
            // can leak signing-key length, claim positions, or which
            // validation step failed.
            await WriteUnauthorizedAsync(context, "PORTAL_AUTH_INVALID_TOKEN",
                "Portal token is invalid.");
            return;
        }

        if (validatedToken is not JwtSecurityToken jwt)
        {
            await WriteUnauthorizedAsync(context, "PORTAL_AUTH_INVALID_TOKEN",
                "Portal token is invalid.");
            return;
        }

        // Lifetime cap: reject tokens whose declared `exp - nbf` window
        // exceeds the configured ceiling, even if currently still valid.
        // Keeps a leaked token's blast radius bounded regardless of what
        // the host minted. `nbf` must be present so the cap is measurable;
        // a missing `nbf` would otherwise compute a multi-thousand-year
        // lifetime against `DateTime.MinValue` (which is the safe direction
        // — but we want the rejection to be intentional, not accidental).
        // We do NOT require `iat`: JwtSecurityTokenHandler does not emit it
        // by default, and many host JWT libs follow the same convention.
        if (jwt.ValidFrom == DateTime.MinValue)
        {
            await WriteUnauthorizedAsync(context, "PORTAL_AUTH_INVALID_TOKEN",
                "Portal token is invalid.");
            return;
        }

        var lifetime = jwt.ValidTo - jwt.ValidFrom;
        if (lifetime > TimeSpan.FromMinutes(options.MaxLifetimeMinutes))
        {
            await WriteUnauthorizedAsync(context, "PORTAL_AUTH_LIFETIME_TOO_LONG",
                $"Portal token lifetime exceeds the maximum of {options.MaxLifetimeMinutes} minutes.");
            return;
        }

        var capabilities = ExtractCapabilities(principal);

        context.Items[AppIdItemKey] = appId;
        context.Items[CapabilitiesItemKey] = capabilities;
        context.Items[AppLookupItemKey] = lookup;

        await _next(context);
    }

    private static HashSet<PortalCapability> ExtractCapabilities(ClaimsPrincipal principal)
    {
        var result = new HashSet<PortalCapability>();
        foreach (var claim in principal.FindAll(CapabilitiesClaim))
        {
            var parsed = PortalCapabilityExtensions.TryFromWire(claim.Value);
            if (parsed.HasValue)
            {
                result.Add(parsed.Value);
            }
        }
        return result;
    }

    private static async Task WriteUnauthorizedAsync(HttpContext context, string code, string message)
    {
        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        context.Response.ContentType = "application/json";
        var requestId = context.Items["RequestId"]?.ToString() ?? "unknown";
        await context.Response.WriteAsJsonAsync(new
        {
            error = new { code, message },
            meta = new { requestId = $"req_{requestId}" }
        });
    }
}

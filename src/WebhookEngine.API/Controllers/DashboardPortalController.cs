using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WebhookEngine.API.Audit;
using WebhookEngine.Core.Interfaces;
using WebhookEngine.API.Contracts;
using WebhookEngine.Core.Utilities;
using WebhookEngine.Infrastructure.Repositories;
using WebhookEngine.Infrastructure.Services;

namespace WebhookEngine.API.Controllers;

/// <summary>
/// Dashboard surface for managing the embeddable customer portal: enable /
/// rotate / disable the per-app HMAC signing key and maintain the allow-listed
/// CORS origins. The signing key is returned in plain text exactly once on
/// enable / rotate and is never echoed by the read endpoint or the audit log.
/// All mutations invalidate the in-process <see cref="PortalLookupCache"/> so
/// the portal middleware sees the new state within milliseconds rather than
/// after the configured TTL.
/// </summary>
[ApiController]
[Produces("application/json")]
[ProducesResponseType<ApiErrorResponse>(StatusCodes.Status401Unauthorized)]
[ProducesResponseType<ApiErrorResponse>(StatusCodes.Status500InternalServerError)]
[Route("api/v1/dashboard/applications/{appId:guid}/portal")]
[Authorize(AuthenticationSchemes = CookieAuthenticationDefaults.AuthenticationScheme)]
public class DashboardPortalController : ControllerBase
{
    private const string SigningKeyPrefix = "whsec_";
    private const string SetupInstructions =
        "Store this signing key in your host SaaS backend's environment as PORTAL_SIGNING_KEY. " +
        "Use it to mint short-lived HS256 JWTs (claim: appId + capabilities) for the embeddable portal. " +
        "The key is shown ONCE — re-run rotate to mint a new one if you lose it.";

    private readonly ApplicationRepository _applicationRepository;
    private readonly IAuditLogger _auditLogger;
    private readonly ILogger<DashboardPortalController> _logger;

    public DashboardPortalController(
        ApplicationRepository applicationRepository,
        IAuditLogger auditLogger,
        ILogger<DashboardPortalController> logger)
    {
        _applicationRepository = applicationRepository;
        _auditLogger = auditLogger;
        _logger = logger;
    }

    [HttpGet]
    public async Task<IActionResult> Get(Guid appId, CancellationToken ct)
    {
        var application = await _applicationRepository.GetByIdAsync(appId, ct);
        if (application is null)
        {
            return NotFound(ApiEnvelope.Error(HttpContext, "NOT_FOUND", "Application not found."));
        }

        return Ok(ApiEnvelope.Success(HttpContext, BuildReadResponse(application)));
    }

    [HttpPost("enable")]
    public async Task<IActionResult> Enable(Guid appId, CancellationToken ct)
    {
        var application = await _applicationRepository.GetByIdAsync(appId, ct);
        if (application is null)
        {
            return NotFound(ApiEnvelope.Error(HttpContext, "NOT_FOUND", "Application not found."));
        }

        var beforeSnapshot = BuildAuditSnapshot(application);

        var newSigningKey = GenerateSigningKey();
        application.PortalSigningKey = newSigningKey;
        application.PortalRotatedAt = DateTime.UtcNow;

        await _applicationRepository.UpdateAsync(application, ct);
        PortalLookupCache.InvalidateApplication(appId);

        var afterSnapshot = BuildAuditSnapshot(application);
        await _auditLogger.LogActionAsync(
            HttpContext,
            action: "application.portal.enabled",
            resourceType: "application",
            resourceId: appId,
            appId: appId,
            before: beforeSnapshot,
            after: afterSnapshot,
            ct: ct);

        // Intentionally narrow log line: appId only, never the key.
        _logger.LogInformation("Portal enabled for application {AppId}.", LogSanitizer.ForLog(appId.ToString()));

        return Ok(ApiEnvelope.Success(HttpContext, new
        {
            signingKey = newSigningKey,
            rotatedAt = application.PortalRotatedAt,
            instructions = SetupInstructions
        }));
    }

    [HttpPost("rotate")]
    public async Task<IActionResult> Rotate(Guid appId, CancellationToken ct)
    {
        var application = await _applicationRepository.GetByIdAsync(appId, ct);
        if (application is null)
        {
            return NotFound(ApiEnvelope.Error(HttpContext, "NOT_FOUND", "Application not found."));
        }

        if (string.IsNullOrEmpty(application.PortalSigningKey))
        {
            return Conflict(ApiEnvelope.Error(HttpContext, "PORTAL_NOT_ENABLED",
                "Portal is not enabled for this application. Call /portal/enable first."));
        }

        var beforeSnapshot = BuildAuditSnapshot(application);

        var newSigningKey = GenerateSigningKey();
        application.PortalSigningKey = newSigningKey;
        application.PortalRotatedAt = DateTime.UtcNow;

        await _applicationRepository.UpdateAsync(application, ct);
        PortalLookupCache.InvalidateApplication(appId);

        var afterSnapshot = BuildAuditSnapshot(application);
        await _auditLogger.LogActionAsync(
            HttpContext,
            action: "application.portal.rotated",
            resourceType: "application",
            resourceId: appId,
            appId: appId,
            before: beforeSnapshot,
            after: afterSnapshot,
            ct: ct);

        _logger.LogInformation("Portal signing key rotated for application {AppId}.", LogSanitizer.ForLog(appId.ToString()));

        return Ok(ApiEnvelope.Success(HttpContext, new
        {
            signingKey = newSigningKey,
            rotatedAt = application.PortalRotatedAt,
            instructions = SetupInstructions
        }));
    }

    [HttpPost("disable")]
    public async Task<IActionResult> Disable(Guid appId, CancellationToken ct)
    {
        var application = await _applicationRepository.GetByIdAsync(appId, ct);
        if (application is null)
        {
            return NotFound(ApiEnvelope.Error(HttpContext, "NOT_FOUND", "Application not found."));
        }

        var beforeSnapshot = BuildAuditSnapshot(application);

        application.PortalSigningKey = null;
        application.AllowedPortalOriginsJson = null;
        application.PortalRotatedAt = null;

        await _applicationRepository.UpdateAsync(application, ct);
        PortalLookupCache.InvalidateApplication(appId);

        var afterSnapshot = BuildAuditSnapshot(application);
        await _auditLogger.LogActionAsync(
            HttpContext,
            action: "application.portal.disabled",
            resourceType: "application",
            resourceId: appId,
            appId: appId,
            before: beforeSnapshot,
            after: afterSnapshot,
            ct: ct);

        _logger.LogInformation("Portal disabled for application {AppId}.", LogSanitizer.ForLog(appId.ToString()));

        return NoContent();
    }

    [HttpPut("origins")]
    public async Task<IActionResult> UpdateOrigins(Guid appId, [FromBody] DashboardPortalOriginsRequest request, CancellationToken ct)
    {
        var application = await _applicationRepository.GetByIdAsync(appId, ct);
        if (application is null)
        {
            return NotFound(ApiEnvelope.Error(HttpContext, "NOT_FOUND", "Application not found."));
        }

        if (string.IsNullOrEmpty(application.PortalSigningKey))
        {
            return Conflict(ApiEnvelope.Error(HttpContext, "PORTAL_NOT_ENABLED",
                "Portal is not enabled for this application. Call /portal/enable before configuring origins."));
        }

        var beforeSnapshot = BuildAuditSnapshot(application);

        // Canonicalize: lowercase scheme + host before persistence so portal CORS
        // matching at request time is predictable. RFC 6454 §4 — scheme + host
        // are case-insensitive; we normalize on write to avoid every reader
        // having to compare case-insensitively.
        var canonical = (request.Origins ?? new List<string>())
            .Select(CanonicalizeOrigin)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        application.AllowedPortalOriginsJson = canonical.Count == 0
            ? "[]"
            : JsonSerializer.Serialize(canonical);

        await _applicationRepository.UpdateAsync(application, ct);
        PortalLookupCache.InvalidateApplication(appId);

        var afterSnapshot = BuildAuditSnapshot(application);
        await _auditLogger.LogActionAsync(
            HttpContext,
            action: "application.portal.origins.updated",
            resourceType: "application",
            resourceId: appId,
            appId: appId,
            before: beforeSnapshot,
            after: afterSnapshot,
            ct: ct);

        return Ok(ApiEnvelope.Success(HttpContext, new
        {
            allowedOrigins = canonical
        }));
    }

    private static string GenerateSigningKey()
    {
        // 32 bytes of cryptographic randomness → 44 base64 chars + "whsec_"
        // prefix = 50 chars, well within the varchar(64) PortalSigningKey
        // column. The PortalTokenAuthMiddleware reads this literal string as
        // the HMAC key — do not hash it before persistence.
        var random = RandomNumberGenerator.GetBytes(32);
        return SigningKeyPrefix + Convert.ToBase64String(random);
    }

    private static string CanonicalizeOrigin(string origin)
    {
        // Validator already rejects malformed input, so a parse failure here
        // would be a programming bug. Defensive fallback returns the raw value
        // lowercased so a future bug never silently writes mixed-case data.
        if (!Uri.TryCreate(origin, UriKind.Absolute, out var uri))
        {
            return origin.ToLowerInvariant();
        }

        var scheme = uri.Scheme.ToLowerInvariant();
        var host = uri.Host.ToLowerInvariant();
        var port = uri.IsDefaultPort ? string.Empty : $":{uri.Port}";
        return $"{scheme}://{host}{port}";
    }

    private static object BuildReadResponse(Core.Entities.Application application)
    {
        return new
        {
            portalEnabled = !string.IsNullOrEmpty(application.PortalSigningKey),
            allowedOrigins = ParseOrigins(application.AllowedPortalOriginsJson),
            rotatedAt = application.PortalRotatedAt,
            // Always null on read — the signing key is shown once at enable / rotate
            // and never returned by GET. Callers must not attempt to use this field.
            signingKey = (string?)null
        };
    }

    private static object BuildAuditSnapshot(Core.Entities.Application application)
    {
        // CRITICAL: PortalSigningKey MUST NOT appear here. Audit rows persist
        // the boolean fact + origin list + rotated-at timestamp only — every
        // controller action writes the same shape so tampering is grep-able.
        return new
        {
            portalEnabled = !string.IsNullOrEmpty(application.PortalSigningKey),
            allowedOrigins = ParseOrigins(application.AllowedPortalOriginsJson),
            rotatedAt = application.PortalRotatedAt
        };
    }

    private static string[] ParseOrigins(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return [];
        }

        try
        {
            var parsed = JsonSerializer.Deserialize<string[]>(json);
            return parsed ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }
}

/// <summary>
/// Request body for <c>PUT /api/v1/dashboard/applications/{appId}/portal/origins</c>.
/// Empty list is valid — clears the allowlist.
/// </summary>
public class DashboardPortalOriginsRequest
{
    public List<string> Origins { get; set; } = new();
}

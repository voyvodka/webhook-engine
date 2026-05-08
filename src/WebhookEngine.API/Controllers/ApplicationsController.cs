using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using WebhookEngine.API.Contracts;
using WebhookEngine.API.Middleware;
using WebhookEngine.Infrastructure.Repositories;
using AppEntity = WebhookEngine.Core.Entities.Application;

namespace WebhookEngine.API.Controllers;

/// <summary>
/// Manages webhook applications. Each application has its own API key, signing secret, and endpoints.
/// NOTE: These endpoints are for dashboard (admin) use. API key auth is not required — dashboard cookie auth is used instead.
/// </summary>
[ApiController]
[Produces("application/json")]
[ProducesResponseType<ApiErrorResponse>(StatusCodes.Status401Unauthorized)]
[ProducesResponseType<ApiErrorResponse>(StatusCodes.Status500InternalServerError)]
[Route("api/v1/applications")]
[Authorize(AuthenticationSchemes = CookieAuthenticationDefaults.AuthenticationScheme)]
public class ApplicationsController : ControllerBase
{
    private readonly ApplicationRepository _appRepo;
    private readonly IMemoryCache _cache;

    public ApplicationsController(ApplicationRepository appRepo, IMemoryCache cache)
    {
        _appRepo = appRepo;
        _cache = cache;
    }

    private void InvalidateAuthCache(string apiKeyPrefix)
    {
        _cache.Remove(ApiKeyAuthMiddleware.CacheKeyFor(apiKeyPrefix));
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateApplicationRequest request, CancellationToken ct)
    {
        // Generate API key: whe_{appIdShort}_{random32}
        var appId = Guid.NewGuid();
        var appIdShort = appId.ToString("N")[..8];
        var randomPart = Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();
        var apiKey = $"whe_{appIdShort}_{randomPart}";
        var apiKeyPrefix = $"whe_{appIdShort}_";
        var apiKeyHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(apiKey))).ToLowerInvariant();

        // Generate signing secret (Base64-encoded HMAC key)
        var signingSecret = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));

        var application = new AppEntity
        {
            Id = appId,
            Name = request.Name,
            ApiKeyPrefix = apiKeyPrefix,
            ApiKeyHash = apiKeyHash,
            SigningSecret = signingSecret
        };

        await _appRepo.CreateAsync(application, ct);

        // Return the API key in plain text ONLY on creation — it is never retrievable again
        return Created($"/api/v1/applications/{application.Id}", ApiEnvelope.Success(HttpContext, new
        {
            id = application.Id,
            name = application.Name,
            apiKey,  // Only shown once
            signingSecret,  // Only shown once
            isActive = application.IsActive,
            createdAt = application.CreatedAt
        }));
    }

    [HttpGet]
    public async Task<IActionResult> List([FromQuery] int page = 1, [FromQuery] int pageSize = 20, CancellationToken ct = default)
    {
        var applications = await _appRepo.ListAsync(page, pageSize, ct);
        var totalCount = await _appRepo.CountAsync(ct);
        var pagination = ApiEnvelope.Pagination(page, pageSize, totalCount);

        return Ok(ApiEnvelope.Success(HttpContext,
            applications.Select(a => new
            {
                id = a.Id,
                name = a.Name,
                apiKeyPrefix = a.ApiKeyPrefix,
                isActive = a.IsActive,
                createdAt = a.CreatedAt,
                updatedAt = a.UpdatedAt
            }),
            pagination));
    }

    [HttpGet("{applicationId:guid}")]
    public async Task<IActionResult> Get(Guid applicationId, CancellationToken ct)
    {
        var application = await _appRepo.GetByIdAsync(applicationId, ct);
        if (application is null)
            return NotFound(ApiEnvelope.Error(HttpContext, "NOT_FOUND", "Application not found."));

        return Ok(ApiEnvelope.Success(HttpContext, new
        {
            id = application.Id,
            name = application.Name,
            apiKeyPrefix = application.ApiKeyPrefix,
            isActive = application.IsActive,
            idempotencyWindowMinutes = application.IdempotencyWindowMinutes,
            retentionDeliveredDays = application.RetentionDeliveredDays,
            retentionDeadLetterDays = application.RetentionDeadLetterDays,
            rateLimitPerSecond = application.RateLimitPerSecond,
            createdAt = application.CreatedAt,
            updatedAt = application.UpdatedAt
        }));
    }

    [HttpPut("{applicationId:guid}")]
    public async Task<IActionResult> Update(Guid applicationId, [FromBody] UpdateApplicationRequest request, CancellationToken ct)
    {
        var application = await _appRepo.GetByIdAsync(applicationId, ct);
        if (application is null)
            return NotFound(ApiEnvelope.Error(HttpContext, "NOT_FOUND", "Application not found."));

        application.Name = request.Name ?? application.Name;
        application.IsActive = request.IsActive ?? application.IsActive;
        application.IdempotencyWindowMinutes = request.IdempotencyWindowMinutes ?? application.IdempotencyWindowMinutes;

        // 0 = clear the override (fall back to global). > 0 = override. null = leave unchanged.
        if (request.RetentionDeliveredDays is int delivered)
        {
            application.RetentionDeliveredDays = delivered == 0 ? null : delivered;
        }
        if (request.RetentionDeadLetterDays is int deadLetter)
        {
            application.RetentionDeadLetterDays = deadLetter == 0 ? null : deadLetter;
        }
        if (request.RateLimitPerSecond is int rateLimit)
        {
            application.RateLimitPerSecond = rateLimit == 0 ? null : rateLimit;
        }

        await _appRepo.UpdateAsync(application, ct);
        InvalidateAuthCache(application.ApiKeyPrefix);

        return Ok(ApiEnvelope.Success(HttpContext, new
        {
            id = application.Id,
            name = application.Name,
            apiKeyPrefix = application.ApiKeyPrefix,
            isActive = application.IsActive,
            idempotencyWindowMinutes = application.IdempotencyWindowMinutes,
            retentionDeliveredDays = application.RetentionDeliveredDays,
            retentionDeadLetterDays = application.RetentionDeadLetterDays,
            rateLimitPerSecond = application.RateLimitPerSecond,
            createdAt = application.CreatedAt,
            updatedAt = application.UpdatedAt
        }));
    }

    [HttpDelete("{applicationId:guid}")]
    public async Task<IActionResult> Delete(Guid applicationId, CancellationToken ct)
    {
        var application = await _appRepo.GetByIdAsync(applicationId, ct);
        if (application is null)
            return NotFound(ApiEnvelope.Error(HttpContext, "NOT_FOUND", "Application not found."));

        await _appRepo.DeleteAsync(applicationId, ct);
        InvalidateAuthCache(application.ApiKeyPrefix);
        return NoContent();
    }

    [HttpPost("{applicationId:guid}/rotate-key")]
    public async Task<IActionResult> RotateApiKey(Guid applicationId, CancellationToken ct)
    {
        var application = await _appRepo.GetByIdAsync(applicationId, ct);
        if (application is null)
            return NotFound(ApiEnvelope.Error(HttpContext, "NOT_FOUND", "Application not found."));

        // Generate new API key with same prefix format
        var appIdShort = application.Id.ToString("N")[..8];
        var randomPart = Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();
        var newApiKey = $"whe_{appIdShort}_{randomPart}";
        var newApiKeyHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(newApiKey))).ToLowerInvariant();

        application.ApiKeyHash = newApiKeyHash;
        await _appRepo.UpdateAsync(application, ct);
        InvalidateAuthCache(application.ApiKeyPrefix);

        return Ok(ApiEnvelope.Success(HttpContext, new
        {
            id = application.Id,
            name = application.Name,
            apiKey = newApiKey  // Only shown once
        }));
    }

    [HttpPost("{applicationId:guid}/rotate-secret")]
    public async Task<IActionResult> RotateSigningSecret(Guid applicationId, CancellationToken ct)
    {
        var application = await _appRepo.GetByIdAsync(applicationId, ct);
        if (application is null)
            return NotFound(ApiEnvelope.Error(HttpContext, "NOT_FOUND", "Application not found."));

        var newSigningSecret = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        application.SigningSecret = newSigningSecret;
        await _appRepo.UpdateAsync(application, ct);
        InvalidateAuthCache(application.ApiKeyPrefix);

        return Ok(ApiEnvelope.Success(HttpContext, new
        {
            id = application.Id,
            name = application.Name,
            signingSecret = newSigningSecret  // Only shown once
        }));
    }
}

public class CreateApplicationRequest
{
    public string Name { get; set; } = string.Empty;
}

public class UpdateApplicationRequest
{
    public string? Name { get; set; }
    public bool? IsActive { get; set; }
    public int? IdempotencyWindowMinutes { get; set; }

    // Send 0 to clear an override and fall back to the global RetentionOptions.
    // Send a positive integer to override per-app. Send null to leave unchanged.
    public int? RetentionDeliveredDays { get; set; }
    public int? RetentionDeadLetterDays { get; set; }

    // Send 0 to clear the per-app rate limit (unlimited at the app gate).
    // Send a positive integer to set messages-per-second cap. null = unchanged.
    public int? RateLimitPerSecond { get; set; }
}

using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WebhookEngine.API.Contracts;
using WebhookEngine.Infrastructure.Repositories;
using AppEntity = WebhookEngine.Core.Entities.Application;

namespace WebhookEngine.API.Controllers;

/// <summary>
/// Manages webhook applications. Each application has its own API key, signing secret, and endpoints.
/// NOTE: These endpoints are for dashboard (admin) use. API key auth is not required — dashboard cookie auth is used instead.
/// </summary>
[ApiController]
[Route("api/v1/applications")]
[Authorize(AuthenticationSchemes = CookieAuthenticationDefaults.AuthenticationScheme)]
public class ApplicationsController : ControllerBase
{
    private readonly ApplicationRepository _appRepo;

    public ApplicationsController(ApplicationRepository appRepo)
    {
        _appRepo = appRepo;
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

        await _appRepo.UpdateAsync(application, ct);

        return Ok(ApiEnvelope.Success(HttpContext, new
        {
            id = application.Id,
            name = application.Name,
            apiKeyPrefix = application.ApiKeyPrefix,
            isActive = application.IsActive,
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
}

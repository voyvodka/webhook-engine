using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WebhookEngine.API.Contracts;
using WebhookEngine.API.Services;

namespace WebhookEngine.API.Controllers;

/// <summary>
/// Dev traffic endpoints — start/stop/status/seed-once for development traffic generation.
/// Authenticated via dashboard session cookie (not API key).
/// </summary>
[ApiController]
[Produces("application/json")]
[ProducesResponseType<ApiErrorResponse>(StatusCodes.Status401Unauthorized)]
[ProducesResponseType<ApiErrorResponse>(StatusCodes.Status500InternalServerError)]
[Route("api/v1/dashboard")]
[Authorize(AuthenticationSchemes = CookieAuthenticationDefaults.AuthenticationScheme)]
public class DevTrafficController : ControllerBase
{
    private readonly IDevTrafficGenerator _devTrafficGenerator;

    public DevTrafficController(IDevTrafficGenerator devTrafficGenerator)
    {
        _devTrafficGenerator = devTrafficGenerator;
    }

    [HttpGet("dev/traffic/status")]
    public IActionResult GetDevTrafficStatus()
    {
        if (!_devTrafficGenerator.IsEnabled)
            return NotFound(ApiEnvelope.Error(HttpContext, "NOT_FOUND", "Dev traffic tools are only available in Development or DEBUG builds."));

        return Ok(ApiEnvelope.Success(HttpContext, _devTrafficGenerator.GetStatus()));
    }

    [HttpPost("dev/traffic/start")]
    public async Task<IActionResult> StartDevTraffic([FromBody] DevTrafficStartRequest request, CancellationToken ct)
    {
        if (!_devTrafficGenerator.IsEnabled)
            return NotFound(ApiEnvelope.Error(HttpContext, "NOT_FOUND", "Dev traffic tools are only available in Development or DEBUG builds."));

        var status = await _devTrafficGenerator.StartAsync(request, ct);
        return Ok(ApiEnvelope.Success(HttpContext, status));
    }

    [HttpPost("dev/traffic/seed-once")]
    public async Task<IActionResult> SeedDevTraffic([FromBody] DevTrafficSeedRequest request, CancellationToken ct)
    {
        if (!_devTrafficGenerator.IsEnabled)
            return NotFound(ApiEnvelope.Error(HttpContext, "NOT_FOUND", "Dev traffic tools are only available in Development or DEBUG builds."));

        var result = await _devTrafficGenerator.SeedOnceAsync(request, ct);
        return Ok(ApiEnvelope.Success(HttpContext, result));
    }

    [HttpPost("dev/traffic/stop")]
    public async Task<IActionResult> StopDevTraffic(CancellationToken ct)
    {
        if (!_devTrafficGenerator.IsEnabled)
            return NotFound(ApiEnvelope.Error(HttpContext, "NOT_FOUND", "Dev traffic tools are only available in Development or DEBUG builds."));

        var status = await _devTrafficGenerator.StopAsync(ct);
        return Ok(ApiEnvelope.Success(HttpContext, status));
    }
}

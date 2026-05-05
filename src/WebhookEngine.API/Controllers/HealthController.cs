using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WebhookEngine.API.Services;
using WebhookEngine.Infrastructure.Data;

namespace WebhookEngine.API.Controllers;

/// <summary>
/// Three probes:
///   /health        — alias of /health/live, kept for back-compat.
///   /health/live   — liveness, just confirms the process is up.
///   /health/ready  — readiness, returns 503 until migrations + seeders
///                    finish AND the database is reachable. This is the
///                    one a Kubernetes / Compose readiness probe should
///                    actually call.
/// </summary>
[ApiController]
[Produces("application/json")]
[Route("health")]
[Route("api/v1/health")]
public class HealthController : ControllerBase
{
    [HttpGet]
    [HttpGet("live")]
    public IActionResult Live() =>
        Ok(new { status = "healthy", timestamp = DateTime.UtcNow });

    [HttpGet("ready")]
    public async Task<IActionResult> Ready(
        [FromServices] AppReadinessGate gate,
        [FromServices] WebhookDbContext db,
        CancellationToken ct)
    {
        if (!gate.IsReady)
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new
            {
                status = "starting",
                reason = "migrations or seeding not complete",
                timestamp = DateTime.UtcNow
            });
        }

        bool dbOk;
        try
        {
            dbOk = await db.Database.CanConnectAsync(ct);
        }
        catch
        {
            dbOk = false;
        }

        if (!dbOk)
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new
            {
                status = "degraded",
                reason = "database unreachable",
                timestamp = DateTime.UtcNow
            });
        }

        return Ok(new { status = "healthy", timestamp = DateTime.UtcNow });
    }
}

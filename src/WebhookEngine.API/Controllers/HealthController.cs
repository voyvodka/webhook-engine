using Microsoft.AspNetCore.Mvc;

namespace WebhookEngine.API.Controllers;

[ApiController]
[Produces("application/json")]
[Route("health")]
[Route("api/v1/health")]
public class HealthController : ControllerBase
{
    [HttpGet]
    public IActionResult Get() => Ok(new { status = "healthy", timestamp = DateTime.UtcNow });
}

using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WebhookEngine.API.Auth;
using WebhookEngine.Infrastructure.Repositories;

namespace WebhookEngine.API.Controllers;

/// <summary>
/// Dashboard cookie-based authentication. NOT API key auth.
/// </summary>
[ApiController]
[Route("api/v1/auth")]
public class AuthController : ControllerBase
{
    private readonly DashboardUserRepository _userRepo;

    public AuthController(DashboardUserRepository userRepo)
    {
        _userRepo = userRepo;
    }

    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] LoginRequest request, CancellationToken ct)
    {
        var user = await _userRepo.GetByEmailAsync(request.Email, ct);
        if (user is null)
        {
            return Unauthorized(new
            {
                error = new { code = "UNAUTHORIZED", message = "Invalid email or password." },
                meta = new { requestId = $"req_{HttpContext.Items["RequestId"]}" }
            });
        }

        if (!PasswordHasher.VerifyPassword(request.Password, user.PasswordHash))
        {
            return Unauthorized(new
            {
                error = new { code = "UNAUTHORIZED", message = "Invalid email or password." },
                meta = new { requestId = $"req_{HttpContext.Items["RequestId"]}" }
            });
        }

        // Create claims and sign in with cookie auth
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new(ClaimTypes.Email, user.Email),
            new(ClaimTypes.Role, user.Role)
        };

        var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
        var principal = new ClaimsPrincipal(identity);

        await HttpContext.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme,
            principal,
            new AuthenticationProperties
            {
                IsPersistent = true,
                ExpiresUtc = DateTimeOffset.UtcNow.AddDays(7)
            });

        await _userRepo.UpdateLastLoginAsync(user.Id, ct);

        return Ok(new
        {
            data = new
            {
                id = user.Id,
                email = user.Email,
                role = user.Role
            },
            meta = new { requestId = $"req_{HttpContext.Items["RequestId"]}" }
        });
    }

    [HttpPost("logout")]
    [Authorize(AuthenticationSchemes = CookieAuthenticationDefaults.AuthenticationScheme)]
    public async Task<IActionResult> Logout()
    {
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);

        return Ok(new
        {
            data = new { message = "Logged out successfully." },
            meta = new { requestId = $"req_{HttpContext.Items["RequestId"]}" }
        });
    }

    [HttpGet("me")]
    [Authorize(AuthenticationSchemes = CookieAuthenticationDefaults.AuthenticationScheme)]
    public async Task<IActionResult> Me(CancellationToken ct)
    {
        var userIdClaim = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userIdClaim) || !Guid.TryParse(userIdClaim, out var userId))
        {
            return Unauthorized(new
            {
                error = new { code = "UNAUTHORIZED", message = "Not authenticated." },
                meta = new { requestId = $"req_{HttpContext.Items["RequestId"]}" }
            });
        }

        var user = await _userRepo.GetByIdAsync(userId, ct);
        if (user is null)
        {
            return Unauthorized(new
            {
                error = new { code = "UNAUTHORIZED", message = "User not found." },
                meta = new { requestId = $"req_{HttpContext.Items["RequestId"]}" }
            });
        }

        return Ok(new
        {
            data = new
            {
                id = user.Id,
                email = user.Email,
                role = user.Role,
                lastLoginAt = user.LastLoginAt
            },
            meta = new { requestId = $"req_{HttpContext.Items["RequestId"]}" }
        });
    }
}

public class LoginRequest
{
    public string Email { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
}

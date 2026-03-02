using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WebhookEngine.API.Auth;
using WebhookEngine.API.Contracts;
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
            return Unauthorized(ApiEnvelope.Error(HttpContext, "UNAUTHORIZED", "Invalid email or password."));
        }

        if (!PasswordHasher.VerifyPassword(request.Password, user.PasswordHash))
        {
            return Unauthorized(ApiEnvelope.Error(HttpContext, "UNAUTHORIZED", "Invalid email or password."));
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

        return Ok(ApiEnvelope.Success(HttpContext, new
        {
            id = user.Id,
            email = user.Email,
            role = user.Role
        }));
    }

    [HttpPost("logout")]
    [Authorize(AuthenticationSchemes = CookieAuthenticationDefaults.AuthenticationScheme)]
    public async Task<IActionResult> Logout()
    {
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);

        return Ok(ApiEnvelope.Success(HttpContext, new
        {
            message = "Logged out successfully."
        }));
    }

    [HttpGet("me")]
    [Authorize(AuthenticationSchemes = CookieAuthenticationDefaults.AuthenticationScheme)]
    public async Task<IActionResult> Me(CancellationToken ct)
    {
        var userIdClaim = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userIdClaim) || !Guid.TryParse(userIdClaim, out var userId))
        {
            return Unauthorized(ApiEnvelope.Error(HttpContext, "UNAUTHORIZED", "Not authenticated."));
        }

        var user = await _userRepo.GetByIdAsync(userId, ct);
        if (user is null)
        {
            return Unauthorized(ApiEnvelope.Error(HttpContext, "UNAUTHORIZED", "User not found."));
        }

        return Ok(ApiEnvelope.Success(HttpContext, new
        {
            id = user.Id,
            email = user.Email,
            role = user.Role,
            lastLoginAt = user.LastLoginAt
        }));
    }
}

public class LoginRequest
{
    public string Email { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
}

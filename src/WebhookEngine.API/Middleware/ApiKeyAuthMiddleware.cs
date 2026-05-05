using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Caching.Memory;
using WebhookEngine.Core.Entities;
using WebhookEngine.Infrastructure.Repositories;

namespace WebhookEngine.API.Middleware;

public class ApiKeyAuthMiddleware
{
    public static readonly TimeSpan CacheTtl = TimeSpan.FromSeconds(30);
    private const string CacheKeyPrefix = "apikey:prefix:";

    public static string CacheKeyFor(string apiKeyPrefix) => CacheKeyPrefix + apiKeyPrefix;

    private readonly RequestDelegate _next;

    public ApiKeyAuthMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        // Only apply to /api/v1/* routes (except auth endpoints)
        var path = context.Request.Path.Value ?? "";
        if (!path.StartsWith("/api/v1/")
            || path.StartsWith("/api/v1/auth")
            || path.StartsWith("/api/v1/dashboard")
            || path.StartsWith("/api/v1/applications")
            || path.StartsWith("/api/v1/health")
            || path.StartsWith("/metrics"))
        {
            await _next(context);
            return;
        }

        var authHeader = context.Request.Headers.Authorization.FirstOrDefault();
        if (string.IsNullOrEmpty(authHeader) || !authHeader.StartsWith("Bearer "))
        {
            context.Response.StatusCode = 401;
            await context.Response.WriteAsJsonAsync(new
            {
                error = new { code = "UNAUTHORIZED", message = "Missing or invalid API key." },
                meta = new { requestId = $"req_{context.Items["RequestId"]}" }
            });
            return;
        }

        var apiKey = authHeader["Bearer ".Length..];

        // Extract prefix for fast lookup (format: whe_{appIdShort}_{random32})
        var parts = apiKey.Split('_');
        if (parts.Length < 3)
        {
            context.Response.StatusCode = 401;
            await context.Response.WriteAsJsonAsync(new
            {
                error = new { code = "UNAUTHORIZED", message = "Invalid API key format." },
                meta = new { requestId = $"req_{context.Items["RequestId"]}" }
            });
            return;
        }

        var prefix = $"{parts[0]}_{parts[1]}_";

        var cache = context.RequestServices.GetRequiredService<IMemoryCache>();
        if (!cache.TryGetValue(CacheKeyFor(prefix), out Application? app))
        {
            var appRepo = context.RequestServices.GetRequiredService<ApplicationRepository>();
            app = await appRepo.GetByApiKeyPrefixAsync(prefix);
            if (app is not null)
            {
                cache.Set(CacheKeyFor(prefix), app, CacheTtl);
            }
        }

        if (app is null)
        {
            context.Response.StatusCode = 401;
            await context.Response.WriteAsJsonAsync(new
            {
                error = new { code = "UNAUTHORIZED", message = "Invalid API key." },
                meta = new { requestId = $"req_{context.Items["RequestId"]}" }
            });
            return;
        }

        // Verify hash
        var keyHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(apiKey))).ToLowerInvariant();
        if (keyHash != app.ApiKeyHash)
        {
            context.Response.StatusCode = 401;
            await context.Response.WriteAsJsonAsync(new
            {
                error = new { code = "UNAUTHORIZED", message = "Invalid API key." },
                meta = new { requestId = $"req_{context.Items["RequestId"]}" }
            });
            return;
        }

        if (!app.IsActive)
        {
            context.Response.StatusCode = 401;
            await context.Response.WriteAsJsonAsync(new
            {
                error = new { code = "UNAUTHORIZED", message = "Application is inactive." },
                meta = new { requestId = $"req_{context.Items["RequestId"]}" }
            });
            return;
        }

        // Store authenticated app in context
        context.Items["AppId"] = app.Id;
        context.Items["Application"] = app;

        await _next(context);
    }
}

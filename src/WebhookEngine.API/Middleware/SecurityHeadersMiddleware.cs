namespace WebhookEngine.API.Middleware;

/// <summary>
/// Adds the standard security response headers. Single owner means no
/// disagreements between hosts about what's set; values are tuned for the
/// SPA-and-API-on-the-same-host topology this project ships.
/// </summary>
public sealed class SecurityHeadersMiddleware
{
    private readonly RequestDelegate _next;
    private readonly bool _isDevelopment;

    public SecurityHeadersMiddleware(RequestDelegate next, IHostEnvironment env)
    {
        _next = next;
        _isDevelopment = env.IsDevelopment();
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var headers = context.Response.Headers;

        // OnStarting fires right before the response head is flushed, which
        // is the only place we can be sure no later code path adds a
        // duplicate header.
        context.Response.OnStarting(() =>
        {
            if (!headers.ContainsKey("X-Content-Type-Options"))
                headers["X-Content-Type-Options"] = "nosniff";

            if (!headers.ContainsKey("X-Frame-Options"))
                headers["X-Frame-Options"] = "DENY";

            if (!headers.ContainsKey("Referrer-Policy"))
                headers["Referrer-Policy"] = "no-referrer";

            if (!headers.ContainsKey("Permissions-Policy"))
                headers["Permissions-Policy"] = "camera=(), microphone=(), geolocation=()";

            // Strict CSP for the SPA: scripts and styles only from same
            // origin, no inline scripts. Vite injects a tiny inline script
            // for module preload — Tailwind 4 runtime + the Scalar dev tool
            // also need 'unsafe-inline' for styles.
            if (!headers.ContainsKey("Content-Security-Policy"))
                headers["Content-Security-Policy"] =
                    "default-src 'self'; " +
                    "script-src 'self'; " +
                    "style-src 'self' 'unsafe-inline'; " +
                    "img-src 'self' data:; " +
                    "connect-src 'self'; " +
                    "frame-ancestors 'none'; " +
                    "base-uri 'self'; " +
                    "form-action 'self'";

            // HSTS: only meaningful over HTTPS, only useful outside dev.
            if (!_isDevelopment && context.Request.IsHttps && !headers.ContainsKey("Strict-Transport-Security"))
                headers["Strict-Transport-Security"] = "max-age=31536000; includeSubDomains";

            return Task.CompletedTask;
        });

        await _next(context);
    }
}

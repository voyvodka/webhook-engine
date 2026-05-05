using Microsoft.Extensions.Configuration;

namespace WebhookEngine.API.Middleware;

/// <summary>
/// Optional bearer-token gate on the Prometheus scrape endpoint. When the
/// configuration key WebhookEngine:Metrics:ScrapeToken is set, requests to
/// <c>/metrics</c> must carry <c>Authorization: Bearer &lt;token&gt;</c>;
/// otherwise the endpoint stays public for dev / single-host setups.
/// </summary>
public sealed class MetricsAuthMiddleware
{
    private readonly RequestDelegate _next;
    private readonly string? _expectedToken;

    public MetricsAuthMiddleware(RequestDelegate next, IConfiguration configuration)
    {
        _next = next;
        _expectedToken = configuration["WebhookEngine:Metrics:ScrapeToken"];
    }

    public async Task InvokeAsync(HttpContext context)
    {
        if (!string.IsNullOrWhiteSpace(_expectedToken)
            && context.Request.Path.Equals("/metrics", StringComparison.Ordinal))
        {
            var auth = context.Request.Headers.Authorization.ToString();
            if (auth != $"Bearer {_expectedToken}")
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                return;
            }
        }

        await _next(context);
    }
}

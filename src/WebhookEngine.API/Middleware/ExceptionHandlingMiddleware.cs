using System.Net;
using System.Text.Json;
using WebhookEngine.API.Services;

namespace WebhookEngine.API.Middleware;

public class ExceptionHandlingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ExceptionHandlingMiddleware> _logger;

    public ExceptionHandlingMiddleware(RequestDelegate next, ILogger<ExceptionHandlingMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Unhandled exception on {Method} {Path}",
                LogSanitizer.ForLog(context.Request.Method),
                LogSanitizer.ForLog(context.Request.Path));
            await HandleExceptionAsync(context, ex);
        }
    }

    private static async Task HandleExceptionAsync(HttpContext context, Exception exception)
    {
        context.Response.ContentType = "application/json";
        context.Response.StatusCode = (int)HttpStatusCode.InternalServerError;

        var requestId = context.Items["RequestId"]?.ToString() ?? "unknown";

        var response = new
        {
            error = new
            {
                code = "INTERNAL_ERROR",
                message = "An unexpected error occurred."
            },
            meta = new
            {
                requestId = $"req_{requestId}"
            }
        };

        var json = JsonSerializer.Serialize(response, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
        await context.Response.WriteAsync(json);
    }
}

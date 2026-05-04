using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using FluentValidation;
using WebhookEngine.API.Validators;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using OpenTelemetry.Metrics;
using Serilog;
using WebhookEngine.API.Hubs;
using WebhookEngine.API.Middleware;
using WebhookEngine.API.Services;
using WebhookEngine.API.Startup;
using WebhookEngine.Core.Interfaces;
using WebhookEngine.Core.Metrics;
using WebhookEngine.Core.StateMachine;
using WebhookEngine.Core.Options;
using WebhookEngine.Infrastructure.Data;
using WebhookEngine.Infrastructure.Queue;
using WebhookEngine.Infrastructure.Repositories;
using WebhookEngine.Infrastructure.Services;
using WebhookEngine.Worker;

var builder = WebApplication.CreateBuilder(args);

// Serilog
builder.Host.UseSerilog((context, configuration) =>
    configuration.ReadFrom.Configuration(context.Configuration));

// Options
builder.Services.Configure<DeliveryOptions>(builder.Configuration.GetSection(DeliveryOptions.SectionName));
builder.Services.Configure<RetryPolicyOptions>(builder.Configuration.GetSection(RetryPolicyOptions.SectionName));
builder.Services.Configure<CircuitBreakerOptions>(builder.Configuration.GetSection(CircuitBreakerOptions.SectionName));
builder.Services.Configure<DashboardAuthOptions>(builder.Configuration.GetSection(DashboardAuthOptions.SectionName));
builder.Services.Configure<RetentionOptions>(builder.Configuration.GetSection(RetentionOptions.SectionName));
builder.Services.Configure<RateLimitOptions>(builder.Configuration.GetSection(RateLimitOptions.SectionName));
builder.Services.Configure<TransformationOptions>(builder.Configuration.GetSection(TransformationOptions.SectionName));

// Database
builder.Services.AddDbContext<WebhookDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("Default")));

// HTTP Client — never use new HttpClient() directly
var deliveryTimeoutSeconds = builder.Configuration.GetValue<int>("WebhookEngine:Delivery:TimeoutSeconds", 30);
builder.Services.AddHttpClient("webhook-delivery", client =>
{
    client.DefaultRequestHeaders.Add("User-Agent", "WebhookEngine/1.0");
    client.Timeout = TimeSpan.FromSeconds(deliveryTimeoutSeconds);
}).ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
{
    AllowAutoRedirect = false
});

// Repositories
builder.Services.AddScoped<ApplicationRepository>();
builder.Services.AddScoped<EndpointRepository>();
builder.Services.AddScoped<MessageRepository>();
builder.Services.AddScoped<EventTypeRepository>();
builder.Services.AddScoped<DashboardUserRepository>();
builder.Services.AddScoped<DashboardStatsRepository>();

// Services
builder.Services.AddScoped<IMessageQueue, PostgresMessageQueue>();
builder.Services.AddScoped<IDeliveryService, HttpDeliveryService>();
builder.Services.AddSingleton<ISigningService, HmacSigningService>();
builder.Services.AddScoped<IEndpointHealthTracker, EndpointHealthTracker>();
builder.Services.AddSingleton<IEndpointRateLimiter, EndpointRateLimiter>();
builder.Services.AddSingleton<IMessageStateMachine, MessageStateMachine>();
builder.Services.AddSingleton<IDeliveryNotifier, SignalRDeliveryNotifier>();
builder.Services.AddSingleton<IDevTrafficGenerator, DevTrafficGenerator>();
builder.Services.AddSingleton<IPayloadTransformer, JmesPathPayloadTransformer>();

// Background Workers
builder.Services.AddHostedService<DeliveryWorker>();
builder.Services.AddHostedService<RetryScheduler>();
builder.Services.AddHostedService<CircuitBreakerWorker>();
builder.Services.AddHostedService<StaleLockRecoveryWorker>();
builder.Services.AddHostedService<RetentionCleanupWorker>();

builder.Services
    .AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.Cookie.Name = "webhookengine_dashboard";
        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = SameSiteMode.Lax;
        options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
        options.SlidingExpiration = true;
        options.ExpireTimeSpan = TimeSpan.FromDays(7);

        options.Events.OnRedirectToLogin = context =>
        {
            if (context.Request.Path.StartsWithSegments("/api"))
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                return Task.CompletedTask;
            }

            context.Response.Redirect(context.RedirectUri);
            return Task.CompletedTask;
        };

        options.Events.OnRedirectToAccessDenied = context =>
        {
            if (context.Request.Path.StartsWithSegments("/api"))
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                return Task.CompletedTask;
            }

            context.Response.Redirect(context.RedirectUri);
            return Task.CompletedTask;
        };
    });

builder.Services.AddAuthorization();

// Controllers
builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
    });
builder.Services.AddSingleton<WebhookEngine.API.Validators.EndpointUrlPolicy>();
builder.Services.AddValidatorsFromAssemblyContaining<Program>();
builder.Services.AddMvcCore(options => options.Filters.Add<FluentValidationFilter>());

// SignalR
builder.Services.AddSignalR();

// OpenTelemetry Metrics + Prometheus
builder.Services.AddSingleton<WebhookMetrics>();
builder.Services.AddOpenTelemetry()
    .WithMetrics(metrics => metrics
        .AddAspNetCoreInstrumentation()
        .AddRuntimeInstrumentation()
        .AddMeter(WebhookMetrics.MeterName)
        .AddPrometheusExporter());

// Rate limiting — per-AppId token bucket (D-01)
builder.Services.AddRateLimiter(options =>
{
    var rlOpts = builder.Configuration
        .GetSection(RateLimitOptions.SectionName)
        .Get<RateLimitOptions>() ?? new RateLimitOptions();

    options.AddPolicy("send-by-appid", httpContext =>
    {
        var appId = httpContext.Items["AppId"] as Guid? ?? Guid.Empty;

        return RateLimitPartition.GetTokenBucketLimiter(appId, _ =>
            new TokenBucketRateLimiterOptions
            {
                TokenLimit = rlOpts.PermitLimit,
                ReplenishmentPeriod = TimeSpan.FromSeconds(rlOpts.ReplenishmentPeriodSeconds),
                TokensPerPeriod = rlOpts.TokensPerPeriod,
                AutoReplenishment = true,
                QueueLimit = rlOpts.QueueLimit
            });
    });

    // Custom 429 response: ApiEnvelope-compatible format + Retry-After header (D-04)
    options.OnRejected = async (context, ct) =>
    {
        context.HttpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;

        if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
        {
            context.HttpContext.Response.Headers.RetryAfter =
                ((int)retryAfter.TotalSeconds).ToString();
        }

        context.HttpContext.Response.ContentType = "application/json";
        var requestId = context.HttpContext.Items["RequestId"]?.ToString() ?? "unknown";
        await context.HttpContext.Response.WriteAsJsonAsync(new
        {
            error = new { code = "RATE_LIMIT_EXCEEDED", message = "Too many requests. Please retry after the indicated time." },
            meta = new { requestId = $"req_{requestId}" }
        }, ct);
    };
});

var app = builder.Build();

// Auto-apply EF Core migrations on startup (skip in Testing environment)
if (!app.Environment.IsEnvironment("Testing"))
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<WebhookDbContext>();
    await db.Database.MigrateAsync();

    var dashboardAuthOptions = scope.ServiceProvider
        .GetRequiredService<IOptions<DashboardAuthOptions>>()
        .Value;

    await DashboardAdminSeeder.SeedAsync(db, dashboardAuthOptions, app.Logger);
}

// Middleware pipeline (order matters)
app.UseMiddleware<RequestLoggingMiddleware>();
app.UseMiddleware<ExceptionHandlingMiddleware>();
app.UseMiddleware<ApiKeyAuthMiddleware>();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

app.UseStaticFiles(); // Serve React dashboard from wwwroot

app.MapControllers();
app.MapHub<DeliveryHub>("/hubs/deliveries");
app.MapPrometheusScrapingEndpoint(); // GET /metrics
app.MapFallbackToFile("index.html");

app.Run();

// Make Program accessible for integration tests
public partial class Program { }

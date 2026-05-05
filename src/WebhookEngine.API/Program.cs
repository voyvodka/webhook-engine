using System.Net;
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
using Scalar.AspNetCore;
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
builder.Services.Configure<SsrfGuardOptions>(builder.Configuration.GetSection(SsrfGuardOptions.SectionName));
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
}).ConfigurePrimaryHttpMessageHandler(sp =>
{
    // Connect-time SSRF guard: even if validate-time DNS resolution returned
    // a public IP, DNS rebinding can swap in a private IP at connect time.
    // Re-classifying the resolved endpoint here defeats that.
    var ssrfOptions = sp.GetRequiredService<IOptions<SsrfGuardOptions>>().Value;
    var hostEnv = sp.GetRequiredService<IHostEnvironment>();
    var allowLoopback = hostEnv.IsDevelopment() && ssrfOptions.AllowLoopbackInDevelopment;

    var handler = new SocketsHttpHandler
    {
        AllowAutoRedirect = false
    };

    if (ssrfOptions.Enabled)
    {
        handler.ConnectCallback = async (ctx, ct) =>
        {
            var addresses = await Dns.GetHostAddressesAsync(ctx.DnsEndPoint.Host, ct);
            foreach (var address in addresses)
            {
                var reason = PrivateIpDetector.Classify(address, allowLoopback);
                if (reason is not null)
                {
                    throw new HttpRequestException(
                        $"Refused to connect to {ctx.DnsEndPoint.Host}: {reason}.");
                }
            }

            // Pin the connection to a vetted address — defeats DNS rebinding.
            var safeAddress = addresses.FirstOrDefault()
                ?? throw new HttpRequestException($"Cannot resolve {ctx.DnsEndPoint.Host}.");
            var socket = new System.Net.Sockets.Socket(
                System.Net.Sockets.SocketType.Stream,
                System.Net.Sockets.ProtocolType.Tcp)
            {
                NoDelay = true
            };
            try
            {
                await socket.ConnectAsync(new System.Net.IPEndPoint(safeAddress, ctx.DnsEndPoint.Port), ct);
                return new System.Net.Sockets.NetworkStream(socket, ownsSocket: true);
            }
            catch
            {
                socket.Dispose();
                throw;
            }
        };
    }

    return handler;
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

// In-memory cache used by ApiKeyAuthMiddleware (api-key→application) and
// DeliveryLookupCache (event-type / subscribed-endpoints lookups on the
// public send path). Auth cache invalidates synchronously; the delivery
// lookups rely on a 30 s TTL.
builder.Services.AddMemoryCache();
builder.Services.AddScoped<DeliveryLookupCache>();

// OpenAPI
builder.Services.AddOpenApi(options =>
{
    options.AddDocumentTransformer((document, _, _) =>
    {
        document.Info.Title = "WebhookEngine API";
        document.Info.Version = "v1";
        document.Info.Description =
            "Self-hosted webhook delivery platform — REST API. " +
            "All public endpoints (under /api/v1/*, excluding /api/v1/dashboard) " +
            "authenticate via Bearer API key.";
        return Task.CompletedTask;
    });
});

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

if (app.Environment.IsDevelopment() || app.Environment.IsStaging())
{
    app.MapOpenApi("/openapi/{documentName}.json");
    app.MapScalarApiReference("/scalar", options =>
    {
        options.WithTitle("WebhookEngine API")
               .WithOpenApiRoutePattern("/openapi/{documentName}.json")
               .WithTheme(ScalarTheme.BluePlanet)
               .EnableDarkMode()
               .WithDefaultHttpClient(ScalarTarget.CSharp, ScalarClient.HttpClient);
    });
}

app.MapFallbackToFile("index.html");

app.Run();

// Make Program accessible for integration tests
public partial class Program { }

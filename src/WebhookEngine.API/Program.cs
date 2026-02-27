using FluentValidation;
using FluentValidation.AspNetCore;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using OpenTelemetry.Metrics;
using Serilog;
using WebhookEngine.API.Hubs;
using WebhookEngine.API.Middleware;
using WebhookEngine.API.Startup;
using WebhookEngine.Core.Interfaces;
using WebhookEngine.Core.Metrics;
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

// Services
builder.Services.AddScoped<IMessageQueue, PostgresMessageQueue>();
builder.Services.AddScoped<IDeliveryService, HttpDeliveryService>();
builder.Services.AddSingleton<ISigningService, HmacSigningService>();
builder.Services.AddScoped<IEndpointHealthTracker, EndpointHealthTracker>();
builder.Services.AddSingleton<IDeliveryNotifier, SignalRDeliveryNotifier>();

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
builder.Services.AddControllers();
builder.Services.AddFluentValidationAutoValidation();
builder.Services.AddValidatorsFromAssemblyContaining<Program>();

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

using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using WebhookEngine.Core.Entities;
using WebhookEngine.Core.Enums;
using WebhookEngine.Core.Options;
using WebhookEngine.Infrastructure.Data;
using ApplicationEntity = WebhookEngine.Core.Entities.Application;

namespace WebhookEngine.Worker.Tests;

public class CircuitBreakerWorkerTests
{
    [Fact]
    public async Task ExecuteAsync_Transitions_Expired_Open_Circuit_To_HalfOpen_And_Marks_Endpoint_Degraded()
    {
        var services = CreateServiceProvider();
        await SeedEndpointAsync(services, EndpointStatus.Failed, CircuitState.Open, DateTime.UtcNow.AddMinutes(-1));

        var worker = CreateWorker(services);
        await RunWorkerOnceAsync(worker);

        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<WebhookDbContext>();

        var endpoint = await db.Endpoints.AsNoTracking().SingleAsync();
        var health = await db.EndpointHealths.AsNoTracking().SingleAsync();

        health.CircuitState.Should().Be(CircuitState.HalfOpen);
        health.CooldownUntil.Should().BeNull();
        endpoint.Status.Should().Be(EndpointStatus.Degraded);
    }

    [Fact]
    public async Task ExecuteAsync_Does_Not_Override_Disabled_Endpoint_Status()
    {
        var services = CreateServiceProvider();
        await SeedEndpointAsync(services, EndpointStatus.Disabled, CircuitState.Open, DateTime.UtcNow.AddMinutes(-1));

        var worker = CreateWorker(services);
        await RunWorkerOnceAsync(worker);

        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<WebhookDbContext>();

        var endpoint = await db.Endpoints.AsNoTracking().SingleAsync();
        var health = await db.EndpointHealths.AsNoTracking().SingleAsync();

        health.CircuitState.Should().Be(CircuitState.HalfOpen);
        endpoint.Status.Should().Be(EndpointStatus.Disabled);
    }

    private static ServiceProvider CreateServiceProvider()
    {
        var services = new ServiceCollection();
        var dbName = $"circuit_worker_tests_{Guid.NewGuid()}";

        services.AddLogging(builder => builder.SetMinimumLevel(LogLevel.Warning));
        services.AddDbContext<WebhookDbContext>(options =>
            options.UseInMemoryDatabase(dbName));
        services.Configure<CircuitBreakerOptions>(options =>
        {
            options.FailureThreshold = 5;
            options.CooldownMinutes = 5;
            options.SuccessThreshold = 1;
        });

        return services.BuildServiceProvider();
    }

    private static CircuitBreakerWorker CreateWorker(ServiceProvider services)
    {
        var logger = services.GetRequiredService<ILogger<CircuitBreakerWorker>>();
        var options = services.GetRequiredService<IOptions<CircuitBreakerOptions>>();
        return new CircuitBreakerWorker(services, logger, options);
    }

    private static async Task SeedEndpointAsync(
        ServiceProvider services,
        EndpointStatus endpointStatus,
        CircuitState circuitState,
        DateTime cooldownUntilUtc)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<WebhookDbContext>();

        var app = new ApplicationEntity
        {
            Id = Guid.NewGuid(),
            Name = "App",
            ApiKeyPrefix = "whe_test_",
            ApiKeyHash = "hash",
            SigningSecret = "secret"
        };

        var endpoint = new Endpoint
        {
            Id = Guid.NewGuid(),
            AppId = app.Id,
            Url = "https://example.com/hook",
            Status = endpointStatus
        };

        var health = new EndpointHealth
        {
            EndpointId = endpoint.Id,
            CircuitState = circuitState,
            CooldownUntil = cooldownUntilUtc,
            ConsecutiveFailures = 5
        };

        db.Applications.Add(app);
        db.Endpoints.Add(endpoint);
        db.EndpointHealths.Add(health);
        await db.SaveChangesAsync();
    }

    private static async Task RunWorkerOnceAsync(CircuitBreakerWorker worker)
    {
        using var cts = new CancellationTokenSource();
        await worker.StartAsync(cts.Token);

        await Task.Delay(250);

        cts.Cancel();
        await worker.StopAsync(CancellationToken.None);
    }
}

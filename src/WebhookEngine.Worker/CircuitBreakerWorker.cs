using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using WebhookEngine.Core.Enums;
using WebhookEngine.Core.Options;
using WebhookEngine.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace WebhookEngine.Worker;

/// <summary>
/// Periodically checks endpoint health and transitions circuit breaker states.
/// Open → HalfOpen when cooldown expires.
/// </summary>
public class CircuitBreakerWorker : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<CircuitBreakerWorker> _logger;
    private readonly CircuitBreakerOptions _options;

    public CircuitBreakerWorker(
        IServiceProvider serviceProvider,
        ILogger<CircuitBreakerWorker> logger,
        IOptions<CircuitBreakerOptions> options)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
        _options = options.Value;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("CircuitBreakerWorker started");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _serviceProvider.CreateScope();
                var dbContext = scope.ServiceProvider.GetRequiredService<WebhookDbContext>();

                // Find endpoints with open circuits whose cooldown has expired
                var expiredCircuits = await dbContext.EndpointHealths
                    .Where(h => h.CircuitState == CircuitState.Open
                        && h.CooldownUntil != null
                        && h.CooldownUntil <= DateTime.UtcNow)
                    .ToListAsync(stoppingToken);

                foreach (var health in expiredCircuits)
                {
                    health.CircuitState = CircuitState.HalfOpen;
                    health.UpdatedAt = DateTime.UtcNow;

                    _logger.LogInformation("Endpoint {EndpointId} circuit transitioned to HalfOpen", health.EndpointId);
                }

                if (expiredCircuits.Count > 0)
                {
                    await dbContext.SaveChangesAsync(stoppingToken);
                }

                // Check every 30 seconds
                await Task.Delay(30_000, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "CircuitBreakerWorker encountered an error");
                await Task.Delay(30_000, stoppingToken);
            }
        }

        _logger.LogInformation("CircuitBreakerWorker stopped");
    }
}

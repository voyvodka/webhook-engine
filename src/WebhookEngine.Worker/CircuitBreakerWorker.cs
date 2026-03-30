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
                    var isInMemory = string.Equals(dbContext.Database.ProviderName, "Microsoft.EntityFrameworkCore.InMemory", StringComparison.Ordinal);
                    var transaction = isInMemory ? null : await dbContext.Database.BeginTransactionAsync(stoppingToken);
                    try
                    {
                        if (!isInMemory)
                        {
                            // Advisory lock key: namespace 100_001 + first 4 bytes of endpointId
                            // Prevents two concurrent workers from both transitioning the same endpoint Open → HalfOpen
                            var endpointBytes = health.EndpointId.ToByteArray();
                            var low = BitConverter.ToUInt32(endpointBytes, 0);
                            var lockKey = ((long)100_001 << 32) | low;

                            var acquired = await dbContext.Database
                                .SqlQuery<bool>($"SELECT pg_try_advisory_xact_lock({lockKey})")
                                .SingleAsync(stoppingToken);

                            if (!acquired)
                            {
                                await transaction!.RollbackAsync(stoppingToken);
                                _logger.LogInformation("Endpoint {EndpointId} circuit transition already in progress by another worker", health.EndpointId);
                                continue;
                            }
                        }

                        // Re-read under lock to verify state hasn't changed since the initial candidate query
                        var freshHealth = await dbContext.EndpointHealths
                            .FirstOrDefaultAsync(h => h.EndpointId == health.EndpointId, stoppingToken);

                        if (freshHealth is null || freshHealth.CircuitState != CircuitState.Open
                            || freshHealth.CooldownUntil is null || freshHealth.CooldownUntil > DateTime.UtcNow)
                        {
                            if (transaction is not null)
                                await transaction.RollbackAsync(stoppingToken);
                            continue;
                        }

                        freshHealth.CircuitState = CircuitState.HalfOpen;
                        freshHealth.CooldownUntil = null;
                        freshHealth.ConsecutiveFailures = 0;
                        freshHealth.UpdatedAt = DateTime.UtcNow;

                        // Update endpoint status within the same transaction
                        var endpoint = await dbContext.Endpoints
                            .FirstOrDefaultAsync(e => e.Id == freshHealth.EndpointId && e.Status != EndpointStatus.Disabled, stoppingToken);
                        if (endpoint is not null && endpoint.Status != EndpointStatus.Degraded)
                        {
                            endpoint.Status = EndpointStatus.Degraded;
                            endpoint.UpdatedAt = DateTime.UtcNow;
                        }

                        await dbContext.SaveChangesAsync(stoppingToken);
                        if (transaction is not null)
                            await transaction.CommitAsync(stoppingToken);

                        _logger.LogInformation("Endpoint {EndpointId} circuit transitioned to HalfOpen", freshHealth.EndpointId);
                    }
                    catch (Exception ex)
                    {
                        if (transaction is not null)
                            await transaction.RollbackAsync(stoppingToken);
                        _logger.LogError(ex, "Failed to transition circuit for endpoint {EndpointId}", health.EndpointId);
                    }
                    finally
                    {
                        if (transaction is not null)
                            await transaction.DisposeAsync();
                    }
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

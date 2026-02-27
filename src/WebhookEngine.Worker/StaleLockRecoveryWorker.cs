using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using WebhookEngine.Core.Interfaces;
using WebhookEngine.Core.Metrics;
using WebhookEngine.Core.Options;

namespace WebhookEngine.Worker;

/// <summary>
/// Periodically recovers stale locks from crashed workers.
/// Messages stuck in 'sending' with locked_at older than StaleLockMinutes get reset to 'pending'.
/// </summary>
public class StaleLockRecoveryWorker : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<StaleLockRecoveryWorker> _logger;
    private readonly DeliveryOptions _options;
    private readonly WebhookMetrics? _metrics;

    public StaleLockRecoveryWorker(
        IServiceProvider serviceProvider,
        ILogger<StaleLockRecoveryWorker> logger,
        IOptions<DeliveryOptions> options,
        WebhookMetrics? metrics = null)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
        _options = options.Value;
        _metrics = metrics;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("StaleLockRecoveryWorker started");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _serviceProvider.CreateScope();
                var messageQueue = scope.ServiceProvider.GetRequiredService<IMessageQueue>();

                var recovered = await messageQueue.ReleaseStaleLocksAsync(
                    TimeSpan.FromMinutes(_options.StaleLockMinutes), stoppingToken);

                if (recovered > 0)
                {
                    _metrics?.RecordStaleLockRecovered(recovered);
                    _logger.LogWarning("StaleLockRecoveryWorker recovered {Count} stale locks", recovered);
                }

                // Check every minute
                await Task.Delay(60_000, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "StaleLockRecoveryWorker encountered an error");
                await Task.Delay(60_000, stoppingToken);
            }
        }

        _logger.LogInformation("StaleLockRecoveryWorker stopped");
    }
}

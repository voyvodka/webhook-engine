using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using WebhookEngine.Infrastructure.Repositories;

namespace WebhookEngine.Worker;

/// <summary>
/// Periodically reschedules failed messages for retry based on exponential backoff.
/// </summary>
public class RetryScheduler : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<RetryScheduler> _logger;

    public RetryScheduler(
        IServiceProvider serviceProvider,
        ILogger<RetryScheduler> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("RetryScheduler started");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _serviceProvider.CreateScope();
                var messageRepo = scope.ServiceProvider.GetRequiredService<MessageRepository>();
                var now = DateTime.UtcNow;

                var requeuedCount = await messageRepo.RequeueDueFailedMessagesAsync(now, stoppingToken);
                if (requeuedCount > 0)
                {
                    _logger.LogInformation("RetryScheduler requeued {MessageCount} failed messages", requeuedCount);
                }

                // Check every 10 seconds
                await Task.Delay(10_000, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "RetryScheduler encountered an error");
                await Task.Delay(30_000, stoppingToken);
            }
        }

        _logger.LogInformation("RetryScheduler stopped");
    }
}

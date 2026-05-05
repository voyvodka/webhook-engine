using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using WebhookEngine.Core.Enums;
using WebhookEngine.Core.Options;
using WebhookEngine.Infrastructure.Data;

namespace WebhookEngine.Worker;

public class RetentionCleanupWorker : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<RetentionCleanupWorker> _logger;
    private readonly RetentionOptions _options;
    private const int BatchSize = 1000;

    public RetentionCleanupWorker(
        IServiceProvider serviceProvider,
        ILogger<RetentionCleanupWorker> logger,
        IOptions<RetentionOptions> options)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
        _options = options.Value;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("RetentionCleanupWorker started");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var delay = GetDelayUntilNextRunUtc(DateTime.UtcNow);
                await Task.Delay(delay, stoppingToken);

                using var scope = _serviceProvider.CreateScope();
                var dbContext = scope.ServiceProvider.GetRequiredService<WebhookDbContext>();

                var now = DateTime.UtcNow;
                var deliveredCutoff = now.AddDays(-_options.DeliveredRetentionDays);
                var deadLetterCutoff = now.AddDays(-_options.DeadLetterRetentionDays);

                var deletedDelivered = await DeleteInBatchesAsync(
                    dbContext,
                    MessageStatus.Delivered,
                    deliveredCutoff,
                    stoppingToken);

                var deletedDeadLetter = await DeleteInBatchesAsync(
                    dbContext,
                    MessageStatus.DeadLetter,
                    deadLetterCutoff,
                    stoppingToken);

                var nulledIdempotencyKeys = await NullifyExpiredIdempotencyKeysAsync(
                    dbContext,
                    stoppingToken);

                _logger.LogInformation(
                    "Retention cleanup completed. DeletedDelivered: {DeletedDelivered}, DeletedDeadLetter: {DeletedDeadLetter}, NulledIdempotencyKeys: {NulledIdempotencyKeys}",
                    deletedDelivered,
                    deletedDeadLetter,
                    nulledIdempotencyKeys);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "RetentionCleanupWorker encountered an error");
                await Task.Delay(TimeSpan.FromMinutes(5), stoppingToken);
            }
        }

        _logger.LogInformation("RetentionCleanupWorker stopped");
    }

    private static async Task<int> DeleteInBatchesAsync(
        WebhookDbContext dbContext,
        MessageStatus status,
        DateTime cutoff,
        CancellationToken ct)
    {
        var totalDeleted = 0;

        while (!ct.IsCancellationRequested)
        {
            var deleted = await dbContext.Messages
                .Where(m => m.Status == status && m.CreatedAt < cutoff)
                .OrderBy(m => m.CreatedAt)
                .Take(BatchSize)
                .ExecuteDeleteAsync(ct);

            if (deleted == 0)
            {
                break;
            }

            totalDeleted += deleted;
        }

        return totalDeleted;
    }

    // Window-expired rows lose their idempotency_key so the same key can be
    // re-used in a fresh window without violating the unique partial index
    // on (app_id, endpoint_id, idempotency_key) WHERE idempotency_key IS NOT NULL.
    // Stripe-style: idempotency keys are valid only inside the configured
    // per-app window (default 24h) and the row itself is preserved.
    private static async Task<int> NullifyExpiredIdempotencyKeysAsync(
        WebhookDbContext dbContext,
        CancellationToken ct)
    {
        var apps = await dbContext.Applications
            .AsNoTracking()
            .Select(a => new { a.Id, a.IdempotencyWindowMinutes })
            .ToListAsync(ct);

        var totalNulled = 0;
        var nowUtc = DateTime.UtcNow;

        foreach (var app in apps)
        {
            if (ct.IsCancellationRequested)
            {
                break;
            }

            var cutoff = nowUtc.AddMinutes(-app.IdempotencyWindowMinutes);

            while (!ct.IsCancellationRequested)
            {
                var batch = await dbContext.Messages
                    .Where(m => m.AppId == app.Id
                        && m.IdempotencyKey != null
                        && m.CreatedAt < cutoff)
                    .OrderBy(m => m.CreatedAt)
                    .Take(BatchSize)
                    .ExecuteUpdateAsync(
                        s => s.SetProperty(m => m.IdempotencyKey, (string?)null),
                        ct);

                if (batch == 0)
                {
                    break;
                }

                totalNulled += batch;
            }
        }

        return totalNulled;
    }

    private static TimeSpan GetDelayUntilNextRunUtc(DateTime nowUtc)
    {
        var nextRun = new DateTime(nowUtc.Year, nowUtc.Month, nowUtc.Day, 3, 0, 0, DateTimeKind.Utc);

        if (nextRun <= nowUtc)
        {
            nextRun = nextRun.AddDays(1);
        }

        return nextRun - nowUtc;
    }
}

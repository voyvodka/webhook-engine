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

                var result = await RunCleanupAsync(dbContext, DateTime.UtcNow, stoppingToken);

                _logger.LogInformation(
                    "Retention cleanup completed. DeletedDelivered: {DeletedDelivered}, DeletedDeadLetter: {DeletedDeadLetter}, NulledIdempotencyKeys: {NulledIdempotencyKeys}",
                    result.DeletedDelivered,
                    result.DeletedDeadLetter,
                    result.NulledIdempotencyKeys);
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

    /// <summary>
    /// Runs one cleanup pass over all applications. Exposed as <c>public</c>
    /// so integration tests can drive it deterministically with a fixed
    /// <paramref name="nowUtc"/> instead of waiting for the daily 03:00 schedule.
    /// In production it is only invoked from <see cref="ExecuteAsync"/>.
    /// </summary>
    public async Task<RetentionCleanupResult> RunCleanupAsync(
        WebhookDbContext dbContext,
        DateTime nowUtc,
        CancellationToken ct)
    {
        var apps = await dbContext.Applications
            .AsNoTracking()
            .Select(a => new AppRetentionConfig(a.Id, a.RetentionDeliveredDays, a.RetentionDeadLetterDays, a.IdempotencyWindowMinutes))
            .ToListAsync(ct);

        var deletedDelivered = 0;
        var deletedDeadLetter = 0;

        foreach (var app in apps)
        {
            if (ct.IsCancellationRequested) break;

            // Per-app override or fall back to the deployment-level RetentionOptions.
            var deliveredDays = app.DeliveredDays ?? _options.DeliveredRetentionDays;
            var deadLetterDays = app.DeadLetterDays ?? _options.DeadLetterRetentionDays;

            deletedDelivered += await DeleteInBatchesAsync(
                dbContext,
                app.Id,
                MessageStatus.Delivered,
                nowUtc.AddDays(-deliveredDays),
                ct);

            deletedDeadLetter += await DeleteInBatchesAsync(
                dbContext,
                app.Id,
                MessageStatus.DeadLetter,
                nowUtc.AddDays(-deadLetterDays),
                ct);
        }

        var nulledIdempotencyKeys = await NullifyExpiredIdempotencyKeysAsync(dbContext, apps, nowUtc, ct);

        return new RetentionCleanupResult(deletedDelivered, deletedDeadLetter, nulledIdempotencyKeys);
    }

    private static async Task<int> DeleteInBatchesAsync(
        WebhookDbContext dbContext,
        Guid appId,
        MessageStatus status,
        DateTime cutoff,
        CancellationToken ct)
    {
        var totalDeleted = 0;

        while (!ct.IsCancellationRequested)
        {
            var deleted = await dbContext.Messages
                .Where(m => m.AppId == appId && m.Status == status && m.CreatedAt < cutoff)
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

    private sealed record AppRetentionConfig(Guid Id, int? DeliveredDays, int? DeadLetterDays, int IdempotencyWindowMinutes);

    // Window-expired rows lose their idempotency_key so the same key can be
    // re-used in a fresh window without violating the unique partial index
    // on (app_id, endpoint_id, idempotency_key) WHERE idempotency_key IS NOT NULL.
    // Stripe-style: idempotency keys are valid only inside the configured
    // per-app window (default 24h) and the row itself is preserved.
    private static async Task<int> NullifyExpiredIdempotencyKeysAsync(
        WebhookDbContext dbContext,
        IReadOnlyList<AppRetentionConfig> apps,
        DateTime nowUtc,
        CancellationToken ct)
    {
        var totalNulled = 0;

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

/// <summary>
/// Per-pass tally of what the retention sweep removed. Returned by
/// <see cref="RetentionCleanupWorker.RunCleanupAsync"/> for tests and logs.
/// </summary>
public sealed record RetentionCleanupResult(
    int DeletedDelivered,
    int DeletedDeadLetter,
    int NulledIdempotencyKeys);

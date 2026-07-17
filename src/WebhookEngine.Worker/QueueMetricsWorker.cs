using System.Diagnostics.Metrics;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using WebhookEngine.Core.Enums;
using WebhookEngine.Core.Metrics;
using WebhookEngine.Infrastructure.Data;

namespace WebhookEngine.Worker;

/// <summary>
/// Refreshes the operator backlog gauges on an interval so their (sync, per-scrape) ObservableGauge
/// callbacks only read a cached snapshot. Gauges live on the WebhookEngine meter — the existing
/// <c>AddMeter(WebhookMetrics.MeterName)</c> subscription exports them with no extra wiring.
/// </summary>
public sealed class QueueMetricsWorker : BackgroundService
{
    private const int PollIntervalMs = 15_000;
    private const int ErrorBackoffMs = 30_000;

    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<QueueMetricsWorker> _logger;
    private readonly Meter _meter;

    private QueueMetricsSnapshot _snapshot = new(0, 0, 0);

    public QueueMetricsWorker(
        IServiceProvider serviceProvider,
        ILogger<QueueMetricsWorker> logger,
        IMeterFactory meterFactory)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
        _meter = meterFactory.Create(WebhookMetrics.MeterName);

        _meter.CreateObservableGauge(
            "webhookengine.queue.depth",
            () => Volatile.Read(ref _snapshot).PendingBacklog,
            unit: "{message}",
            description: "Pending messages currently due for delivery (status='Pending' AND scheduled_at <= now)");

        _meter.CreateObservableGauge(
            "webhookengine.queue.oldest_pending_age",
            () => Volatile.Read(ref _snapshot).OldestPendingAgeSeconds,
            unit: "s",
            description: "Age in seconds of the oldest due pending message (head-of-line latency); 0 when the queue is empty");

        _meter.CreateObservableGauge(
            "webhookengine.circuit.open",
            () => Volatile.Read(ref _snapshot).OpenCircuitCount,
            unit: "{endpoint}",
            description: "Endpoints whose circuit breaker is currently Open");
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("QueueMetricsWorker started");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _serviceProvider.CreateScope();
                var dbContext = scope.ServiceProvider.GetRequiredService<WebhookDbContext>();

                var snapshot = await ComputeSnapshotAsync(dbContext, DateTime.UtcNow, stoppingToken);
                Volatile.Write(ref _snapshot, snapshot);

                await Task.Delay(PollIntervalMs, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "QueueMetricsWorker encountered an error refreshing queue gauges");
                await Task.Delay(ErrorBackoffMs, stoppingToken);
            }
        }

        _logger.LogInformation("QueueMetricsWorker stopped");
    }

    internal static async Task<QueueMetricsSnapshot> ComputeSnapshotAsync(
        WebhookDbContext dbContext, DateTime now, CancellationToken ct = default)
    {
        // Single round-trip for backlog + head-of-line age over the due-pending set.
        var due = await dbContext.Messages
            .AsNoTracking()
            .Where(m => m.Status == MessageStatus.Pending && m.ScheduledAt <= now)
            .GroupBy(_ => 1)
            .Select(g => new { Count = g.Count(), Oldest = (DateTime?)g.Min(x => x.ScheduledAt) })
            .FirstOrDefaultAsync(ct);

        long backlog = due?.Count ?? 0;
        long oldestAgeSeconds = due?.Oldest is { } oldest
            ? Math.Max(0, (long)(now - oldest).TotalSeconds)
            : 0;

        long openCircuits = await dbContext.EndpointHealths
            .AsNoTracking()
            .CountAsync(h => h.CircuitState == CircuitState.Open, ct);

        return new QueueMetricsSnapshot(backlog, oldestAgeSeconds, openCircuits);
    }

    internal sealed record QueueMetricsSnapshot(long PendingBacklog, long OldestPendingAgeSeconds, long OpenCircuitCount);
}

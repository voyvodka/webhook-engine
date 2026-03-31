using Microsoft.EntityFrameworkCore;
using WebhookEngine.Infrastructure.Data;

namespace WebhookEngine.Infrastructure.Repositories;

public record DashboardOverviewStats
{
    public int TotalMessages { get; init; }
    public int Delivered { get; init; }
    public int Failed { get; init; }
    public int Pending { get; init; }
    public int DeadLetter { get; init; }
    public int QueueDepth { get; init; }
    public double? AvgLatencyMs { get; init; }
    public int TotalEndpoints { get; init; }
    public int HealthyEndpoints { get; init; }
    public int DegradedEndpoints { get; init; }
    public int FailedEndpoints { get; init; }
    public int DisabledEndpoints { get; init; }
}

/// <summary>
/// Repository for dashboard overview stats — executes a single aggregated SQL query
/// instead of 11 separate CountAsync/AverageAsync round-trips.
/// </summary>
public class DashboardStatsRepository
{
    private readonly WebhookDbContext _dbContext;

    public DashboardStatsRepository(WebhookDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<DashboardOverviewStats> GetOverviewStatsAsync(DateTime cutoff, CancellationToken ct = default)
    {
        var results = await _dbContext.Database
            .SqlQueryRaw<DashboardOverviewStats>(
                """
                SELECT
                    COUNT(*) FILTER (WHERE m.created_at >= @p0)                                   AS "TotalMessages",
                    COUNT(*) FILTER (WHERE m.created_at >= @p0 AND m.status = 'Delivered')        AS "Delivered",
                    COUNT(*) FILTER (WHERE m.created_at >= @p0 AND m.status = 'Failed')           AS "Failed",
                    COUNT(*) FILTER (WHERE m.created_at >= @p0 AND m.status = 'Pending')          AS "Pending",
                    COUNT(*) FILTER (WHERE m.created_at >= @p0 AND m.status = 'DeadLetter')       AS "DeadLetter",
                    COUNT(*) FILTER (WHERE m.status IN ('Pending','Sending'))                      AS "QueueDepth",
                    (SELECT COALESCE(AVG(a.latency_ms), 0) FROM message_attempts a
                     WHERE a.created_at >= @p0 AND a.status = 'Success')                          AS "AvgLatencyMs",
                    (SELECT COUNT(*) FROM endpoints)                                               AS "TotalEndpoints",
                    (SELECT COUNT(*) FROM endpoints WHERE status = 'Active')                      AS "HealthyEndpoints",
                    (SELECT COUNT(*) FROM endpoints WHERE status = 'Degraded')                    AS "DegradedEndpoints",
                    (SELECT COUNT(*) FROM endpoints WHERE status = 'Failed')                      AS "FailedEndpoints",
                    (SELECT COUNT(*) FROM endpoints WHERE status = 'Disabled')                    AS "DisabledEndpoints"
                FROM messages m
                """,
                cutoff)
            .ToListAsync(ct);
        return results.Single();
    }
}

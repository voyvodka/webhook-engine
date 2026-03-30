using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WebhookEngine.API.Contracts;
using WebhookEngine.Core.Enums;
using WebhookEngine.Infrastructure.Data;

namespace WebhookEngine.API.Controllers;

/// <summary>
/// Dashboard analytics endpoints — overview stats and delivery timeline.
/// Authenticated via dashboard session cookie (not API key).
/// </summary>
[ApiController]
[Route("api/v1/dashboard")]
[Authorize(AuthenticationSchemes = CookieAuthenticationDefaults.AuthenticationScheme)]
public class DashboardAnalyticsController : ControllerBase
{
    private readonly WebhookDbContext _dbContext;

    public DashboardAnalyticsController(WebhookDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    [HttpGet("overview")]
    public async Task<IActionResult> Overview(CancellationToken ct)
    {
        var cutoff = DateTime.UtcNow.AddHours(-24);

        // Last 24h message stats — individual CountAsync calls avoid EF "FirstWithoutOrderBy" warning
        var recentMessages = _dbContext.Messages.AsNoTracking().Where(m => m.CreatedAt >= cutoff);
        var total = await recentMessages.CountAsync(ct);
        var delivered = await recentMessages.CountAsync(m => m.Status == MessageStatus.Delivered, ct);
        var failed = await recentMessages.CountAsync(m => m.Status == MessageStatus.Failed, ct);
        var pending = await recentMessages.CountAsync(m => m.Status == MessageStatus.Pending, ct);
        var deadLetter = await recentMessages.CountAsync(m => m.Status == MessageStatus.DeadLetter, ct);

        // Average latency (last 24h)
        var avgLatency = await _dbContext.MessageAttempts
            .AsNoTracking()
            .Where(a => a.CreatedAt >= cutoff && a.Status == AttemptStatus.Success)
            .AverageAsync(a => (double?)a.LatencyMs, ct) ?? 0;

        // Endpoint health summary — derive from endpoint status
        var endpointsQuery = _dbContext.Endpoints.AsNoTracking();
        var totalEndpoints = await endpointsQuery.CountAsync(ct);
        var healthyEndpoints = await endpointsQuery.CountAsync(e => e.Status == EndpointStatus.Active, ct);
        var degradedEndpoints = await endpointsQuery.CountAsync(e => e.Status == EndpointStatus.Degraded, ct);
        var failedEndpoints = await endpointsQuery.CountAsync(e => e.Status == EndpointStatus.Failed, ct);
        var disabledEndpoints = await endpointsQuery.CountAsync(e => e.Status == EndpointStatus.Disabled, ct);

        // Queue depth (messages currently pending or sending)
        var queueDepth = await _dbContext.Messages
            .AsNoTracking()
            .CountAsync(m => m.Status == MessageStatus.Pending || m.Status == MessageStatus.Sending, ct);

        var successRate = total > 0 ? Math.Round((double)delivered / total * 100, 1) : 0;

        return Ok(ApiEnvelope.Success(HttpContext, new
        {
            last24h = new
            {
                totalMessages = total,
                delivered,
                failed,
                pending,
                deadLetter,
                successRate,
                avgLatencyMs = Math.Round(avgLatency, 0)
            },
            endpoints = new
            {
                total = totalEndpoints,
                healthy = healthyEndpoints,
                degraded = degradedEndpoints,
                failed = failedEndpoints,
                disabled = disabledEndpoints
            },
            queueDepth
        }));
    }

    [HttpGet("timeline")]
    public async Task<IActionResult> Timeline(
        [FromQuery] string period = "24h",
        [FromQuery] string interval = "15m",
        CancellationToken ct = default)
    {
        var (startTime, intervalMinutes) = ParseTimelineParams(period, interval);

        // Raw query for time-bucketed aggregation — performance-critical, raw SQL is acceptable
        var buckets = await _dbContext.Database
            .SqlQueryRaw<TimelineBucket>(
                """
                SELECT
                    date_bin((@p0 || ' minutes')::interval, created_at, TIMESTAMPTZ '2000-01-01 00:00:00+00') AS timestamp,
                    COUNT(*) FILTER (WHERE status = 'Delivered') AS delivered,
                    COUNT(*) FILTER (WHERE status = 'Failed' OR status = 'DeadLetter') AS failed
                FROM messages
                WHERE created_at >= @p1
                GROUP BY 1
                ORDER BY 1
                """,
                intervalMinutes, startTime)
            .ToListAsync(ct);

        return Ok(ApiEnvelope.Success(HttpContext, new { buckets }));
    }

    // ──────────────────────────────────────────────────
    // Helper
    // ──────────────────────────────────────────────────

    private static (DateTime StartTime, int IntervalMinutes) ParseTimelineParams(string period, string interval)
    {
        var startTime = period switch
        {
            "1h" => DateTime.UtcNow.AddHours(-1),
            "24h" => DateTime.UtcNow.AddHours(-24),
            "7d" => DateTime.UtcNow.AddDays(-7),
            "30d" => DateTime.UtcNow.AddDays(-30),
            _ => DateTime.UtcNow.AddHours(-24)
        };

        var intervalMinutes = interval switch
        {
            "5m" => 5,
            "15m" => 15,
            "1h" => 60,
            "1d" => 1440,
            _ => 15
        };

        return (startTime, intervalMinutes);
    }
}

public class TimelineBucket
{
    public DateTime Timestamp { get; set; }
    public int Delivered { get; set; }
    public int Failed { get; set; }
}

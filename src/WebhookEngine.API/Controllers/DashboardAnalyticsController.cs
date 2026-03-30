using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WebhookEngine.API.Contracts;
using WebhookEngine.Infrastructure.Data;
using WebhookEngine.Infrastructure.Repositories;

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
    private readonly DashboardStatsRepository _statsRepository;

    public DashboardAnalyticsController(WebhookDbContext dbContext, DashboardStatsRepository statsRepository)
    {
        _dbContext = dbContext;
        _statsRepository = statsRepository;
    }

    [HttpGet("overview")]
    public async Task<IActionResult> Overview(CancellationToken ct)
    {
        var cutoff = DateTime.UtcNow.AddHours(-24);
        var stats = await _statsRepository.GetOverviewStatsAsync(cutoff, ct);

        var successRate = stats.TotalMessages > 0
            ? Math.Round((double)stats.Delivered / stats.TotalMessages * 100, 1)
            : 0;

        return Ok(ApiEnvelope.Success(HttpContext, new
        {
            last24h = new
            {
                totalMessages = stats.TotalMessages,
                delivered = stats.Delivered,
                failed = stats.Failed,
                pending = stats.Pending,
                deadLetter = stats.DeadLetter,
                successRate,
                avgLatencyMs = Math.Round(stats.AvgLatencyMs ?? 0, 0)
            },
            endpoints = new
            {
                total = stats.TotalEndpoints,
                healthy = stats.HealthyEndpoints,
                degraded = stats.DegradedEndpoints,
                failed = stats.FailedEndpoints,
                disabled = stats.DisabledEndpoints
            },
            queueDepth = stats.QueueDepth
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

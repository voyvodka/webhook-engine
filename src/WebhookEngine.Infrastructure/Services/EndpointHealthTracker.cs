using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using WebhookEngine.Core.Entities;
using WebhookEngine.Core.Enums;
using WebhookEngine.Core.Interfaces;
using WebhookEngine.Core.Metrics;
using WebhookEngine.Core.Options;
using WebhookEngine.Infrastructure.Data;

namespace WebhookEngine.Infrastructure.Services;

public class EndpointHealthTracker : IEndpointHealthTracker
{
    private readonly WebhookDbContext _dbContext;
    private readonly CircuitBreakerOptions _options;
    private readonly WebhookMetrics? _metrics;
    private readonly ILogger<EndpointHealthTracker>? _logger;

    public EndpointHealthTracker(
        WebhookDbContext dbContext,
        IOptions<CircuitBreakerOptions> options,
        WebhookMetrics? metrics = null,
        ILogger<EndpointHealthTracker>? logger = null)
    {
        _dbContext = dbContext;
        _options = options.Value;
        _metrics = metrics;
        _logger = logger;
    }

    public Task RecordSuccessAsync(Guid endpointId, CancellationToken ct = default) =>
        WithEndpointLockAsync(endpointId, ApplySuccess, ct);

    public Task RecordFailureAsync(Guid endpointId, CancellationToken ct = default) =>
        WithEndpointLockAsync(endpointId, ApplyFailure, ct);

    public async Task<CircuitState> GetCircuitStateAsync(Guid endpointId, CancellationToken ct = default)
    {
        var health = await _dbContext.EndpointHealths
            .AsNoTracking()
            .FirstOrDefaultAsync(h => h.EndpointId == endpointId, ct);
        return health?.CircuitState ?? CircuitState.Closed;
    }

    public async Task<EndpointHealth?> GetHealthAsync(Guid endpointId, CancellationToken ct = default)
    {
        return await _dbContext.EndpointHealths
            .AsNoTracking()
            .FirstOrDefaultAsync(h => h.EndpointId == endpointId, ct);
    }

    /// <summary>
    /// Wraps a health-mutation in an advisory-locked transaction so two
    /// concurrent attempts on the same endpoint can't race the consecutive-
    /// failure counter or step on each other's state transitions. Same
    /// lock-key namespace as <c>CircuitBreakerWorker</c> so a sweep and a
    /// delivery attempt serialize against each other.
    /// </summary>
    private async Task WithEndpointLockAsync(Guid endpointId, Action<EndpointHealth> mutate, CancellationToken ct)
    {
        var isInMemory = string.Equals(
            _dbContext.Database.ProviderName,
            "Microsoft.EntityFrameworkCore.InMemory",
            StringComparison.Ordinal);

        if (isInMemory)
        {
            // InMemory tests don't speak Postgres advisory locks; tests don't
            // exercise concurrent writers anyway.
            var memHealth = await GetOrCreateHealthAsync(endpointId, ct);
            mutate(memHealth);
            await UpdateEndpointStatusAsync(endpointId, memHealth, DateTime.UtcNow, ct);
            await _dbContext.SaveChangesAsync(ct);
            return;
        }

        await using var transaction = await _dbContext.Database.BeginTransactionAsync(ct);
        try
        {
            var endpointBytes = endpointId.ToByteArray();
            var low = BitConverter.ToUInt32(endpointBytes, 0);
            var lockKey = ((long)100_001 << 32) | low;
            await _dbContext.Database
                .ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock({lockKey})", ct);

            // Re-read inside the lock so we mutate the freshest state.
            var health = await _dbContext.EndpointHealths
                .FirstOrDefaultAsync(h => h.EndpointId == endpointId, ct);

            if (health is null)
            {
                health = new EndpointHealth { EndpointId = endpointId };
                _dbContext.EndpointHealths.Add(health);
            }

            mutate(health);
            await UpdateEndpointStatusAsync(endpointId, health, DateTime.UtcNow, ct);
            await _dbContext.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
        }
        catch
        {
            try { await transaction.RollbackAsync(ct); } catch { /* best-effort */ }
            throw;
        }
    }

    private void ApplySuccess(EndpointHealth health)
    {
        var now = DateTime.UtcNow;
        health.LastSuccessAt = now;
        health.UpdatedAt = now;

        if (health.CircuitState == CircuitState.HalfOpen)
        {
            // ConsecutiveFailures doubles as the half-open success counter
            // by historical accident — flagged in the audit, tracked as a
            // follow-up to keep blast radius small.
            var successThreshold = Math.Max(1, _options.SuccessThreshold);
            health.ConsecutiveFailures = Math.Max(0, health.ConsecutiveFailures) + 1;

            if (health.ConsecutiveFailures >= successThreshold)
            {
                health.CircuitState = CircuitState.Closed;
                health.CooldownUntil = null;
                health.ConsecutiveFailures = 0;
                _metrics?.RecordCircuitClosed();
            }
        }
        else
        {
            health.ConsecutiveFailures = 0;
            health.CooldownUntil = null;
        }
    }

    private void ApplyFailure(EndpointHealth health)
    {
        var now = DateTime.UtcNow;
        health.ConsecutiveFailures++;
        health.LastFailureAt = now;
        health.UpdatedAt = now;

        if (health.CircuitState == CircuitState.Closed
            && health.ConsecutiveFailures >= _options.FailureThreshold)
        {
            health.CircuitState = CircuitState.Open;
            health.CooldownUntil = now.AddMinutes(_options.CooldownMinutes);
            _metrics?.RecordCircuitOpened();
        }
        else if (health.CircuitState == CircuitState.HalfOpen)
        {
            health.CircuitState = CircuitState.Open;
            health.CooldownUntil = now.AddMinutes(_options.CooldownMinutes);
            _metrics?.RecordCircuitOpened();
        }
    }

    private async Task<EndpointHealth> GetOrCreateHealthAsync(Guid endpointId, CancellationToken ct)
    {
        var health = await _dbContext.EndpointHealths.FirstOrDefaultAsync(h => h.EndpointId == endpointId, ct);
        if (health is null)
        {
            health = new EndpointHealth { EndpointId = endpointId };
            _dbContext.EndpointHealths.Add(health);
        }
        return health;
    }

    private async Task UpdateEndpointStatusAsync(Guid endpointId, EndpointHealth health, DateTime now, CancellationToken ct)
    {
        var endpoint = await _dbContext.Endpoints.FirstOrDefaultAsync(e => e.Id == endpointId, ct);
        if (endpoint is null)
            return;

        if (endpoint.Status == EndpointStatus.Disabled)
            return;

        var targetStatus = ResolveEndpointStatus(health);
        if (endpoint.Status == targetStatus)
            return;

        endpoint.Status = targetStatus;
        endpoint.UpdatedAt = now;
    }

    private static EndpointStatus ResolveEndpointStatus(EndpointHealth health)
    {
        if (health.CircuitState == CircuitState.Open)
            return EndpointStatus.Failed;

        if (health.CircuitState == CircuitState.HalfOpen)
            return EndpointStatus.Degraded;

        return health.ConsecutiveFailures > 0
            ? EndpointStatus.Degraded
            : EndpointStatus.Active;
    }
}

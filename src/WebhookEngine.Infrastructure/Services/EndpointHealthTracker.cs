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
    private readonly IDeliveryNotifier? _notifier;

    public EndpointHealthTracker(
        WebhookDbContext dbContext,
        IOptions<CircuitBreakerOptions> options,
        WebhookMetrics? metrics = null,
        ILogger<EndpointHealthTracker>? logger = null,
        IDeliveryNotifier? notifier = null)
    {
        _dbContext = dbContext;
        _options = options.Value;
        _metrics = metrics;
        _logger = logger;
        _notifier = notifier;
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

        // We want to fire NotifyEndpointHealthChangedAsync exactly when the
        // mutation actually changes either the circuit state or the visible
        // endpoint status — not on every recorded success. Capture the pre-
        // mutation values and a post-mutation snapshot so the comparison is
        // local to this method, and so we only emit AFTER the commit lands
        // (or, on InMemory, after SaveChanges).
        HealthSnapshot? notifySnapshot = null;

        if (isInMemory)
        {
            // InMemory tests don't speak Postgres advisory locks; tests don't
            // exercise concurrent writers anyway.
            var memHealth = await GetOrCreateHealthAsync(endpointId, ct);
            var (memChanged, memAfter) = await ApplyAndCaptureChangeAsync(endpointId, memHealth, mutate, ct);
            await _dbContext.SaveChangesAsync(ct);
            if (memChanged) notifySnapshot = memAfter;
        }
        else
        {
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

                var (changed, after) = await ApplyAndCaptureChangeAsync(endpointId, health, mutate, ct);
                await _dbContext.SaveChangesAsync(ct);
                await transaction.CommitAsync(ct);
                if (changed) notifySnapshot = after;
            }
            catch
            {
                try { await transaction.RollbackAsync(ct); } catch { /* best-effort */ }
                throw;
            }
        }

        // Notification lives outside the lock + transaction so a slow client
        // can't keep the advisory lock on the row, and so a hub failure
        // can't roll back what we already committed.
        if (notifySnapshot is { } snapshot && _notifier is not null)
        {
            try
            {
                await _notifier.NotifyEndpointHealthChangedAsync(
                    endpointId,
                    snapshot.EndpointStatus,
                    snapshot.CircuitState,
                    snapshot.ConsecutiveFailures,
                    snapshot.CooldownUntilUtc,
                    ct);
            }
            catch (Exception ex)
            {
                // The DB has the truth — a missed live update is just a stale
                // dashboard badge until the next user-driven refresh.
                _logger?.LogWarning(ex, "Failed to push endpoint-health notification for {EndpointId}", endpointId);
            }
        }
    }

    private async Task<(bool Changed, HealthSnapshot After)> ApplyAndCaptureChangeAsync(
        Guid endpointId,
        EndpointHealth health,
        Action<EndpointHealth> mutate,
        CancellationToken ct)
    {
        var beforeCircuit = health.CircuitState;

        // Fetch the tracked endpoint up front so the before/after read both
        // see the same instance EF is mutating, then thread it into
        // UpdateEndpointStatus so the helper doesn't issue a second round-trip
        // for the same row.
        var endpoint = await _dbContext.Endpoints
            .FirstOrDefaultAsync(e => e.Id == endpointId, ct);
        var beforeStatus = endpoint?.Status ?? EndpointStatus.Active;

        mutate(health);
        ApplyEndpointStatus(endpoint, health, DateTime.UtcNow);

        var afterStatus = endpoint?.Status ?? beforeStatus;
        var afterSnapshot = new HealthSnapshot(
            afterStatus,
            health.CircuitState,
            health.ConsecutiveFailures,
            health.CooldownUntil);

        var changed = beforeCircuit != health.CircuitState || beforeStatus != afterStatus;
        return (changed, afterSnapshot);
    }

    private sealed record HealthSnapshot(
        EndpointStatus EndpointStatus,
        CircuitState CircuitState,
        int ConsecutiveFailures,
        DateTime? CooldownUntilUtc);

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

    /// <summary>
    /// Applies the target status onto an already-fetched, EF-tracked endpoint
    /// row. The caller owns the fetch — eliminating the second round-trip that
    /// the previous <c>UpdateEndpointStatusAsync</c> overload incurred.
    /// </summary>
    private static void ApplyEndpointStatus(WebhookEngine.Core.Entities.Endpoint? endpoint, EndpointHealth health, DateTime now)
    {
        if (endpoint is null) return;
        if (endpoint.Status == EndpointStatus.Disabled) return;

        var targetStatus = ResolveEndpointStatus(health);
        if (endpoint.Status == targetStatus) return;

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

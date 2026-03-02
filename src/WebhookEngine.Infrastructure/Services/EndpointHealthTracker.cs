using Microsoft.EntityFrameworkCore;
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

    public EndpointHealthTracker(
        WebhookDbContext dbContext,
        IOptions<CircuitBreakerOptions> options,
        WebhookMetrics? metrics = null)
    {
        _dbContext = dbContext;
        _options = options.Value;
        _metrics = metrics;
    }

    public async Task RecordSuccessAsync(Guid endpointId, CancellationToken ct = default)
    {
        var health = await GetOrCreateHealthAsync(endpointId, ct);
        var now = DateTime.UtcNow;

        health.LastSuccessAt = now;
        health.UpdatedAt = now;

        if (health.CircuitState == CircuitState.HalfOpen)
        {
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
        }

        await UpdateEndpointStatusAsync(endpointId, health, now, ct);

        await _dbContext.SaveChangesAsync(ct);
    }

    public async Task RecordFailureAsync(Guid endpointId, CancellationToken ct = default)
    {
        var health = await GetOrCreateHealthAsync(endpointId, ct);

        health.ConsecutiveFailures++;
        health.LastFailureAt = DateTime.UtcNow;
        health.UpdatedAt = DateTime.UtcNow;

        if (health.ConsecutiveFailures >= _options.FailureThreshold && health.CircuitState == CircuitState.Closed)
        {
            health.CircuitState = CircuitState.Open;
            health.CooldownUntil = DateTime.UtcNow.AddMinutes(_options.CooldownMinutes);
            _metrics?.RecordCircuitOpened();
        }
        else if (health.CircuitState == CircuitState.HalfOpen)
        {
            health.CircuitState = CircuitState.Open;
            health.CooldownUntil = DateTime.UtcNow.AddMinutes(_options.CooldownMinutes);
            _metrics?.RecordCircuitOpened();
        }

        await UpdateEndpointStatusAsync(endpointId, health, DateTime.UtcNow, ct);

        await _dbContext.SaveChangesAsync(ct);
    }

    public async Task<CircuitState> GetCircuitStateAsync(Guid endpointId, CancellationToken ct = default)
    {
        var health = await _dbContext.EndpointHealths
            .FirstOrDefaultAsync(h => h.EndpointId == endpointId, ct);

        if (health is null)
            return CircuitState.Closed;

        // Transition from Open to HalfOpen if cooldown expired
        if (health.CircuitState == CircuitState.Open && health.CooldownUntil.HasValue && health.CooldownUntil <= DateTime.UtcNow)
        {
            health.CircuitState = CircuitState.HalfOpen;
            health.CooldownUntil = null;
            health.ConsecutiveFailures = 0;
            health.UpdatedAt = DateTime.UtcNow;
            await UpdateEndpointStatusAsync(endpointId, health, DateTime.UtcNow, ct);
            await _dbContext.SaveChangesAsync(ct);
            return CircuitState.HalfOpen;
        }

        return health.CircuitState;
    }

    public async Task<EndpointHealth?> GetHealthAsync(Guid endpointId, CancellationToken ct = default)
    {
        return await _dbContext.EndpointHealths
            .AsNoTracking()
            .FirstOrDefaultAsync(h => h.EndpointId == endpointId, ct);
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

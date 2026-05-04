using Microsoft.EntityFrameworkCore;
using WebhookEngine.Core.Entities;
using WebhookEngine.Core.Enums;
using WebhookEngine.Infrastructure.Data;

namespace WebhookEngine.Infrastructure.Repositories;

public class EndpointRepository
{
    private readonly WebhookDbContext _dbContext;

    public EndpointRepository(WebhookDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<Endpoint?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        return await _dbContext.Endpoints
            .Include(e => e.Application)
            .Include(e => e.EventTypes)
            .Include(e => e.Health)
            .FirstOrDefaultAsync(e => e.Id == id, ct);
    }

    public async Task<Endpoint?> GetByIdAsync(Guid appId, Guid id, CancellationToken ct = default)
    {
        return await _dbContext.Endpoints
            .Include(e => e.Application)
            .Include(e => e.EventTypes)
            .Include(e => e.Health)
            .FirstOrDefaultAsync(e => e.AppId == appId && e.Id == id, ct);
    }

    public async Task<List<EndpointListItem>> ListByAppIdAsync(
        Guid appId,
        EndpointStatus? status,
        int page,
        int pageSize,
        CancellationToken ct = default)
    {
        var query = _dbContext.Endpoints
            .AsNoTracking()
            .Where(e => e.AppId == appId)
            .AsQueryable();

        if (status.HasValue)
        {
            query = query.Where(e => e.Status == status.Value);
        }

        return await query
            .Select(e => new EndpointListItem
            {
                Id = e.Id,
                AppId = e.AppId,
                AppName = e.Application != null ? e.Application.Name : null,
                Url = e.Url,
                Description = e.Description,
                Status = e.Status,
                CircuitState = e.Health != null ? e.Health.CircuitState.ToString() : null,
                CustomHeadersJson = e.CustomHeadersJson,
                SecretOverride = e.SecretOverride,
                MetadataJson = e.MetadataJson,
                TransformExpression = e.TransformExpression,
                TransformEnabled = e.TransformEnabled,
                TransformValidatedAt = e.TransformValidatedAt,
                EventTypeNames = e.EventTypes.Select(et => et.Name).ToList(),
                EventTypeIds = e.EventTypes.Select(et => et.Id).ToList(),
                CreatedAt = e.CreatedAt,
                UpdatedAt = e.UpdatedAt
            })
            .OrderByDescending(e => e.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);
    }

    public async Task<int> CountByAppIdAsync(Guid appId, EndpointStatus? status, CancellationToken ct = default)
    {
        var query = _dbContext.Endpoints
            .AsNoTracking()
            .Where(e => e.AppId == appId)
            .AsQueryable();

        if (status.HasValue)
        {
            query = query.Where(e => e.Status == status.Value);
        }

        return await query.CountAsync(ct);
    }

    public async Task<List<Endpoint>> GetSubscribedEndpointsAsync(Guid appId, Guid eventTypeId, CancellationToken ct = default)
    {
        return await _dbContext.Endpoints
            .AsNoTracking()
            .Where(e => e.AppId == appId
                && e.Status == EndpointStatus.Active
                && (e.EventTypes.Count == 0 || e.EventTypes.Any(et => et.Id == eventTypeId)))
            .ToListAsync(ct);
    }

    public async Task<Endpoint> CreateAsync(Endpoint endpoint, CancellationToken ct = default)
    {
        _dbContext.Endpoints.Add(endpoint);
        await _dbContext.SaveChangesAsync(ct);
        return endpoint;
    }

    public async Task UpdateAsync(Endpoint endpoint, CancellationToken ct = default)
    {
        endpoint.UpdatedAt = DateTime.UtcNow;
        _dbContext.Endpoints.Update(endpoint);
        await _dbContext.SaveChangesAsync(ct);
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        await _dbContext.Endpoints.Where(e => e.Id == id).ExecuteDeleteAsync(ct);
    }

    public async Task DeleteAsync(Guid appId, Guid id, CancellationToken ct = default)
    {
        await _dbContext.Endpoints
            .Where(e => e.AppId == appId && e.Id == id)
            .ExecuteDeleteAsync(ct);
    }

    /// <summary>
    /// Cross-app list for dashboard admin — returns endpoints across all applications
    /// using Select projection to avoid N+1 on EventTypes.
    /// </summary>
    public async Task<List<EndpointListItem>> ListAllAsync(
        Guid? appId,
        EndpointStatus? status,
        int page,
        int pageSize,
        CancellationToken ct = default)
    {
        var query = _dbContext.Endpoints
            .AsNoTracking()
            .AsQueryable();

        if (appId.HasValue)
            query = query.Where(e => e.AppId == appId.Value);

        if (status.HasValue)
            query = query.Where(e => e.Status == status.Value);

        return await query
            .Select(e => new EndpointListItem
            {
                Id = e.Id,
                AppId = e.AppId,
                AppName = e.Application != null ? e.Application.Name : null,
                Url = e.Url,
                Description = e.Description,
                Status = e.Status,
                CircuitState = e.Health != null ? e.Health.CircuitState.ToString() : null,
                CustomHeadersJson = e.CustomHeadersJson,
                SecretOverride = e.SecretOverride,
                MetadataJson = e.MetadataJson,
                TransformExpression = e.TransformExpression,
                TransformEnabled = e.TransformEnabled,
                TransformValidatedAt = e.TransformValidatedAt,
                EventTypeNames = e.EventTypes.Select(et => et.Name).ToList(),
                EventTypeIds = e.EventTypes.Select(et => et.Id).ToList(),
                CreatedAt = e.CreatedAt,
                UpdatedAt = e.UpdatedAt
            })
            .OrderByDescending(e => e.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);
    }

    public async Task<int> CountAllAsync(Guid? appId, EndpointStatus? status, CancellationToken ct = default)
    {
        var query = _dbContext.Endpoints.AsQueryable();

        if (appId.HasValue)
            query = query.Where(e => e.AppId == appId.Value);

        if (status.HasValue)
            query = query.Where(e => e.Status == status.Value);

        return await query.CountAsync(ct);
    }
}

public record EndpointListItem
{
    public Guid Id { get; init; }
    public Guid AppId { get; init; }
    public string? AppName { get; init; }
    public string Url { get; init; } = string.Empty;
    public string? Description { get; init; }
    public EndpointStatus Status { get; init; }
    public string? CircuitState { get; init; }
    public string CustomHeadersJson { get; init; } = "{}";
    public string? SecretOverride { get; init; }
    public string MetadataJson { get; init; } = "{}";
    public string? TransformExpression { get; init; }
    public bool TransformEnabled { get; init; }
    public DateTime? TransformValidatedAt { get; init; }
    public List<string> EventTypeNames { get; init; } = [];
    public List<Guid> EventTypeIds { get; init; } = [];
    public DateTime CreatedAt { get; init; }
    public DateTime UpdatedAt { get; init; }
}

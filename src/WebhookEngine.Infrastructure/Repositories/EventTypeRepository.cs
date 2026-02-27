using Microsoft.EntityFrameworkCore;
using WebhookEngine.Core.Entities;
using WebhookEngine.Infrastructure.Data;

namespace WebhookEngine.Infrastructure.Repositories;

public class EventTypeRepository
{
    private readonly WebhookDbContext _dbContext;

    public EventTypeRepository(WebhookDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<EventType?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        return await _dbContext.EventTypes.FirstOrDefaultAsync(et => et.Id == id, ct);
    }

    public async Task<EventType?> GetByIdAsync(Guid appId, Guid id, CancellationToken ct = default)
    {
        return await _dbContext.EventTypes
            .FirstOrDefaultAsync(et => et.AppId == appId && et.Id == id, ct);
    }

    public async Task<EventType?> GetByNameAsync(Guid appId, string name, CancellationToken ct = default)
    {
        return await _dbContext.EventTypes
            .FirstOrDefaultAsync(et => et.AppId == appId && et.Name == name, ct);
    }

    public async Task<List<EventType>> ListByAppIdAsync(Guid appId, bool includeArchived, int page, int pageSize, CancellationToken ct = default)
    {
        var query = _dbContext.EventTypes
            .AsNoTracking()
            .Where(et => et.AppId == appId);

        if (!includeArchived)
            query = query.Where(et => !et.IsArchived);

        return await query
            .OrderByDescending(et => et.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);
    }

    public async Task<int> CountByAppIdAsync(Guid appId, bool includeArchived, CancellationToken ct = default)
    {
        var query = _dbContext.EventTypes.Where(et => et.AppId == appId);

        if (!includeArchived)
            query = query.Where(et => !et.IsArchived);

        return await query.CountAsync(ct);
    }

    public async Task<EventType> CreateAsync(EventType eventType, CancellationToken ct = default)
    {
        _dbContext.EventTypes.Add(eventType);
        await _dbContext.SaveChangesAsync(ct);
        return eventType;
    }

    public async Task UpdateAsync(EventType eventType, CancellationToken ct = default)
    {
        _dbContext.EventTypes.Update(eventType);
        await _dbContext.SaveChangesAsync(ct);
    }

    public async Task ArchiveAsync(Guid id, CancellationToken ct = default)
    {
        await _dbContext.EventTypes
            .Where(et => et.Id == id)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(et => et.IsArchived, true), ct);
    }

    public async Task ArchiveAsync(Guid appId, Guid id, CancellationToken ct = default)
    {
        await _dbContext.EventTypes
            .Where(et => et.AppId == appId && et.Id == id)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(et => et.IsArchived, true), ct);
    }
}

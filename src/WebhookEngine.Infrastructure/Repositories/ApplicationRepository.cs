using Microsoft.EntityFrameworkCore;
using WebhookEngine.Core.Entities;
using WebhookEngine.Infrastructure.Data;

namespace WebhookEngine.Infrastructure.Repositories;

public class ApplicationRepository
{
    private readonly WebhookDbContext _dbContext;

    public ApplicationRepository(WebhookDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<Application?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        return await _dbContext.Applications.FirstOrDefaultAsync(a => a.Id == id, ct);
    }

    public async Task<Application?> GetByApiKeyPrefixAsync(string prefix, CancellationToken ct = default)
    {
        return await _dbContext.Applications.FirstOrDefaultAsync(a => a.ApiKeyPrefix == prefix, ct);
    }

    public async Task<List<Application>> ListAsync(int page, int pageSize, CancellationToken ct = default)
    {
        return await _dbContext.Applications
            .AsNoTracking()
            .OrderByDescending(a => a.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);
    }

    public async Task<int> CountAsync(CancellationToken ct = default)
    {
        return await _dbContext.Applications.CountAsync(ct);
    }

    public async Task<Application> CreateAsync(Application application, CancellationToken ct = default)
    {
        _dbContext.Applications.Add(application);
        await _dbContext.SaveChangesAsync(ct);
        return application;
    }

    public async Task UpdateAsync(Application application, CancellationToken ct = default)
    {
        application.UpdatedAt = DateTime.UtcNow;
        _dbContext.Applications.Update(application);
        await _dbContext.SaveChangesAsync(ct);
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        await _dbContext.Applications.Where(a => a.Id == id).ExecuteDeleteAsync(ct);
    }
}

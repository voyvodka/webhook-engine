using Microsoft.EntityFrameworkCore;
using WebhookEngine.Core.Entities;
using WebhookEngine.Infrastructure.Data;

namespace WebhookEngine.Infrastructure.Repositories;

public class DashboardUserRepository
{
    private readonly WebhookDbContext _dbContext;

    public DashboardUserRepository(WebhookDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<DashboardUser?> GetByEmailAsync(string email, CancellationToken ct = default)
    {
        return await _dbContext.DashboardUsers
            .FirstOrDefaultAsync(u => u.Email == email, ct);
    }

    public async Task<DashboardUser?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        return await _dbContext.DashboardUsers
            .FirstOrDefaultAsync(u => u.Id == id, ct);
    }

    public async Task<DashboardUser> CreateAsync(DashboardUser user, CancellationToken ct = default)
    {
        _dbContext.DashboardUsers.Add(user);
        await _dbContext.SaveChangesAsync(ct);
        return user;
    }

    public async Task UpdateLastLoginAsync(Guid id, CancellationToken ct = default)
    {
        await _dbContext.DashboardUsers
            .Where(u => u.Id == id)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(u => u.LastLoginAt, DateTime.UtcNow), ct);
    }
}

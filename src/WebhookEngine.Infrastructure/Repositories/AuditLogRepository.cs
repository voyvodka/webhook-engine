using Microsoft.EntityFrameworkCore;
using WebhookEngine.Core.Entities;
using WebhookEngine.Infrastructure.Data;

namespace WebhookEngine.Infrastructure.Repositories;

public class AuditLogRepository
{
    private readonly WebhookDbContext _dbContext;

    public AuditLogRepository(WebhookDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<(List<AuditLog> Rows, int Total)> ListAsync(
        Guid? appId,
        string? action,
        string? resourceType,
        Guid? resourceId,
        DateTime? from,
        DateTime? to,
        int page,
        int pageSize,
        CancellationToken ct = default)
    {
        var query = _dbContext.AuditLogs.AsNoTracking().AsQueryable();

        if (appId.HasValue) query = query.Where(l => l.AppId == appId);
        if (!string.IsNullOrWhiteSpace(action)) query = query.Where(l => l.Action == action);
        if (!string.IsNullOrWhiteSpace(resourceType)) query = query.Where(l => l.ResourceType == resourceType);
        if (resourceId.HasValue) query = query.Where(l => l.ResourceId == resourceId);
        if (from.HasValue) query = query.Where(l => l.CreatedAt >= from);
        if (to.HasValue) query = query.Where(l => l.CreatedAt <= to);

        var total = await query.CountAsync(ct);
        var rows = await query
            .OrderByDescending(l => l.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        return (rows, total);
    }
}

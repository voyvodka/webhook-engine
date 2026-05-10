using System.Text.Json;
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
        return await _dbContext.Applications
            .AsNoTracking()
            .FirstOrDefaultAsync(a => a.ApiKeyPrefix == prefix, ct);
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

    /// <summary>
    /// Returns true when at least one portal-enabled application lists the
    /// given origin in its <c>AllowedPortalOriginsJson</c>. Used by the
    /// portal CORS layer for OPTIONS preflight, before a token is available.
    ///
    /// Filtering is performed in-process: the candidate set is the apps with
    /// portal enabled (PortalSigningKey + AllowedPortalOriginsJson both set),
    /// which is bounded and small. Doing the JSON containment in C# avoids
    /// coupling to a provider-specific JSON translator (Npgsql vs InMemory)
    /// and exact-string matching in the deserialized array is more strict
    /// than a substring-style <c>JsonContains("\"origin\"")</c> probe.
    /// </summary>
    public async Task<bool> AnyAllowsPortalOriginAsync(string origin, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(origin))
        {
            return false;
        }

        var candidates = await _dbContext.Applications
            .AsNoTracking()
            .Where(a => a.PortalSigningKey != null && a.AllowedPortalOriginsJson != null)
            .Select(a => a.AllowedPortalOriginsJson!)
            .ToListAsync(ct);

        foreach (var json in candidates)
        {
            string[]? origins;
            try
            {
                origins = JsonSerializer.Deserialize<string[]>(json);
            }
            catch (JsonException)
            {
                continue;
            }

            // RFC 6454 §4 — scheme + host are case-insensitive. Match the
            // ordinal-insensitive comparison used at request-time in
            // PortalCorsMiddleware so preflight + real-request decisions agree.
            if (origins is not null &&
                origins.Any(o => string.Equals(o, origin, StringComparison.OrdinalIgnoreCase)))
            {
                return true;
            }
        }

        return false;
    }
}

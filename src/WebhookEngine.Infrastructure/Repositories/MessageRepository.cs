using Microsoft.EntityFrameworkCore;
using WebhookEngine.Core.Entities;
using WebhookEngine.Core.Enums;
using WebhookEngine.Infrastructure.Data;

namespace WebhookEngine.Infrastructure.Repositories;

public class MessageRepository
{
    private readonly WebhookDbContext _dbContext;

    public MessageRepository(WebhookDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<Message?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        return await _dbContext.Messages
            .Include(m => m.Endpoint)
            .Include(m => m.EventType)
            .FirstOrDefaultAsync(m => m.Id == id, ct);
    }

    public async Task<Message?> GetByIdAsync(Guid appId, Guid id, CancellationToken ct = default)
    {
        return await _dbContext.Messages
            .Include(m => m.Endpoint)
            .Include(m => m.EventType)
            .FirstOrDefaultAsync(m => m.AppId == appId && m.Id == id, ct);
    }

    public async Task<List<Message>> ListAsync(
        Guid appId,
        MessageStatus? status,
        Guid? endpointId,
        Guid? eventTypeId,
        DateTime? after,
        DateTime? before,
        int page,
        int pageSize,
        CancellationToken ct = default)
    {
        var query = _dbContext.Messages
            .AsNoTracking()
            .Where(m => m.AppId == appId);

        if (status.HasValue)
            query = query.Where(m => m.Status == status.Value);

        if (endpointId.HasValue)
            query = query.Where(m => m.EndpointId == endpointId.Value);

        if (eventTypeId.HasValue)
            query = query.Where(m => m.EventTypeId == eventTypeId.Value);

        if (after.HasValue)
            query = query.Where(m => m.CreatedAt >= after.Value);

        if (before.HasValue)
            query = query.Where(m => m.CreatedAt <= before.Value);

        return await query
            .OrderByDescending(m => m.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);
    }

    public async Task<List<Message>> ListByIdempotencyKeyAsync(
        Guid appId,
        string idempotencyKey,
        DateTime after,
        CancellationToken ct = default)
    {
        return await _dbContext.Messages
            .AsNoTracking()
            .Where(m => m.AppId == appId
                && m.IdempotencyKey == idempotencyKey
                && m.CreatedAt >= after)
            .OrderBy(m => m.CreatedAt)
            .ToListAsync(ct);
    }

    public async Task<List<MessageAttempt>> ListAttemptsAsync(Guid messageId, int page, int pageSize, CancellationToken ct = default)
    {
        return await _dbContext.MessageAttempts
            .AsNoTracking()
            .Where(a => a.MessageId == messageId)
            .OrderBy(a => a.AttemptNumber)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);
    }

    public async Task<List<MessageAttempt>> ListAttemptsAsync(Guid appId, Guid messageId, int page, int pageSize, CancellationToken ct = default)
    {
        return await _dbContext.MessageAttempts
            .AsNoTracking()
            .Where(a => a.MessageId == messageId && a.Message.AppId == appId)
            .OrderBy(a => a.AttemptNumber)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);
    }

    public async Task CreateAttemptAsync(MessageAttempt attempt, CancellationToken ct = default)
    {
        _dbContext.MessageAttempts.Add(attempt);
        await _dbContext.SaveChangesAsync(ct);
    }

    public async Task UpdateMessageStatusAsync(Guid messageId, MessageStatus status, CancellationToken ct = default)
    {
        await _dbContext.Messages
            .Where(m => m.Id == messageId)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(m => m.Status, status)
                .SetProperty(m => m.LockedAt, (DateTime?)null)
                .SetProperty(m => m.LockedBy, (string?)null)
                .SetProperty(m => m.DeliveredAt, status == MessageStatus.Delivered ? DateTime.UtcNow : (DateTime?)null), ct);
    }

    public async Task MarkDeliveredAsync(Guid messageId, int attemptCount, CancellationToken ct = default)
    {
        await _dbContext.Messages
            .Where(m => m.Id == messageId)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(m => m.Status, MessageStatus.Delivered)
                .SetProperty(m => m.AttemptCount, attemptCount)
                .SetProperty(m => m.DeliveredAt, DateTime.UtcNow)
                .SetProperty(m => m.LockedAt, (DateTime?)null)
                .SetProperty(m => m.LockedBy, (string?)null), ct);
    }

    public async Task MarkFailedForRetryAsync(
        Guid messageId,
        int attemptCount,
        DateTime nextScheduledAt,
        CancellationToken ct = default)
    {
        await _dbContext.Messages
            .Where(m => m.Id == messageId)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(m => m.Status, MessageStatus.Failed)
                .SetProperty(m => m.AttemptCount, attemptCount)
                .SetProperty(m => m.ScheduledAt, nextScheduledAt)
                .SetProperty(m => m.DeliveredAt, (DateTime?)null)
                .SetProperty(m => m.LockedAt, (DateTime?)null)
                .SetProperty(m => m.LockedBy, (string?)null), ct);
    }

    public async Task MarkDeadLetterAsync(Guid messageId, int attemptCount, CancellationToken ct = default)
    {
        await _dbContext.Messages
            .Where(m => m.Id == messageId)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(m => m.Status, MessageStatus.DeadLetter)
                .SetProperty(m => m.AttemptCount, attemptCount)
                .SetProperty(m => m.DeliveredAt, (DateTime?)null)
                .SetProperty(m => m.LockedAt, (DateTime?)null)
                .SetProperty(m => m.LockedBy, (string?)null), ct);
    }

    public async Task ReschedulePendingAsync(Guid messageId, DateTime scheduledAt, CancellationToken ct = default)
    {
        await _dbContext.Messages
            .Where(m => m.Id == messageId)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(m => m.Status, MessageStatus.Pending)
                .SetProperty(m => m.ScheduledAt, scheduledAt)
                .SetProperty(m => m.LockedAt, (DateTime?)null)
                .SetProperty(m => m.LockedBy, (string?)null), ct);
    }

    public async Task RetryAsync(Guid messageId, CancellationToken ct = default)
    {
        await _dbContext.Messages
            .Where(m => m.Id == messageId)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(m => m.Status, MessageStatus.Pending)
                .SetProperty(m => m.AttemptCount, 0)
                .SetProperty(m => m.ScheduledAt, DateTime.UtcNow)
                .SetProperty(m => m.DeliveredAt, (DateTime?)null)
                .SetProperty(m => m.LockedAt, (DateTime?)null)
                .SetProperty(m => m.LockedBy, (string?)null), ct);
    }

    public async Task<int> RequeueDueFailedMessagesAsync(DateTime now, CancellationToken ct = default)
    {
        return await _dbContext.Messages
            .Where(m => m.Status == MessageStatus.Failed && m.AttemptCount < m.MaxRetries && m.ScheduledAt <= now)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(m => m.Status, MessageStatus.Pending)
                .SetProperty(m => m.LockedAt, (DateTime?)null)
                .SetProperty(m => m.LockedBy, (string?)null), ct);
    }

    /// <summary>
    /// Cross-app list for dashboard admin — returns messages across all applications with optional filters.
    /// </summary>
    public async Task<List<Message>> ListAllAsync(
        Guid? appId,
        MessageStatus? status,
        Guid? endpointId,
        string? eventType,
        DateTime? after,
        DateTime? before,
        int page,
        int pageSize,
        CancellationToken ct = default)
    {
        var query = _dbContext.Messages
            .AsNoTracking()
            .Include(m => m.Endpoint)
            .Include(m => m.EventType)
            .AsQueryable();

        if (appId.HasValue)
            query = query.Where(m => m.AppId == appId.Value);

        if (status.HasValue)
            query = query.Where(m => m.Status == status.Value);

        if (endpointId.HasValue)
            query = query.Where(m => m.EndpointId == endpointId.Value);

        if (!string.IsNullOrWhiteSpace(eventType))
            query = query.Where(m => m.EventType.Name == eventType);

        if (after.HasValue)
            query = query.Where(m => m.CreatedAt >= after.Value);

        if (before.HasValue)
            query = query.Where(m => m.CreatedAt <= before.Value);

        return await query
            .OrderByDescending(m => m.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);
    }

    public async Task<int> CountAllAsync(
        Guid? appId,
        MessageStatus? status,
        Guid? endpointId,
        string? eventType,
        DateTime? after,
        DateTime? before,
        CancellationToken ct = default)
    {
        var query = _dbContext.Messages.AsQueryable();

        if (appId.HasValue)
            query = query.Where(m => m.AppId == appId.Value);

        if (status.HasValue)
            query = query.Where(m => m.Status == status.Value);

        if (endpointId.HasValue)
            query = query.Where(m => m.EndpointId == endpointId.Value);

        if (!string.IsNullOrWhiteSpace(eventType))
            query = query.Where(m => m.EventType.Name == eventType);

        if (after.HasValue)
            query = query.Where(m => m.CreatedAt >= after.Value);

        if (before.HasValue)
            query = query.Where(m => m.CreatedAt <= before.Value);

        return await query.CountAsync(ct);
    }

    /// <summary>
    /// Get a single message by ID without app scoping — for dashboard admin use.
    /// </summary>
    public async Task<Message?> GetByIdWithAttemptsAsync(Guid messageId, CancellationToken ct = default)
    {
        return await _dbContext.Messages
            .Include(m => m.Endpoint)
            .Include(m => m.EventType)
            .Include(m => m.Attempts.OrderBy(a => a.AttemptNumber))
            .FirstOrDefaultAsync(m => m.Id == messageId, ct);
    }
}

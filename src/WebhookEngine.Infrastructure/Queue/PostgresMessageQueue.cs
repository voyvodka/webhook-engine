using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using WebhookEngine.Core.Entities;
using WebhookEngine.Core.Enums;
using WebhookEngine.Core.Interfaces;
using WebhookEngine.Core.Metrics;
using WebhookEngine.Core.Options;
using WebhookEngine.Infrastructure.Data;

namespace WebhookEngine.Infrastructure.Queue;

public class PostgresMessageQueue : IMessageQueue
{
    private readonly WebhookDbContext _dbContext;
    private readonly RetryPolicyOptions _retryPolicy;
    private readonly WebhookMetrics? _metrics;

    public PostgresMessageQueue(
        WebhookDbContext dbContext,
        IOptions<RetryPolicyOptions>? retryPolicy = null,
        WebhookMetrics? metrics = null)
    {
        _dbContext = dbContext;
        _retryPolicy = retryPolicy?.Value ?? new RetryPolicyOptions();
        _metrics = metrics;
    }

    public async Task<IReadOnlyList<Message>> DequeueAsync(int batchSize, string workerId, CancellationToken ct = default)
    {
        // CORR-04: FOR UPDATE SKIP LOCKED is a transaction-scoped lock (crash → rollback → auto-release);
        // raw SQL is required for SKIP LOCKED. Circuit-open rows are dequeued too so they can dead-letter, not stall in Pending.
        var sql = """
            WITH next_batch AS (
                SELECT m.id
                FROM messages m
                WHERE m.status = 'Pending'
                  AND m.scheduled_at <= NOW()
                ORDER BY m.scheduled_at ASC
                LIMIT {0}
                FOR UPDATE OF m SKIP LOCKED
            )
            UPDATE messages m
            SET status = 'Sending',
                locked_at = NOW(),
                locked_by = {1}
            FROM next_batch
            WHERE m.id = next_batch.id
            RETURNING m.*;
            """;

        var messages = await _dbContext.Messages
            .FromSqlRaw(sql, batchSize, workerId)
            .AsNoTracking()
            .ToListAsync(ct);

        return messages;
    }

    public async Task EnqueueAsync(Message message, CancellationToken ct = default)
    {
        message.Status = MessageStatus.Pending;
        // The one chokepoint every message passes through, so the configured cap wins here;
        // the entity default (7) is only the fallback when RetryPolicyOptions is unbound.
        message.MaxRetries = _retryPolicy.MaxRetries;
        _dbContext.Messages.Add(message);
        try
        {
            await _dbContext.SaveChangesAsync(ct);
        }
        catch
        {
            // Scoped context is reused across an enqueue loop; a still-Added entity would
            // re-flush and re-throw on the next SaveChanges, silently losing that sibling.
            _dbContext.Entry(message).State = EntityState.Detached;
            throw;
        }
        _metrics?.RecordMessageEnqueued();
    }

    public async Task<int> ReleaseStaleLocksAsync(TimeSpan staleDuration, CancellationToken ct = default)
    {
        var cutoff = DateTime.UtcNow - staleDuration;

        return await _dbContext.Messages
            .Where(m => m.Status == MessageStatus.Sending && m.LockedAt != null && m.LockedAt < cutoff)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(m => m.Status, MessageStatus.Pending)
                .SetProperty(m => m.LockedAt, (DateTime?)null)
                .SetProperty(m => m.LockedBy, (string?)null), ct);
    }
}

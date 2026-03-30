using Microsoft.EntityFrameworkCore;
using WebhookEngine.Core.Entities;
using WebhookEngine.Core.Enums;
using WebhookEngine.Core.Interfaces;
using WebhookEngine.Core.Metrics;
using WebhookEngine.Infrastructure.Data;

namespace WebhookEngine.Infrastructure.Queue;

public class PostgresMessageQueue : IMessageQueue
{
    private readonly WebhookDbContext _dbContext;
    private readonly WebhookMetrics? _metrics;

    public PostgresMessageQueue(WebhookDbContext dbContext, WebhookMetrics? metrics = null)
    {
        _dbContext = dbContext;
        _metrics = metrics;
    }

    public async Task<IReadOnlyList<Message>> DequeueAsync(int batchSize, string workerId, CancellationToken ct = default)
    {
        // CORR-04: Transaction-scoped row lock via FOR UPDATE SKIP LOCKED.
        // Worker crash → transaction rollback → lock auto-released by PostgreSQL.
        // StaleLockRecoveryWorker handles residual edge cases (D-08).
        // Use raw SQL for SKIP LOCKED — critical for queue performance
        // Skip messages for endpoints with open circuits to avoid dequeue-reschedule churn
        var sql = """
            WITH next_batch AS (
                SELECT m.id
                FROM messages m
                LEFT JOIN endpoint_health eh ON eh.endpoint_id = m.endpoint_id
                WHERE m.status = 'Pending'
                  AND m.scheduled_at <= NOW()
                  AND (eh.circuit_state IS NULL OR eh.circuit_state <> 'Open')
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
        _dbContext.Messages.Add(message);
        await _dbContext.SaveChangesAsync(ct);
        _metrics?.RecordMessageEnqueued();
        _metrics?.RecordQueueEnqueue();
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

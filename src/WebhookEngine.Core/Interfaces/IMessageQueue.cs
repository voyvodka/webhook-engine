using WebhookEngine.Core.Entities;

namespace WebhookEngine.Core.Interfaces;

public interface IMessageQueue
{
    Task<IReadOnlyList<Message>> DequeueAsync(int batchSize, string workerId, CancellationToken ct = default);
    Task EnqueueAsync(Message message, CancellationToken ct = default);
    Task<int> ReleaseStaleLocksAsync(TimeSpan staleDuration, CancellationToken ct = default);
}

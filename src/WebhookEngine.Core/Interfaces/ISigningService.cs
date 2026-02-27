using WebhookEngine.Core.Models;

namespace WebhookEngine.Core.Interfaces;

public interface ISigningService
{
    SignedHeaders Sign(string messageId, long timestamp, string body, string secret);
}

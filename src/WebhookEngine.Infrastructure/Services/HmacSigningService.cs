using System.Security.Cryptography;
using System.Text;
using WebhookEngine.Core.Interfaces;
using WebhookEngine.Core.Models;

namespace WebhookEngine.Infrastructure.Services;

public class HmacSigningService : ISigningService
{
    public SignedHeaders Sign(string messageId, long timestamp, string body, string secret)
    {
        var payload = $"{messageId}.{timestamp}.{body}";
        var secretBytes = ResolveSecretBytes(secret);

        using var hmac = new HMACSHA256(secretBytes);
        var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(payload));
        var signature = Convert.ToBase64String(hash);

        return new SignedHeaders
        {
            WebhookId = messageId,
            WebhookTimestamp = timestamp.ToString(),
            WebhookSignature = $"v1,{signature}"
        };
    }

    private static byte[] ResolveSecretBytes(string secret)
    {
        if (string.IsNullOrWhiteSpace(secret))
        {
            throw new InvalidOperationException("Signing secret is missing.");
        }

        // Backward compatibility: support old whsec_* format and raw secrets.
        if (secret.StartsWith("whsec_", StringComparison.OrdinalIgnoreCase))
        {
            return Encoding.UTF8.GetBytes(secret);
        }

        try
        {
            return Convert.FromBase64String(secret);
        }
        catch (FormatException)
        {
            return Encoding.UTF8.GetBytes(secret);
        }
    }
}

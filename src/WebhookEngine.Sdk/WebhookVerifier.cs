using System.Security.Cryptography;
using System.Text;

namespace WebhookEngine.Sdk;

/// <summary>
/// Verifies WebhookEngine webhook signatures on the receiver side. Standard
/// Webhooks compatible: checks the <c>webhook-id</c>, <c>webhook-timestamp</c>,
/// and <c>webhook-signature</c> triple, applies a configurable timestamp
/// tolerance (5 min default), and uses constant-time comparison.
/// </summary>
public static class WebhookVerifier
{
    private static readonly TimeSpan DefaultTolerance = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Returns true when the signature is valid AND the timestamp is within
    /// tolerance. Returns false on any other input — invalid timestamp, drift
    /// outside tolerance, signature mismatch, missing fields.
    /// </summary>
    /// <param name="webhookId">Value of the <c>webhook-id</c> header.</param>
    /// <param name="webhookTimestamp">Value of the <c>webhook-timestamp</c> header (Unix seconds).</param>
    /// <param name="webhookSignature">Value of the <c>webhook-signature</c> header (e.g. <c>"v1,base64..."</c>; multiple signatures separated by spaces are checked).</param>
    /// <param name="body">The raw request body as a string.</param>
    /// <param name="secret">Signing secret for the application or endpoint.</param>
    /// <param name="tolerance">Maximum allowed drift between the timestamp and now. Defaults to 5 minutes.</param>
    public static bool Verify(
        string webhookId,
        string webhookTimestamp,
        string webhookSignature,
        string body,
        string secret,
        TimeSpan? tolerance = null)
    {
        if (string.IsNullOrEmpty(webhookId) ||
            string.IsNullOrEmpty(webhookTimestamp) ||
            string.IsNullOrEmpty(webhookSignature) ||
            string.IsNullOrEmpty(secret))
        {
            return false;
        }

        if (!long.TryParse(webhookTimestamp, out var ts))
        {
            return false;
        }

        var messageTime = DateTimeOffset.FromUnixTimeSeconds(ts);
        var drift = DateTimeOffset.UtcNow - messageTime;
        var maxDrift = tolerance ?? DefaultTolerance;
        if (drift.Duration() > maxDrift)
        {
            return false;
        }

        var signedContent = $"{webhookId}.{webhookTimestamp}.{body}";
        var secretBytes = ResolveSecretBytes(secret);

        using var hmac = new HMACSHA256(secretBytes);
        var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(signedContent));
        var expectedSignature = $"v1,{Convert.ToBase64String(hash)}";
        var expectedBytes = Encoding.UTF8.GetBytes(expectedSignature);

        foreach (var sig in webhookSignature.Split(' '))
        {
            var actual = Encoding.UTF8.GetBytes(sig.Trim());
            if (CryptographicOperations.FixedTimeEquals(actual, expectedBytes))
            {
                return true;
            }
        }

        return false;
    }

    private static byte[] ResolveSecretBytes(string secret)
    {
        // Standard Webhooks "whsec_" prefix → bytes are the literal UTF-8.
        if (secret.StartsWith("whsec_", StringComparison.OrdinalIgnoreCase))
            return Encoding.UTF8.GetBytes(secret);

        // Otherwise try base64 first (the engine generates base64 secrets).
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

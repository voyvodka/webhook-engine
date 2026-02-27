// ---------------------------------------------------------------
// WebhookEngine — Signature Verification Helper (C#)
// ---------------------------------------------------------------
// Copy this file into your project to verify webhook signatures.
// No dependencies required beyond the .NET BCL.
//
// Usage:
//   var isValid = WebhookVerifier.Verify(
//       webhookId:        request.Headers["webhook-id"],
//       webhookTimestamp:  request.Headers["webhook-timestamp"],
//       webhookSignature:  request.Headers["webhook-signature"],
//       body:              await new StreamReader(request.Body).ReadToEndAsync(),
//       secret:            "your-signing-secret"
//   );
// ---------------------------------------------------------------

using System.Security.Cryptography;
using System.Text;

public static class WebhookVerifier
{
    /// <summary>
    /// Default tolerance for timestamp verification (5 minutes).
    /// </summary>
    private static readonly TimeSpan DefaultTolerance = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Verifies a WebhookEngine webhook signature.
    /// Returns true if the signature is valid and the timestamp is within tolerance.
    /// </summary>
    /// <param name="webhookId">Value of the 'webhook-id' header.</param>
    /// <param name="webhookTimestamp">Value of the 'webhook-timestamp' header (Unix seconds).</param>
    /// <param name="webhookSignature">Value of the 'webhook-signature' header (e.g. "v1,base64...").</param>
    /// <param name="body">The raw request body as a string.</param>
    /// <param name="secret">The signing secret for the application/endpoint.</param>
    /// <param name="tolerance">Optional timestamp tolerance. Defaults to 5 minutes.</param>
    /// <returns>True if the signature is valid; false otherwise.</returns>
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

        // Check timestamp tolerance
        if (long.TryParse(webhookTimestamp, out var ts))
        {
            var messageTime = DateTimeOffset.FromUnixTimeSeconds(ts);
            var drift = DateTimeOffset.UtcNow - messageTime;
            var maxDrift = tolerance ?? DefaultTolerance;

            if (drift.Duration() > maxDrift)
                return false;
        }
        else
        {
            return false; // Invalid timestamp
        }

        // Compute expected signature
        var signedContent = $"{webhookId}.{webhookTimestamp}.{body}";
        var secretBytes = ResolveSecretBytes(secret);

        using var hmac = new HMACSHA256(secretBytes);
        var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(signedContent));
        var expectedSignature = $"v1,{Convert.ToBase64String(hash)}";

        // The header may contain multiple signatures separated by spaces
        var signatures = webhookSignature.Split(' ');
        foreach (var sig in signatures)
        {
            // Use constant-time comparison to prevent timing attacks
            if (CryptographicOperations.FixedTimeEquals(
                    Encoding.UTF8.GetBytes(sig.Trim()),
                    Encoding.UTF8.GetBytes(expectedSignature)))
            {
                return true;
            }
        }

        return false;
    }

    private static byte[] ResolveSecretBytes(string secret)
    {
        // Support whsec_ prefix (Standard Webhooks format)
        if (secret.StartsWith("whsec_", StringComparison.OrdinalIgnoreCase))
            return Encoding.UTF8.GetBytes(secret);

        // Try base64 decoding first
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

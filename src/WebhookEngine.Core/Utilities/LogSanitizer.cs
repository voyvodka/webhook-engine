namespace WebhookEngine.Core.Utilities;

/// <summary>
/// CodeQL <c>cs/log-forging</c> + <c>cs/exposure-of-sensitive-information</c>
/// helpers. Lives in Core so API and Infrastructure can both consume it.
/// </summary>
public static class LogSanitizer
{
    private const int DefaultMaxLength = 256;

    /// <summary>
    /// Strip CR/LF/tab characters and clamp length so a malicious request
    /// path, header, or expression cannot break out of the current log line.
    /// </summary>
    public static string ForLog(string? value, int maxLength = DefaultMaxLength)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        var trimmed = value.Length > maxLength ? value[..maxLength] : value;
        return trimmed
            .Replace("\r", string.Empty)
            .Replace("\n", string.Empty)
            .Replace("\t", " ");
    }

    /// <summary>
    /// Redact the local part of an email address while keeping the domain
    /// and the leading character. Returns <c>"&lt;redacted&gt;"</c> when the
    /// input is empty or not in a recognisable email shape.
    /// </summary>
    public static string RedactEmail(string? email)
    {
        if (string.IsNullOrWhiteSpace(email))
        {
            return "<empty>";
        }

        var atIndex = email.IndexOf('@');
        if (atIndex <= 0 || atIndex == email.Length - 1)
        {
            return "<redacted>";
        }

        var firstChar = email[0];
        var domain = email[atIndex..];
        return $"{firstChar}***{domain}";
    }
}

namespace WebhookEngine.Core.Utilities;

/// <summary>
/// Helpers for safely emitting user-controlled values into log entries.
///
/// <para>
/// <b>Log forging</b> (CodeQL <c>cs/log-forging</c>): an attacker can put
/// <c>\r\n</c> sequences into request paths, headers, message bodies, or
/// dashboard-supplied configuration (e.g. JMESPath transform expressions)
/// to inject fake log lines. <see cref="ForLog"/> strips control characters
/// and caps length so the output is single-line and bounded.
/// </para>
/// <para>
/// <b>PII exposure</b> (CodeQL <c>cs/exposure-of-sensitive-information</c>):
/// some structured logs include identifiers like email addresses that
/// shouldn't appear verbatim in shared logs. <see cref="RedactEmail"/> keeps
/// just enough of the local part for an operator to recognise the user.
/// </para>
///
/// Lives in <c>Core</c> so both the API host and the Infrastructure layer
/// (workers, services) can reuse it without taking a project reference back
/// up the dependency graph.
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

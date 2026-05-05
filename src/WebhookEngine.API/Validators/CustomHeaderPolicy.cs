namespace WebhookEngine.API.Validators;

/// <summary>
/// Reject custom headers that would override engine-set headers (signature
/// triple, content-type, etc.) or smuggle credentials onto outbound webhook
/// requests. Names are matched case-insensitively. Also strips CR/LF from
/// values to defeat header injection on stacks that don't validate.
/// </summary>
public static class CustomHeaderPolicy
{
    private static readonly HashSet<string> Reserved = new(StringComparer.OrdinalIgnoreCase)
    {
        "Authorization",
        "Cookie",
        "Set-Cookie",
        "Host",
        "Content-Length",
        "Content-Type",
        "Content-Encoding",
        "Transfer-Encoding",
        "User-Agent",
        "webhook-id",
        "webhook-timestamp",
        "webhook-signature"
    };

    public static string? Validate(IDictionary<string, string>? headers)
    {
        if (headers is null || headers.Count == 0)
        {
            return null;
        }

        foreach (var (name, value) in headers)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                return "Custom header names must be non-empty.";
            }

            if (Reserved.Contains(name))
            {
                return $"Custom header '{name}' is reserved and cannot be overridden.";
            }

            if (value is not null && (value.Contains('\r') || value.Contains('\n')))
            {
                return $"Custom header '{name}' value contains CR/LF characters.";
            }

            if (name.Length > 128 || (value?.Length ?? 0) > 1024)
            {
                return $"Custom header '{name}' exceeds size limits (name <= 128, value <= 1024).";
            }
        }

        return null;
    }
}

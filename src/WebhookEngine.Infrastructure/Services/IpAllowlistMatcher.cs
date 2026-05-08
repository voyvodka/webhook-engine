using System.Net;
using System.Text.Json;

namespace WebhookEngine.Infrastructure.Services;

/// <summary>
/// Static helpers for parsing the per-endpoint <c>AllowedIpsJson</c> column
/// and checking whether a resolved IP belongs to one of the listed CIDR
/// ranges. Pure functions; the matcher is intentionally not a registered
/// service so callers don't have to inject anything just to evaluate a
/// list literal.
/// </summary>
public static class IpAllowlistMatcher
{
    /// <summary>
    /// Parses the JSON array stored in <c>Endpoint.AllowedIpsJson</c>. Empty,
    /// null, or non-array JSON returns an empty list — the caller treats that
    /// as "no allowlist configured."
    /// </summary>
    public static IReadOnlyList<IPNetwork> Parse(string? allowedIpsJson)
    {
        if (string.IsNullOrWhiteSpace(allowedIpsJson))
        {
            return [];
        }

        try
        {
            var raw = JsonSerializer.Deserialize<string[]>(allowedIpsJson);
            if (raw is null || raw.Length == 0)
            {
                return [];
            }

            var networks = new List<IPNetwork>(raw.Length);
            foreach (var cidr in raw)
            {
                if (TryParseCidr(cidr, out var network))
                {
                    networks.Add(network);
                }
            }

            return networks;
        }
        catch (JsonException)
        {
            // Stored JSON drifted from the validated shape (e.g. someone
            // edited the row by hand). Treat as no allowlist rather than
            // failing every delivery.
            return [];
        }
    }

    /// <summary>
    /// Returns true when <paramref name="cidr"/> can be parsed by
    /// <see cref="IPNetwork.TryParse(string, out IPNetwork)"/>. Used by the
    /// request validators to reject malformed entries up front.
    /// </summary>
    public static bool TryParseCidr(string? cidr, out IPNetwork network)
    {
        if (string.IsNullOrWhiteSpace(cidr))
        {
            network = default;
            return false;
        }

        return IPNetwork.TryParse(cidr.Trim(), out network);
    }

    /// <summary>
    /// True when every address in <paramref name="resolvedAddresses"/> is
    /// contained by at least one entry in <paramref name="allowed"/>. An
    /// empty allowlist returns true (the gate is opt-in) regardless of the
    /// resolved set — the empty-allowlist check fires first so the
    /// "allowlist not configured" path can't accidentally fail because no
    /// addresses came back from the resolver. An empty resolution against a
    /// configured allowlist returns false (we will not deliver to nothing).
    /// </summary>
    public static bool AllAddressesAllowed(IReadOnlyList<IPNetwork> allowed, IReadOnlyList<IPAddress> resolvedAddresses)
    {
        // Order is intentional: empty allowlist short-circuits FIRST so a
        // future caller passing an empty resolved set with no allowlist
        // configured cannot accidentally land on the deny branch below.
        if (allowed.Count == 0)
        {
            return true;
        }

        if (resolvedAddresses.Count == 0)
        {
            return false;
        }

        foreach (var address in resolvedAddresses)
        {
            var matched = false;
            foreach (var network in allowed)
            {
                if (network.Contains(address))
                {
                    matched = true;
                    break;
                }
            }
            if (!matched) return false;
        }

        return true;
    }
}

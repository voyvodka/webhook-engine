using System.Net;
using System.Net.Sockets;

namespace WebhookEngine.Infrastructure.Services;

/// <summary>
/// Classifies an <see cref="IPAddress"/> into safe (publicly routable) vs.
/// dangerous (loopback, link-local, private RFC1918, CGNAT, unique-local,
/// cloud-metadata) buckets. Used by the SSRF guard at both validate and
/// connect time.
/// </summary>
public static class PrivateIpDetector
{
    /// <summary>
    /// Returns a non-null reason string if the address should be blocked.
    /// Returns null if the address is acceptable.
    /// </summary>
    /// <param name="address">The resolved IP address.</param>
    /// <param name="allowLoopback">When true, loopback (127.0.0.0/8, ::1)
    /// is permitted — only set this in Development where the dev compose
    /// receiver legitimately lives on loopback.</param>
    public static string? Classify(IPAddress address, bool allowLoopback)
    {
        if (IPAddress.IsLoopback(address))
        {
            return allowLoopback ? null : "loopback address (127.0.0.0/8 or ::1)";
        }

        if (address.AddressFamily == AddressFamily.InterNetwork)
        {
            var bytes = address.GetAddressBytes();

            // 0.0.0.0/8 — "this network"
            if (bytes[0] == 0)
                return "unspecified address (0.0.0.0/8)";

            // 10.0.0.0/8 — RFC1918 private
            if (bytes[0] == 10)
                return "private network (10.0.0.0/8)";

            // 172.16.0.0/12 — RFC1918 private
            if (bytes[0] == 172 && bytes[1] >= 16 && bytes[1] <= 31)
                return "private network (172.16.0.0/12)";

            // 192.168.0.0/16 — RFC1918 private
            if (bytes[0] == 192 && bytes[1] == 168)
                return "private network (192.168.0.0/16)";

            // 100.64.0.0/10 — CGNAT
            if (bytes[0] == 100 && bytes[1] >= 64 && bytes[1] <= 127)
                return "carrier-grade NAT (100.64.0.0/10)";

            // 169.254.0.0/16 — link-local; covers cloud metadata
            // (169.254.169.254 is AWS/GCP/Azure metadata service)
            if (bytes[0] == 169 && bytes[1] == 254)
                return "link-local / cloud metadata (169.254.0.0/16)";

            // 224.0.0.0/4 — multicast
            if (bytes[0] >= 224 && bytes[0] <= 239)
                return "multicast (224.0.0.0/4)";

            // 240.0.0.0/4 — reserved
            if (bytes[0] >= 240)
                return "reserved (240.0.0.0/4)";
        }
        else if (address.AddressFamily == AddressFamily.InterNetworkV6)
        {
            var v6 = address.GetAddressBytes();

            // ::/128 — unspecified
            if (v6.All(b => b == 0))
                return "unspecified IPv6";

            // ::1 — loopback (already handled by IsLoopback above on most stacks)

            // fe80::/10 — link-local
            if (v6[0] == 0xFE && (v6[1] & 0xC0) == 0x80)
                return "link-local IPv6 (fe80::/10)";

            // fc00::/7 — unique local
            if ((v6[0] & 0xFE) == 0xFC)
                return "unique-local IPv6 (fc00::/7)";

            // ff00::/8 — multicast
            if (v6[0] == 0xFF)
                return "multicast IPv6";

            // IPv4-mapped IPv6 (::ffff:0:0/96): unwrap and re-classify.
            if (address.IsIPv4MappedToIPv6)
            {
                return Classify(address.MapToIPv4(), allowLoopback);
            }
        }

        return null;
    }
}

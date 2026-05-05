using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using WebhookEngine.Core.Options;
using WebhookEngine.Infrastructure.Services;

namespace WebhookEngine.API.Validators;

/// <summary>
/// Centralizes the rule for what counts as a valid webhook endpoint URL.
/// Production: HTTPS-only and publicly-routable IPs only. Development: HTTP
/// and loopback are also accepted (dev compose receivers typically live on
/// 127.0.0.1). The relaxation is gated on
/// <see cref="IHostEnvironment.IsDevelopment"/>; production deployments
/// cannot opt in.
/// </summary>
public sealed class EndpointUrlPolicy
{
    private readonly bool _allowHttp;
    private readonly bool _allowLoopback;
    private readonly bool _ssrfGuardEnabled;

    public EndpointUrlPolicy(IHostEnvironment hostEnvironment, IOptions<SsrfGuardOptions> ssrfOptions)
    {
        _allowHttp = hostEnvironment.IsDevelopment();
        _ssrfGuardEnabled = ssrfOptions.Value.Enabled;
        _allowLoopback = hostEnvironment.IsDevelopment() && ssrfOptions.Value.AllowLoopbackInDevelopment;
    }

    public string ValidationMessage =>
        _allowHttp
            ? "Url must be a valid HTTPS or HTTP URL."
            : "Url must be a valid HTTPS URL.";

    public bool IsValid(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return false;
        }

        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            return false;
        }

        if (uri.Scheme == Uri.UriSchemeHttps)
        {
            return true;
        }

        return _allowHttp && uri.Scheme == Uri.UriSchemeHttp;
    }

    /// <summary>
    /// Resolves the URL host and rejects every resolved IP that lands in a
    /// private / loopback / cloud-metadata range. Returns a non-null error
    /// message on failure, null on success. Caller should also call IsValid
    /// before this method.
    /// </summary>
    public async Task<string?> CheckHostSafeAsync(string url, CancellationToken ct)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            return null;
        }

        IPAddress[] addresses;
        try
        {
            addresses = await Dns.GetHostAddressesAsync(uri.Host, ct);
        }
        catch (SocketException)
        {
            return $"Cannot resolve host '{uri.Host}'. Check the URL or DNS configuration.";
        }
        catch (ArgumentException)
        {
            return $"Invalid host '{uri.Host}'.";
        }

        if (addresses.Length == 0)
        {
            return $"Cannot resolve host '{uri.Host}'.";
        }

        if (!_ssrfGuardEnabled)
        {
            return null;
        }

        foreach (var address in addresses)
        {
            var reason = PrivateIpDetector.Classify(address, _allowLoopback);
            if (reason is not null)
            {
                return $"Host '{uri.Host}' resolves to {reason}; webhook delivery to this address is blocked.";
            }
        }

        return null;
    }
}

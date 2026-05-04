using Microsoft.Extensions.Hosting;

namespace WebhookEngine.API.Validators;

/// <summary>
/// Centralizes the rule for what counts as a valid webhook endpoint URL.
/// Production: HTTPS-only. Development environment: HTTP is also accepted so
/// local end-to-end testing (Docker compose, sample receivers, ngrok-free flows)
/// works without bending TLS configuration. The relaxation is gated on
/// <see cref="IHostEnvironment.IsDevelopment"/> — never on a feature flag — so
/// that production deployments cannot accidentally opt in.
/// </summary>
public sealed class EndpointUrlPolicy
{
    private readonly bool _allowHttp;

    public EndpointUrlPolicy(IHostEnvironment hostEnvironment)
    {
        _allowHttp = hostEnvironment.IsDevelopment();
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
}

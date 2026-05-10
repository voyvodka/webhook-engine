using FluentValidation;
using WebhookEngine.API.Contracts.Portal;
using WebhookEngine.Infrastructure.Services;

namespace WebhookEngine.API.Validators;

public class PortalCreateEndpointRequestValidator : AbstractValidator<PortalCreateEndpointRequest>
{
    public PortalCreateEndpointRequestValidator(EndpointUrlPolicy urlPolicy)
    {
        RuleFor(x => x.Url)
            .NotEmpty()
            .Must(urlPolicy.IsValid)
            .WithMessage(urlPolicy.ValidationMessage)
            .DependentRules(() =>
            {
                RuleFor(x => x.Url).CustomAsync(async (url, ctx, ct) =>
                {
                    var error = await urlPolicy.CheckHostSafeAsync(url, ct);
                    if (error is not null) ctx.AddFailure(error);
                });
            });

        RuleFor(x => x.Description)
            .MaximumLength(500)
            .When(x => x.Description is not null);

        RuleFor(x => x.CustomHeaders)
            .Must(headers => CustomHeaderPolicy.Validate(headers) is null)
            .WithMessage(x => CustomHeaderPolicy.Validate(x.CustomHeaders) ?? "Invalid custom headers.")
            .When(x => x.CustomHeaders is not null);

        RuleFor(x => x.SecretOverride)
            .MinimumLength(32)
            .MaximumLength(128)
            .Must(s => s!.StartsWith("whsec_", StringComparison.Ordinal))
            .WithMessage("SecretOverride must start with the 'whsec_' prefix and be at least 32 characters. Use the portal's rotate-secret action to generate one rather than typing a password.")
            .When(x => x.SecretOverride is not null);
    }
}

public class PortalUpdateEndpointRequestValidator : AbstractValidator<PortalUpdateEndpointRequest>
{
    public PortalUpdateEndpointRequestValidator(EndpointUrlPolicy urlPolicy)
    {
        RuleFor(x => x.Url)
            .Must(urlPolicy.IsValid)
            .When(x => x.Url is not null)
            .WithMessage(urlPolicy.ValidationMessage)
            .DependentRules(() =>
            {
                RuleFor(x => x.Url!).CustomAsync(async (url, ctx, ct) =>
                {
                    var error = await urlPolicy.CheckHostSafeAsync(url, ct);
                    if (error is not null) ctx.AddFailure(error);
                }).When(x => x.Url is not null);
            });

        RuleFor(x => x.Description)
            .MaximumLength(500)
            .When(x => x.Description is not null);

        RuleFor(x => x.CustomHeaders)
            .Must(headers => CustomHeaderPolicy.Validate(headers) is null)
            .WithMessage(x => CustomHeaderPolicy.Validate(x.CustomHeaders) ?? "Invalid custom headers.")
            .When(x => x.CustomHeaders is not null);

        RuleFor(x => x.SecretOverride)
            .MinimumLength(32)
            .MaximumLength(128)
            .Must(s => s!.StartsWith("whsec_", StringComparison.Ordinal))
            .WithMessage("SecretOverride must start with the 'whsec_' prefix and be at least 32 characters. Use the portal's rotate-secret action to generate one rather than typing a password.")
            .When(x => x.SecretOverride is not null);

        RuleFor(x => x)
            .Must(x => x.Url is not null
                || x.Description is not null
                || x.FilterEventTypes is not null
                || x.CustomHeaders is not null
                || x.Metadata is not null
                || x.SecretOverride is not null)
            .WithMessage("At least one field must be provided.");
    }
}

public class PortalEndpointTestRequestValidator : AbstractValidator<PortalEndpointTestRequest>
{
    private const int MaxPayloadBytes = 256 * 1024;

    public PortalEndpointTestRequestValidator()
    {
        RuleFor(x => x.EventType)
            .MaximumLength(256)
            .When(x => x.EventType is not null);

        RuleFor(x => x.Payload)
            .Must(payload => !payload.HasValue || payload.Value.GetRawText().Length <= MaxPayloadBytes)
            .WithMessage($"Payload exceeds the {MaxPayloadBytes / 1024} KB probe cap.");
    }
}


/// <summary>
/// Validator for the dashboard "set portal allowed origins" payload. Each origin
/// must be an absolute URL with no path/query/fragment, https-only outside of
/// development. Wildcards are rejected — the host SaaS must enumerate exact
/// origins. Empty array is valid (clears the allowlist).
///
/// Sanity caps: max 50 origins per app and max 256 chars per origin. The 50-cap
/// keeps the in-process membership check (PortalLookupCache + PortalCorsMiddleware)
/// O(small); 256 chars covers the longest reasonable scheme+host+port combo while
/// rejecting payload abuse.
/// </summary>
public class DashboardPortalOriginsRequestValidator : AbstractValidator<WebhookEngine.API.Controllers.DashboardPortalOriginsRequest>
{
    public const int MaxOrigins = 50;
    public const int MaxOriginLength = 256;

    public DashboardPortalOriginsRequestValidator(Microsoft.Extensions.Hosting.IHostEnvironment hostEnvironment)
    {
        var allowHttp = hostEnvironment.IsDevelopment();

        RuleFor(x => x.Origins)
            .NotNull().WithMessage("Origins must be an array (use [] to clear).")
            .Must(origins => origins!.Count <= MaxOrigins)
            .WithMessage($"At most {MaxOrigins} origins are allowed per application.");

        RuleForEach(x => x.Origins)
            .NotEmpty().WithMessage("Origin must not be empty.")
            .MaximumLength(MaxOriginLength)
            .WithMessage($"Origin exceeds the {MaxOriginLength}-character limit.")
            .Must(origin => !origin!.Contains('*'))
            .WithMessage("Wildcards are not allowed; enumerate exact origins.")
            .Must(origin => IsValidOrigin(origin, allowHttp))
            .WithMessage("Origin must be an absolute https URL with scheme and host only (no path, query, or fragment).");
    }

    private static bool IsValidOrigin(string? origin, bool allowHttp)
    {
        if (string.IsNullOrWhiteSpace(origin))
        {
            return false;
        }

        if (!Uri.TryCreate(origin, UriKind.Absolute, out var uri))
        {
            return false;
        }

        var schemeOk = uri.Scheme == Uri.UriSchemeHttps || (allowHttp && uri.Scheme == Uri.UriSchemeHttp);
        if (!schemeOk)
        {
            return false;
        }

        // RFC 6454 origins are scheme + host (+ optional non-default port).
        // Reject anything that smuggles a path, query, or fragment.
        if (uri.AbsolutePath != "/" && uri.AbsolutePath.Length > 0)
        {
            return false;
        }
        if (!string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment))
        {
            return false;
        }
        if (!string.IsNullOrEmpty(uri.UserInfo))
        {
            return false;
        }

        // The original input shouldn't carry a trailing slash beyond the host
        // (Uri parses "https://x" and "https://x/" identically into AbsolutePath="/",
        // so we check the raw string for the path segment after host:port).
        var schemePart = $"{uri.Scheme}://";
        var afterScheme = origin.AsSpan(schemePart.Length);
        // Forbid any '/' or '?' or '#' in the part after the scheme.
        if (afterScheme.IndexOfAny('/', '?', '#') >= 0)
        {
            return false;
        }

        return true;
    }
}

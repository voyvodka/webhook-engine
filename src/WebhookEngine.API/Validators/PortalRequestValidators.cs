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

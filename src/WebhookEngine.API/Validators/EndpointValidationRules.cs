using FluentValidation;
using WebhookEngine.Infrastructure.Services;

namespace WebhookEngine.API.Validators;

/// <summary>
/// Single source of truth for the field-shape rules shared between the admin
/// (`/api/v1/endpoints` + `/api/v1/dashboard/endpoints`) and customer-facing
/// (`/api/v1/portal/endpoints`) validators. Without this, the two surfaces
/// drift over time — the v0.2.0 audit flagged that the 32-char `whsec_`
/// prefix rule, the 4 KiB transform-expression cap, and the custom-header
/// policy were each duplicated in 4-6 places, so a tightening on one side
/// quietly leaves the other side weaker.
///
/// Async URL host-safety (`EndpointUrlPolicy.CheckHostSafeAsync`) is NOT
/// covered here because the FluentValidation `DependentRules` + `CustomAsync`
/// pattern needs to call back into the surrounding validator with the full
/// property selector — keeping it in each validator avoids a fragile
/// reflection-driven helper. The synchronous URL syntax check IS shared.
/// </summary>
public static class EndpointValidationRules
{
    public const int MaxDescriptionLength = 500;
    public const int MaxTransformExpressionLength = 4096;
    public const int MinSecretOverrideLength = 32;
    public const int MaxSecretOverrideLength = 128;
    public const string SecretOverridePrefix = "whsec_";

    /// <summary>
    /// Synchronous URL syntax + SSRF-classification (no DNS lookup). Pair with
    /// a `DependentRules { RuleFor(...).CustomAsync(... CheckHostSafeAsync ...) }`
    /// in the host validator for the eager DNS-resolution check.
    /// </summary>
    public static IRuleBuilderOptions<T, string?> EndpointUrlSyntax<T>(
        this IRuleBuilder<T, string?> rule,
        EndpointUrlPolicy urlPolicy)
    {
        return rule
            .Must(urlPolicy.IsValid)
            .WithMessage(urlPolicy.ValidationMessage);
    }

    public static IRuleBuilderOptions<T, string?> EndpointDescription<T>(
        this IRuleBuilder<T, string?> rule)
    {
        return rule.MaximumLength(MaxDescriptionLength);
    }

    public static IRuleBuilderOptions<T, string?> EndpointTransformExpression<T>(
        this IRuleBuilder<T, string?> rule)
    {
        return rule
            .MaximumLength(MaxTransformExpressionLength)
            .WithMessage($"TransformExpression must not exceed {MaxTransformExpressionLength} characters.");
    }

    public static IRuleBuilderOptions<T, IDictionary<string, string>?> EndpointCustomHeaders<T>(
        this IRuleBuilder<T, IDictionary<string, string>?> rule)
    {
        return rule
            .Must(headers => CustomHeaderPolicy.Validate(headers) is null)
            .WithMessage(_ => CustomHeaderPolicy.Validate(default(IDictionary<string, string>?)) ?? "Invalid custom headers.");
    }

    public static IRuleBuilderOptions<T, IList<string>?> EndpointAllowedIpsCidrs<T>(
        this IRuleBuilder<T, IList<string>?> rule)
    {
        return rule
            .Must(list => list!.All(cidr => IpAllowlistMatcher.TryParseCidr(cidr, out _)))
            .WithMessage("AllowedIps must contain valid CIDR notations (e.g. \"203.0.113.0/24\").");
    }

    /// <summary>
    /// Customer-typed override secret. Requires the `whsec_` prefix and at
    /// least 32 chars so a hand-typed weak password ("password123") cannot
    /// quietly undermine HMAC authenticity on every signed delivery.
    /// </summary>
    public static IRuleBuilderOptions<T, string?> EndpointSecretOverride<T>(
        this IRuleBuilder<T, string?> rule)
    {
        return rule
            .MinimumLength(MinSecretOverrideLength)
            .MaximumLength(MaxSecretOverrideLength)
            .Must(s => s!.StartsWith(SecretOverridePrefix, StringComparison.Ordinal))
            .WithMessage(
                $"SecretOverride must start with the '{SecretOverridePrefix}' prefix and be at least " +
                $"{MinSecretOverrideLength} characters. Use the portal's rotate-secret action to generate one " +
                "rather than typing a password.");
    }
}

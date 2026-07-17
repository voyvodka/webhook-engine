using Microsoft.Extensions.Options;
using WebhookEngine.Core.Options;

namespace WebhookEngine.API.Startup;

/// <summary>Fail-fast <see cref="RetryPolicyOptions"/> validation: MaxRetries is stamped onto every enqueued message and BackoffSchedule drives retry timing, so a zero cap or empty schedule must fail at boot rather than dead-letter or index out of range at runtime.</summary>
public sealed class RetryPolicyOptionsValidator : IValidateOptions<RetryPolicyOptions>
{
    public ValidateOptionsResult Validate(string? name, RetryPolicyOptions options)
    {
        var failures = new List<string>();

        if (options.MaxRetries <= 0)
            failures.Add($"{RetryPolicyOptions.SectionName}:MaxRetries must be greater than 0 (was {options.MaxRetries}).");

        if (options.BackoffSchedule is null || options.BackoffSchedule.Length == 0)
            failures.Add($"{RetryPolicyOptions.SectionName}:BackoffSchedule must contain at least one backoff interval.");

        return failures.Count > 0
            ? ValidateOptionsResult.Fail(failures)
            : ValidateOptionsResult.Success;
    }
}

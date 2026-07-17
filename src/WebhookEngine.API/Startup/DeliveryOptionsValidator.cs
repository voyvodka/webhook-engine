using Microsoft.Extensions.Options;
using WebhookEngine.Core.Options;

namespace WebhookEngine.API.Startup;

/// <summary>Fail-fast <see cref="DeliveryOptions"/> validation; the load-bearing rule is TimeoutSeconds * 1.5 &lt; StaleLockMinutes * 60 so a live delivery plus finalize fits inside the stale-lock threshold (else stale-lock recovery can double-deliver an in-flight message).</summary>
public sealed class DeliveryOptionsValidator : IValidateOptions<DeliveryOptions>
{
    public ValidateOptionsResult Validate(string? name, DeliveryOptions options)
    {
        var failures = new List<string>();

        if (options.TimeoutSeconds <= 0)
            failures.Add($"{DeliveryOptions.SectionName}:TimeoutSeconds must be greater than 0 (was {options.TimeoutSeconds}).");

        if (options.StaleLockMinutes <= 0)
            failures.Add($"{DeliveryOptions.SectionName}:StaleLockMinutes must be greater than 0 (was {options.StaleLockMinutes}).");

        if (options.BatchSize <= 0)
            failures.Add($"{DeliveryOptions.SectionName}:BatchSize must be greater than 0 (was {options.BatchSize}).");

        if (options.PollIntervalMs <= 0)
            failures.Add($"{DeliveryOptions.SectionName}:PollIntervalMs must be greater than 0 (was {options.PollIntervalMs}).");

        if (options.TimeoutSeconds > 0 && options.StaleLockMinutes > 0
            && options.TimeoutSeconds * 1.5 >= options.StaleLockMinutes * 60.0)
        {
            failures.Add(
                $"{DeliveryOptions.SectionName}: TimeoutSeconds * 1.5 must be less than StaleLockMinutes * 60 " +
                "so a live delivery plus finalize fits inside the stale-lock threshold — otherwise stale-lock " +
                "recovery can reclaim an in-flight message and double-deliver. Current: " +
                $"TimeoutSeconds={options.TimeoutSeconds}s (x1.5 = {options.TimeoutSeconds * 1.5}s) vs " +
                $"StaleLockMinutes={options.StaleLockMinutes}min ({options.StaleLockMinutes * 60}s). " +
                "Increase StaleLockMinutes or decrease TimeoutSeconds.");
        }

        return failures.Count > 0
            ? ValidateOptionsResult.Fail(failures)
            : ValidateOptionsResult.Success;
    }
}

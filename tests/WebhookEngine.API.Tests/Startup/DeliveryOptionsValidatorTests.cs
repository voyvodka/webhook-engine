using FluentAssertions;
using WebhookEngine.API.Startup;
using WebhookEngine.Core.Options;

namespace WebhookEngine.API.Tests.Startup;

// Fail-fast guard: TimeoutSeconds * 1.5 < StaleLockMinutes * 60 keeps a live delivery plus
// finalize inside the stale-lock window, else stale-lock recovery can reclaim an in-flight
// message and double-deliver. Plus a positivity floor on the four knobs.
public class DeliveryOptionsValidatorTests
{
    private static readonly DeliveryOptionsValidator Validator = new();

    [Fact]
    public void Validate_With_Default_Options_Succeeds()
    {
        var options = new DeliveryOptions();

        var result = Validator.Validate(null, options);

        result.Succeeded.Should().BeTrue(
            "the shipped defaults (30s timeout vs 5min stale window) satisfy the safety inequality");
    }

    [Fact]
    public void Validate_When_Timeout_Exceeds_Stale_Window_Fails_And_Names_Both_Knobs()
    {
        var options = new DeliveryOptions
        {
            TimeoutSeconds = 300,
            StaleLockMinutes = 2,
            BatchSize = 10,
            PollIntervalMs = 1000
        };

        var result = Validator.Validate(null, options);

        result.Failed.Should().BeTrue(
            "300s * 1.5 = 450s exceeds the 120s stale window, so stale-lock recovery could double-deliver");
        result.FailureMessage.Should().Contain("TimeoutSeconds")
            .And.Contain("StaleLockMinutes", "the failure must name both knobs the operator has to rebalance");
    }

    [Theory]
    [InlineData(0, 5, 10, 1000, "TimeoutSeconds")]
    [InlineData(-1, 5, 10, 1000, "TimeoutSeconds")]
    [InlineData(30, 0, 10, 1000, "StaleLockMinutes")]
    [InlineData(30, -1, 10, 1000, "StaleLockMinutes")]
    [InlineData(30, 5, 0, 1000, "BatchSize")]
    [InlineData(30, 5, -1, 1000, "BatchSize")]
    [InlineData(30, 5, 10, 0, "PollIntervalMs")]
    [InlineData(30, 5, 10, -1, "PollIntervalMs")]
    public void Validate_When_A_Knob_Is_Not_Positive_Fails(
        int timeoutSeconds, int staleLockMinutes, int batchSize, int pollIntervalMs, string expectedKnob)
    {
        var options = new DeliveryOptions
        {
            TimeoutSeconds = timeoutSeconds,
            StaleLockMinutes = staleLockMinutes,
            BatchSize = batchSize,
            PollIntervalMs = pollIntervalMs
        };

        var result = Validator.Validate(null, options);

        result.Failed.Should().BeTrue($"{expectedKnob} must be greater than 0");
        result.FailureMessage.Should().Contain(expectedKnob);
    }

    [Fact]
    public void Validate_At_Boundary_Where_Timeout_Times_1_5_Equals_Stale_Window_Fails()
    {
        // 40 * 1.5 = 60.0s == 1min * 60 = 60.0s; the check is >=, so equality must fail.
        var options = new DeliveryOptions
        {
            TimeoutSeconds = 40,
            StaleLockMinutes = 1,
            BatchSize = 10,
            PollIntervalMs = 1000
        };

        var result = Validator.Validate(null, options);

        result.Failed.Should().BeTrue("the inequality is strict (< ), so exact equality is rejected");
    }

    [Fact]
    public void Validate_Just_Below_Boundary_Succeeds()
    {
        // 39 * 1.5 = 58.5s < 1min * 60 = 60.0s, one step inside the safety window.
        var options = new DeliveryOptions
        {
            TimeoutSeconds = 39,
            StaleLockMinutes = 1,
            BatchSize = 10,
            PollIntervalMs = 1000
        };

        var result = Validator.Validate(null, options);

        result.Succeeded.Should().BeTrue("just under the equality point must pass, pinning the inequality direction");
    }
}

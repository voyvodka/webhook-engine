using FluentAssertions;
using WebhookEngine.API.Startup;
using WebhookEngine.Core.Options;

namespace WebhookEngine.API.Tests.Startup;

// Fail-fast guard: MaxRetries is stamped onto every enqueued message and BackoffSchedule drives
// retry timing, so a non-positive cap or an empty schedule must be rejected at boot rather than
// dead-letter immediately or index out of range at runtime.
public class RetryPolicyOptionsValidatorTests
{
    private static readonly RetryPolicyOptionsValidator Validator = new();

    [Fact]
    public void Validate_With_Default_Options_Succeeds()
    {
        var options = new RetryPolicyOptions();

        var result = Validator.Validate(null, options);

        result.Succeeded.Should().BeTrue(
            "the shipped defaults (MaxRetries=7, a non-empty backoff schedule) satisfy the fail-fast guard");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Validate_When_MaxRetries_Is_Not_Positive_Fails_And_Names_The_Knob(int maxRetries)
    {
        var options = new RetryPolicyOptions { MaxRetries = maxRetries };

        var result = Validator.Validate(null, options);

        result.Failed.Should().BeTrue($"MaxRetries={maxRetries} would dead-letter every message on the first pass");
        result.FailureMessage.Should().Contain("MaxRetries",
            "the failure must name the offending knob so the operator knows what to fix");
    }

    [Fact]
    public void Validate_When_BackoffSchedule_Is_Empty_Fails_And_Names_The_Knob()
    {
        var options = new RetryPolicyOptions { BackoffSchedule = [] };

        var result = Validator.Validate(null, options);

        result.Failed.Should().BeTrue("an empty schedule leaves retry timing undefined");
        result.FailureMessage.Should().Contain("BackoffSchedule",
            "the failure must name the offending knob so the operator knows what to fix");
    }

    [Fact]
    public void Validate_When_BackoffSchedule_Is_Null_Fails_And_Names_The_Knob()
    {
        var options = new RetryPolicyOptions { BackoffSchedule = null! };

        var result = Validator.Validate(null, options);

        result.Failed.Should().BeTrue("a null schedule leaves retry timing undefined");
        result.FailureMessage.Should().Contain("BackoffSchedule");
    }
}

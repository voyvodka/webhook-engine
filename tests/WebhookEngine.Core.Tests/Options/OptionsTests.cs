using FluentAssertions;
using WebhookEngine.Core.Options;

namespace WebhookEngine.Core.Tests.Options;

public class OptionsTests
{
    [Fact]
    public void RetryPolicyOptions_Has_Correct_Defaults()
    {
        var options = new RetryPolicyOptions();

        options.MaxRetries.Should().Be(7);
        options.BackoffSchedule.Should().BeEquivalentTo([5, 30, 120, 900, 3600, 21600, 86400]);
        options.BackoffSchedule.Should().HaveCount(7, "backoff schedule should match MaxRetries");
    }

    [Fact]
    public void RetryPolicyOptions_SectionName_Is_Correct()
    {
        RetryPolicyOptions.SectionName.Should().Be("WebhookEngine:RetryPolicy");
    }

    [Fact]
    public void DeliveryOptions_Has_Correct_Defaults()
    {
        var options = new DeliveryOptions();

        options.TimeoutSeconds.Should().Be(30);
        options.BatchSize.Should().Be(10);
        options.PollIntervalMs.Should().Be(1000);
        options.StaleLockMinutes.Should().Be(5);
    }

    [Fact]
    public void DeliveryOptions_SectionName_Is_Correct()
    {
        DeliveryOptions.SectionName.Should().Be("WebhookEngine:Delivery");
    }

    [Fact]
    public void CircuitBreakerOptions_Has_Correct_Defaults()
    {
        var options = new CircuitBreakerOptions();

        options.FailureThreshold.Should().Be(5);
        options.CooldownMinutes.Should().Be(5);
        options.SuccessThreshold.Should().Be(1);
    }

    [Fact]
    public void CircuitBreakerOptions_SectionName_Is_Correct()
    {
        CircuitBreakerOptions.SectionName.Should().Be("WebhookEngine:CircuitBreaker");
    }

    [Fact]
    public void DashboardAuthOptions_Has_Correct_Defaults()
    {
        var options = new DashboardAuthOptions();

        options.AdminEmail.Should().Be("admin@example.com");
        options.AdminPassword.Should().Be("changeme");
    }

    [Fact]
    public void DashboardAuthOptions_SectionName_Is_Correct()
    {
        DashboardAuthOptions.SectionName.Should().Be("WebhookEngine:DashboardAuth");
    }

    [Fact]
    public void RetentionOptions_Has_Correct_Defaults()
    {
        var options = new RetentionOptions();

        options.DeliveredRetentionDays.Should().Be(30);
        options.DeadLetterRetentionDays.Should().Be(90);
    }

    [Fact]
    public void RetentionOptions_SectionName_Is_Correct()
    {
        RetentionOptions.SectionName.Should().Be("WebhookEngine:Retention");
    }

    [Fact]
    public void Backoff_Schedule_Is_Strictly_Increasing()
    {
        var options = new RetryPolicyOptions();

        for (int i = 1; i < options.BackoffSchedule.Length; i++)
        {
            options.BackoffSchedule[i].Should().BeGreaterThan(
                options.BackoffSchedule[i - 1],
                $"backoff[{i}] should be greater than backoff[{i - 1}]");
        }
    }
}

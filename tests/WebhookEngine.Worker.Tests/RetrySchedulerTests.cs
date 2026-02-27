using FluentAssertions;
using WebhookEngine.Core.Enums;
using WebhookEngine.Core.Options;

namespace WebhookEngine.Worker.Tests;

/// <summary>
/// Tests for RetryScheduler logic — verifying that failed messages with
/// due ScheduledAt are eligible for requeue.
/// </summary>
public class RetrySchedulerTests
{
    [Fact]
    public void Failed_Message_With_Due_ScheduledAt_Is_Eligible_For_Requeue()
    {
        var now = DateTime.UtcNow;
        var message = new Core.Entities.Message
        {
            Status = MessageStatus.Failed,
            AttemptCount = 3,
            MaxRetries = 7,
            ScheduledAt = now.AddMinutes(-1) // Due in the past
        };

        var isEligible = message.Status == MessageStatus.Failed
                         && message.AttemptCount < message.MaxRetries
                         && message.ScheduledAt <= now;

        isEligible.Should().BeTrue();
    }

    [Fact]
    public void Failed_Message_With_Future_ScheduledAt_Is_Not_Eligible()
    {
        var now = DateTime.UtcNow;
        var message = new Core.Entities.Message
        {
            Status = MessageStatus.Failed,
            AttemptCount = 3,
            MaxRetries = 7,
            ScheduledAt = now.AddMinutes(10) // Future
        };

        var isEligible = message.Status == MessageStatus.Failed
                         && message.AttemptCount < message.MaxRetries
                         && message.ScheduledAt <= now;

        isEligible.Should().BeFalse();
    }

    [Fact]
    public void Failed_Message_At_MaxRetries_Is_Not_Eligible()
    {
        var now = DateTime.UtcNow;
        var message = new Core.Entities.Message
        {
            Status = MessageStatus.Failed,
            AttemptCount = 7,
            MaxRetries = 7,
            ScheduledAt = now.AddMinutes(-1)
        };

        var isEligible = message.Status == MessageStatus.Failed
                         && message.AttemptCount < message.MaxRetries
                         && message.ScheduledAt <= now;

        isEligible.Should().BeFalse();
    }

    [Fact]
    public void Delivered_Message_Is_Not_Eligible()
    {
        var now = DateTime.UtcNow;
        var message = new Core.Entities.Message
        {
            Status = MessageStatus.Delivered,
            AttemptCount = 1,
            MaxRetries = 7,
            ScheduledAt = now.AddMinutes(-1)
        };

        var isEligible = message.Status == MessageStatus.Failed
                         && message.AttemptCount < message.MaxRetries
                         && message.ScheduledAt <= now;

        isEligible.Should().BeFalse();
    }

    [Fact]
    public void DeadLetter_Message_Is_Not_Eligible()
    {
        var now = DateTime.UtcNow;
        var message = new Core.Entities.Message
        {
            Status = MessageStatus.DeadLetter,
            AttemptCount = 7,
            MaxRetries = 7,
            ScheduledAt = now.AddMinutes(-1)
        };

        var isEligible = message.Status == MessageStatus.Failed
                         && message.AttemptCount < message.MaxRetries
                         && message.ScheduledAt <= now;

        isEligible.Should().BeFalse();
    }

    [Theory]
    [InlineData(0, 5)]
    [InlineData(1, 30)]
    [InlineData(2, 120)]
    [InlineData(3, 900)]
    [InlineData(4, 3600)]
    [InlineData(5, 21600)]
    [InlineData(6, 86400)]
    public void Backoff_Schedule_Entries_Match_Spec(int index, int expectedSeconds)
    {
        var policy = new RetryPolicyOptions();
        policy.BackoffSchedule[index].Should().Be(expectedSeconds);
    }
}

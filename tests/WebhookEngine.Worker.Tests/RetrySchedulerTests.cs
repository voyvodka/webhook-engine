using FluentAssertions;
using WebhookEngine.Core.Options;

namespace WebhookEngine.Worker.Tests;

/// <summary>
/// Unit coverage for the <see cref="RetryPolicyOptions"/> defaults that the
/// RetryScheduler depends on. The eligibility predicate itself lives in raw SQL
/// (<c>MessageRepository.RequeueDueFailedMessagesAsync</c>) and is covered by a
/// real-PostgreSQL test in Infrastructure.Tests — testing it inline here would
/// only re-implement the production logic.
/// </summary>
public class RetrySchedulerTests
{
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

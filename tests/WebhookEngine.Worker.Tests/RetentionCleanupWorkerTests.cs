using System.Reflection;
using FluentAssertions;

namespace WebhookEngine.Worker.Tests;

public class RetentionCleanupWorkerTests
{
    [Fact]
    public void GetDelayUntilNextRunUtc_When_Before_03_00_Schedules_Same_Day()
    {
        var nowUtc = new DateTime(2026, 3, 2, 1, 30, 0, DateTimeKind.Utc);

        var delay = InvokeGetDelayUntilNextRunUtc(nowUtc);

        delay.Should().Be(TimeSpan.FromHours(1.5));
    }

    [Fact]
    public void GetDelayUntilNextRunUtc_When_After_03_00_Schedules_Next_Day()
    {
        var nowUtc = new DateTime(2026, 3, 2, 4, 15, 0, DateTimeKind.Utc);

        var delay = InvokeGetDelayUntilNextRunUtc(nowUtc);

        delay.Should().Be(TimeSpan.FromHours(22.75));
    }

    [Fact]
    public void GetDelayUntilNextRunUtc_When_Exactly_03_00_Schedules_Next_Day()
    {
        var nowUtc = new DateTime(2026, 3, 2, 3, 0, 0, DateTimeKind.Utc);

        var delay = InvokeGetDelayUntilNextRunUtc(nowUtc);

        delay.Should().Be(TimeSpan.FromDays(1));
    }

    private static TimeSpan InvokeGetDelayUntilNextRunUtc(DateTime nowUtc)
    {
        var method = typeof(RetentionCleanupWorker).GetMethod(
            "GetDelayUntilNextRunUtc",
            BindingFlags.NonPublic | BindingFlags.Static);

        method.Should().NotBeNull();

        var result = method!.Invoke(null, [nowUtc]);
        result.Should().NotBeNull();

        return (TimeSpan)result!;
    }
}

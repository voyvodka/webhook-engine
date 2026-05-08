using System.Reflection;
using FluentAssertions;
using WebhookEngine.Infrastructure.Services;

namespace WebhookEngine.Infrastructure.Tests.Services;

public class ApplicationRateLimiterTests
{
    [Fact]
    public void TryAcquire_Allows_Requests_Until_Limit_Then_Rejects()
    {
        var limiter = new ApplicationRateLimiter();
        var appId = Guid.NewGuid();

        limiter.TryAcquire(appId, 2, out _).Should().BeTrue();
        limiter.TryAcquire(appId, 2, out _).Should().BeTrue();

        var allowed = limiter.TryAcquire(appId, 2, out var retryAtUtc);

        allowed.Should().BeFalse();
        // Retry must land within the next-second window — well under 30s, very far
        // from "never," and notably tighter than the per-endpoint per-minute clock.
        retryAtUtc.Should().BeBefore(DateTime.UtcNow.AddSeconds(2));
        retryAtUtc.Should().BeAfter(DateTime.UtcNow.AddMilliseconds(-100));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(-1000)]
    public void TryAcquire_With_NonPositive_Limit_Always_Allows(int limitPerSecond)
    {
        var limiter = new ApplicationRateLimiter();
        var appId = Guid.NewGuid();

        limiter.TryAcquire(appId, limitPerSecond, out _).Should().BeTrue();
        limiter.TryAcquire(appId, limitPerSecond, out _).Should().BeTrue();
        limiter.TryAcquire(appId, limitPerSecond, out _).Should().BeTrue();
    }

    [Fact]
    public void TryAcquire_Resets_Window_After_Window_Expires()
    {
        var limiter = new ApplicationRateLimiter();
        var appId = Guid.NewGuid();

        limiter.TryAcquire(appId, 1, out _).Should().BeTrue();
        limiter.TryAcquire(appId, 1, out _).Should().BeFalse();

        // Backdate the window start by 2 seconds so the next call enters
        // the fresh-window branch.
        ForceWindowStart(limiter, appId, DateTime.UtcNow.AddSeconds(-2));

        limiter.TryAcquire(appId, 1, out _).Should().BeTrue();
    }

    [Fact]
    public void TryAcquire_Tracks_Different_Apps_Independently()
    {
        var limiter = new ApplicationRateLimiter();
        var appA = Guid.NewGuid();
        var appB = Guid.NewGuid();

        limiter.TryAcquire(appA, 1, out _).Should().BeTrue();
        limiter.TryAcquire(appA, 1, out _).Should().BeFalse();

        // App-B's bucket is independent — A burning its slot does not
        // prevent B from acquiring its first.
        limiter.TryAcquire(appB, 1, out _).Should().BeTrue();
    }

    private static void ForceWindowStart(ApplicationRateLimiter limiter, Guid appId, DateTime windowStartUtc)
    {
        var windowsField = typeof(ApplicationRateLimiter).GetField("_windows", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var windows = windowsField.GetValue(limiter)!;

        var indexer = windows.GetType().GetProperty("Item");
        var state = indexer!.GetValue(windows, [appId])!;

        var windowStartProperty = state.GetType().GetProperty("WindowStartUtc", BindingFlags.Instance | BindingFlags.Public)!;
        windowStartProperty.SetValue(state, windowStartUtc);
    }
}

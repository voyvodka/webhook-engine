using System.Reflection;
using FluentAssertions;
using WebhookEngine.Infrastructure.Services;

namespace WebhookEngine.Infrastructure.Tests.Services;

public class EndpointRateLimiterTests
{
    [Fact]
    public void TryAcquire_Allows_Requests_Until_Limit_Then_Rejects()
    {
        var limiter = new EndpointRateLimiter();
        var endpointId = Guid.NewGuid();

        limiter.TryAcquire(endpointId, 2, out _).Should().BeTrue();
        limiter.TryAcquire(endpointId, 2, out _).Should().BeTrue();

        var allowed = limiter.TryAcquire(endpointId, 2, out var retryAtUtc);

        allowed.Should().BeFalse();
        retryAtUtc.Should().BeAfter(DateTime.UtcNow.AddSeconds(30));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(-10)]
    public void TryAcquire_With_NonPositive_Limit_Always_Allows(int limitPerMinute)
    {
        var limiter = new EndpointRateLimiter();
        var endpointId = Guid.NewGuid();

        var first = limiter.TryAcquire(endpointId, limitPerMinute, out _);
        var second = limiter.TryAcquire(endpointId, limitPerMinute, out _);
        var third = limiter.TryAcquire(endpointId, limitPerMinute, out _);

        first.Should().BeTrue();
        second.Should().BeTrue();
        third.Should().BeTrue();
    }

    [Fact]
    public void TryAcquire_Resets_Window_After_Window_Expires()
    {
        var limiter = new EndpointRateLimiter();
        var endpointId = Guid.NewGuid();

        limiter.TryAcquire(endpointId, 1, out _).Should().BeTrue();
        limiter.TryAcquire(endpointId, 1, out _).Should().BeFalse();

        ForceWindowStart(limiter, endpointId, DateTime.UtcNow.AddMinutes(-2));

        limiter.TryAcquire(endpointId, 1, out _).Should().BeTrue();
    }

    private static void ForceWindowStart(EndpointRateLimiter limiter, Guid endpointId, DateTime windowStartUtc)
    {
        var windowsField = typeof(EndpointRateLimiter).GetField("_windows", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var windows = windowsField.GetValue(limiter)!;

        var indexer = windows.GetType().GetProperty("Item");
        var state = indexer!.GetValue(windows, [endpointId])!;

        var windowStartProperty = state.GetType().GetProperty("WindowStartUtc", BindingFlags.Instance | BindingFlags.Public)!;
        windowStartProperty.SetValue(state, windowStartUtc);
    }
}

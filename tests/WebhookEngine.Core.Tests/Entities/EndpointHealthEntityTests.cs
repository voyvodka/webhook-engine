using FluentAssertions;
using WebhookEngine.Core.Entities;
using WebhookEngine.Core.Enums;

namespace WebhookEngine.Core.Tests.Entities;

public class EndpointHealthEntityTests
{
    [Fact]
    public void New_EndpointHealth_Has_Expected_Defaults()
    {
        var health = new EndpointHealth();

        health.EndpointId.Should().Be(Guid.Empty);
        health.CircuitState.Should().Be(CircuitState.Closed);
        health.ConsecutiveFailures.Should().Be(0);
        health.LastFailureAt.Should().BeNull();
        health.LastSuccessAt.Should().BeNull();
        health.CooldownUntil.Should().BeNull();
        health.UpdatedAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(5));
    }
}

using FluentAssertions;
using WebhookEngine.Core.Enums;

namespace WebhookEngine.Core.Tests.Enums;

public class EnumTests
{
    [Fact]
    public void MessageStatus_Has_Expected_Values()
    {
        Enum.GetNames<MessageStatus>().Should().BeEquivalentTo(
            "Pending", "Sending", "Delivered", "Failed", "DeadLetter");
    }

    [Fact]
    public void AttemptStatus_Has_Expected_Values()
    {
        Enum.GetNames<AttemptStatus>().Should().BeEquivalentTo(
            "Success", "Failed", "Timeout", "Sending");
    }

    [Fact]
    public void CircuitState_Has_Expected_Values()
    {
        Enum.GetNames<CircuitState>().Should().BeEquivalentTo(
            "Closed", "Open", "HalfOpen");
    }

    [Fact]
    public void EndpointStatus_Has_Expected_Values()
    {
        Enum.GetNames<EndpointStatus>().Should().BeEquivalentTo(
            "Active", "Degraded", "Failed", "Disabled");
    }

    [Fact]
    public void MessageStatus_Pending_Is_Default()
    {
        default(MessageStatus).Should().Be(MessageStatus.Pending);
    }

    [Fact]
    public void CircuitState_Closed_Is_Default()
    {
        default(CircuitState).Should().Be(CircuitState.Closed);
    }
}

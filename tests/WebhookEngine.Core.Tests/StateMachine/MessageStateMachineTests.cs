using FluentAssertions;
using WebhookEngine.Core.Enums;
using WebhookEngine.Core.StateMachine;

namespace WebhookEngine.Core.Tests.StateMachine;

// B8: exhaustive guard over MessageStateMachine.IsValid. The legal set is enumerated from
// the production AllowedTransitions table; every other (from, to) pair must be rejected —
// so loosening the table (e.g. re-allowing Delivered -> anything) breaks a test.
public class MessageStateMachineTests
{
    private static readonly (MessageStatus From, MessageStatus To)[] LegalTransitions =
    [
        (MessageStatus.Pending, MessageStatus.Sending),
        (MessageStatus.Sending, MessageStatus.Delivered),
        (MessageStatus.Sending, MessageStatus.Failed),
        (MessageStatus.Sending, MessageStatus.DeadLetter),
        (MessageStatus.Sending, MessageStatus.Pending),
        (MessageStatus.Failed, MessageStatus.Pending),
        (MessageStatus.DeadLetter, MessageStatus.Pending)
    ];

    public static IEnumerable<object[]> LegalTransitionCases()
        => LegalTransitions.Select(t => new object[] { t.From, t.To });

    public static IEnumerable<object[]> IllegalTransitionCases()
    {
        var legal = LegalTransitions.ToHashSet();
        foreach (var from in Enum.GetValues<MessageStatus>())
            foreach (var to in Enum.GetValues<MessageStatus>())
                if (!legal.Contains((from, to)))
                    yield return [from, to];
    }

    [Theory]
    [MemberData(nameof(LegalTransitionCases))]
    public void IsValid_For_Legal_Transition_Returns_True(MessageStatus from, MessageStatus to)
    {
        var machine = new MessageStateMachine();

        machine.IsValid(from, to).Should().BeTrue($"{from} -> {to} is in the allowed-transitions table");
    }

    [Theory]
    [MemberData(nameof(IllegalTransitionCases))]
    public void IsValid_For_Illegal_Transition_Returns_False(MessageStatus from, MessageStatus to)
    {
        var machine = new MessageStateMachine();

        machine.IsValid(from, to).Should().BeFalse($"{from} -> {to} is not in the allowed-transitions table");
    }

    [Theory]
    [InlineData(MessageStatus.Delivered, MessageStatus.Pending)]
    [InlineData(MessageStatus.Delivered, MessageStatus.Sending)]
    [InlineData(MessageStatus.Delivered, MessageStatus.Failed)]
    [InlineData(MessageStatus.Delivered, MessageStatus.DeadLetter)]
    [InlineData(MessageStatus.DeadLetter, MessageStatus.Sending)]
    [InlineData(MessageStatus.DeadLetter, MessageStatus.Delivered)]
    [InlineData(MessageStatus.DeadLetter, MessageStatus.Failed)]
    public void IsValid_Blocks_Terminal_State_Regression(MessageStatus from, MessageStatus to)
    {
        var machine = new MessageStateMachine();

        machine.IsValid(from, to).Should().BeFalse(
            $"the delivery fix leans on {from} being terminal against {to}");
    }

    [Theory]
    [InlineData(MessageStatus.Sending, MessageStatus.Delivered)]
    [InlineData(MessageStatus.Sending, MessageStatus.Failed)]
    [InlineData(MessageStatus.Sending, MessageStatus.DeadLetter)]
    [InlineData(MessageStatus.Sending, MessageStatus.Pending)]
    public void IsValid_Allows_Every_Sending_Exit_The_Delivery_Loop_Uses(MessageStatus from, MessageStatus to)
    {
        var machine = new MessageStateMachine();

        machine.IsValid(from, to).Should().BeTrue(
            $"the delivery loop drives {from} -> {to}");
    }

    [Fact]
    public void ValidateTransition_For_Legal_Transition_Does_Not_Throw()
    {
        var machine = new MessageStateMachine();

        var act = () => machine.ValidateTransition(MessageStatus.Sending, MessageStatus.Delivered);

        act.Should().NotThrow();
    }

    [Fact]
    public void ValidateTransition_For_Illegal_Transition_Throws_With_Descriptive_Message()
    {
        var machine = new MessageStateMachine();

        var act = () => machine.ValidateTransition(MessageStatus.Delivered, MessageStatus.Pending);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*Delivered*Pending*");
    }
}

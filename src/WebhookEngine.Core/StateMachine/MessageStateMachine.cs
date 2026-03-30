using WebhookEngine.Core.Enums;
using WebhookEngine.Core.Interfaces;

namespace WebhookEngine.Core.StateMachine;

/// <summary>
/// Enforces valid message status transitions. Invalid transitions throw InvalidOperationException (D-04).
///
/// Transition table:
///   Pending    -> Sending     (DequeueAsync picks up message)
///   Sending    -> Delivered   (successful delivery)
///   Sending    -> Failed      (failed delivery, retries remaining)
///   Sending    -> DeadLetter  (max retries exceeded)
///   Sending    -> Pending     (rate limit / circuit open / error recovery)
///   Failed     -> Pending     (RetryScheduler re-enqueues)
///   DeadLetter -> Pending     (manual replay via RetryAsync)
///   Delivered  -> *           (BLOCKED — terminal state)
/// </summary>
public class MessageStateMachine : IMessageStateMachine
{
    private static readonly HashSet<(MessageStatus From, MessageStatus To)> AllowedTransitions = new()
    {
        (MessageStatus.Pending,    MessageStatus.Sending),
        (MessageStatus.Sending,    MessageStatus.Delivered),
        (MessageStatus.Sending,    MessageStatus.Failed),
        (MessageStatus.Sending,    MessageStatus.DeadLetter),
        (MessageStatus.Sending,    MessageStatus.Pending),
        (MessageStatus.Failed,     MessageStatus.Pending),
        (MessageStatus.DeadLetter, MessageStatus.Pending)
    };

    /// <inheritdoc />
    public bool IsValid(MessageStatus from, MessageStatus to)
        => AllowedTransitions.Contains((from, to));

    /// <inheritdoc />
    public void ValidateTransition(MessageStatus from, MessageStatus to)
    {
        if (!IsValid(from, to))
            throw new InvalidOperationException(
                $"Invalid message status transition: {from} -> {to}");
    }
}

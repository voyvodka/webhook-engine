using WebhookEngine.Core.Enums;

namespace WebhookEngine.Core.Interfaces;

/// <summary>
/// Centralized state machine for message status transitions.
/// All status changes must go through this interface to prevent invalid transitions
/// such as Delivered to Pending regression.
/// </summary>
public interface IMessageStateMachine
{
    /// <summary>
    /// Validates the transition is allowed. Throws InvalidOperationException if not.
    /// </summary>
    void ValidateTransition(MessageStatus from, MessageStatus to);

    /// <summary>
    /// Returns true if the transition from one status to another is valid.
    /// </summary>
    bool IsValid(MessageStatus from, MessageStatus to);
}

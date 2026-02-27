namespace WebhookEngine.Core.Options;

public class CircuitBreakerOptions
{
    public const string SectionName = "WebhookEngine:CircuitBreaker";

    /// <summary>
    /// Number of consecutive failures before opening the circuit.
    /// </summary>
    public int FailureThreshold { get; set; } = 5;

    /// <summary>
    /// Cooldown period in minutes before transitioning from Open to HalfOpen.
    /// </summary>
    public int CooldownMinutes { get; set; } = 5;

    /// <summary>
    /// Number of successes in HalfOpen state to close the circuit.
    /// </summary>
    public int SuccessThreshold { get; set; } = 1;
}

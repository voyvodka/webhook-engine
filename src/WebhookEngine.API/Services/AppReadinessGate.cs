namespace WebhookEngine.API.Services;

/// <summary>
/// Single source of truth for the readiness probe. Set to true once startup
/// migrations + admin seeding finish. Until then, /health/ready returns 503
/// so a load balancer or orchestrator does not route traffic to a pod that
/// hasn't finished bootstrapping.
/// </summary>
public sealed class AppReadinessGate
{
    private volatile bool _ready;

    public bool IsReady => _ready;

    public void MarkReady() => _ready = true;
}

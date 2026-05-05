namespace WebhookEngine.Core.Options;

public class SsrfGuardOptions
{
    public const string SectionName = "WebhookEngine:SsrfGuard";

    /// <summary>
    /// Master switch. Set to false to disable all private-IP / metadata-IP
    /// rejection. Default true; only flip off in tightly-controlled internal
    /// deployments where webhook receivers legitimately live on private IPs.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// In Development the dev compose stack typically resolves the receiver
    /// to a loopback or RFC1918 address. This flag flips to true automatically
    /// in Development; production keeps it false so a misconfigured prod
    /// deployment can't be talked into delivering to its own loopback.
    /// </summary>
    public bool AllowLoopbackInDevelopment { get; set; } = true;
}

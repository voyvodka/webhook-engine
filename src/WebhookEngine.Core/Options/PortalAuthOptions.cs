namespace WebhookEngine.Core.Options;

/// <summary>
/// Tuning knobs for the embedded customer portal's JWT verification path.
/// All values are deliberately conservative — the host SaaS mints fresh
/// tokens per page render, so a short MaxLifetime and small ClockSkew are
/// safe and reduce the blast radius of a leaked token.
/// </summary>
public class PortalAuthOptions
{
    public const string SectionName = "WebhookEngine:PortalAuth";

    /// <summary>
    /// Hard cap on <c>exp - nbf</c>. Tokens with a longer requested lifetime
    /// are rejected even if currently still valid. <c>nbf</c> must be present;
    /// <c>iat</c> is not consulted because not every JWT library emits it by
    /// default. Defaults to 15 minutes.
    /// </summary>
    public int MaxLifetimeMinutes { get; set; } = 15;

    /// <summary>
    /// Allowance for clock drift between the host SaaS minter and this engine
    /// when validating <c>nbf</c> / <c>exp</c>. Defaults to 30 seconds.
    /// </summary>
    public int ClockSkewSeconds { get; set; } = 30;

    /// <summary>
    /// TTL on the per-app signing-key + allowed-origins lookup cache. Trades
    /// freshness on rotation against database round-trips per request.
    /// Defaults to 60 seconds.
    /// </summary>
    public int LookupCacheTtlSeconds { get; set; } = 60;
}

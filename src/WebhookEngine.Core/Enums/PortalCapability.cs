namespace WebhookEngine.Core.Enums;

/// <summary>
/// Capabilities a portal JWT may grant to the embedded customer portal session.
/// Wire form is colon-delimited (e.g. <c>endpoints:read</c>) — see
/// <see cref="PortalCapabilityExtensions"/> for round-trip helpers.
/// </summary>
public enum PortalCapability
{
    EndpointsRead,
    EndpointsWrite,
    EndpointsTest,
    AttemptsRead
}

public static class PortalCapabilityExtensions
{
    /// <summary>
    /// Convert the enum value to its colon-delimited wire form.
    /// </summary>
    public static string ToWire(this PortalCapability capability) => capability switch
    {
        PortalCapability.EndpointsRead => "endpoints:read",
        PortalCapability.EndpointsWrite => "endpoints:write",
        PortalCapability.EndpointsTest => "endpoints:test",
        PortalCapability.AttemptsRead => "attempts:read",
        _ => throw new ArgumentOutOfRangeException(nameof(capability), capability, null)
    };

    /// <summary>
    /// Parse the colon-delimited wire form into an enum value. Unknown values
    /// return <see langword="null"/> (forward-compat: tokens minted by a newer
    /// host SaaS with capabilities this engine doesn't recognise yet are
    /// silently dropped rather than failing the request).
    /// </summary>
    public static PortalCapability? TryFromWire(string? wire) => wire switch
    {
        "endpoints:read" => PortalCapability.EndpointsRead,
        "endpoints:write" => PortalCapability.EndpointsWrite,
        "endpoints:test" => PortalCapability.EndpointsTest,
        "attempts:read" => PortalCapability.AttemptsRead,
        _ => null
    };
}

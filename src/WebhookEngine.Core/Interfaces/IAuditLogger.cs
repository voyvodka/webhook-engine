namespace WebhookEngine.Core.Interfaces;

/// <summary>
/// Records an admin action into the append-only <c>audit_logs</c> table.
/// Best-effort: a logger failure must NOT abort the action it was meant to
/// describe — the implementation swallows exceptions and lets the call site
/// continue. Callers are responsible for resolving the acting user and the
/// HTTP context (IP / user-agent) and passing them in.
/// </summary>
public interface IAuditLogger
{
    Task LogAsync(AuditLogEntry entry, CancellationToken ct = default);
}

/// <summary>
/// Single audit-log row payload. <see cref="Before"/> / <see cref="After"/>
/// are arbitrary anonymous types; the logger serialises them with the same
/// JSON conventions the rest of the API uses.
/// </summary>
public sealed class AuditLogEntry
{
    public required string Action { get; init; }
    public required string ResourceType { get; init; }
    public Guid? ResourceId { get; init; }
    public Guid? AppId { get; init; }
    public Guid? UserId { get; init; }
    public object? Before { get; init; }
    public object? After { get; init; }
    public string? IpAddress { get; init; }
    public string? UserAgent { get; init; }
}

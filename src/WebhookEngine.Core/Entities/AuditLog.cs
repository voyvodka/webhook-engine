namespace WebhookEngine.Core.Entities;

/// <summary>
/// Append-only record of an admin action: who did it, when, against which
/// resource, and (where it makes sense to capture) the before/after JSON
/// snapshots so a regression can be diffed without re-running the action.
/// Compliance for multi-tenant SaaS deployments and on-call forensics for
/// "who changed this endpoint?" both hang off this table.
/// </summary>
public class AuditLog
{
    public Guid Id { get; set; }
    public Guid? AppId { get; set; }
    public Guid? UserId { get; set; }

    /// <summary>
    /// Stable dotted action key — e.g. <c>application.created</c>,
    /// <c>endpoint.disabled</c>, <c>messages.replay</c>. Stays the same across
    /// future UI churn so log queries don't need translation.
    /// </summary>
    public string Action { get; set; } = string.Empty;

    /// <summary>
    /// The kind of thing the action targeted: <c>application</c>,
    /// <c>endpoint</c>, <c>event-type</c>, <c>message</c>, etc.
    /// </summary>
    public string ResourceType { get; set; } = string.Empty;

    /// <summary>
    /// Optional id of the targeted resource. Bulk actions (e.g. replay) leave
    /// this null and stash details in <see cref="AfterJson"/>.
    /// </summary>
    public Guid? ResourceId { get; set; }

    /// <summary>JSON snapshot of the resource immediately before the action. Null on creates.</summary>
    public string? BeforeJson { get; set; }

    /// <summary>JSON snapshot or detail blob immediately after the action. Null on deletes.</summary>
    public string? AfterJson { get; set; }

    public string? IpAddress { get; set; }
    public string? UserAgent { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

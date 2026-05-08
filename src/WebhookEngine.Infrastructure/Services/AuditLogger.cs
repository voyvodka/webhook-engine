using System.Text.Json;
using Microsoft.Extensions.Logging;
using WebhookEngine.Core.Entities;
using WebhookEngine.Core.Interfaces;
using WebhookEngine.Infrastructure.Data;

namespace WebhookEngine.Infrastructure.Services;

/// <summary>
/// Writes audit-log rows. Scoped because it shares <see cref="WebhookDbContext"/>
/// with the calling controller — the audit row goes in the same scope as the
/// action it describes, but in its own SaveChanges call so a transient logger
/// failure cannot roll back the action itself.
/// </summary>
public class AuditLogger : IAuditLogger
{
    // Match the rest of the API: camelCase keys + sensible whitespace
    // suppression so jsonb stays compact in the column.
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    private readonly WebhookDbContext _dbContext;
    private readonly ILogger<AuditLogger>? _logger;

    public AuditLogger(WebhookDbContext dbContext, ILogger<AuditLogger>? logger = null)
    {
        _dbContext = dbContext;
        _logger = logger;
    }

    public async Task LogAsync(AuditLogEntry entry, CancellationToken ct = default)
    {
        try
        {
            var row = new AuditLog
            {
                Action = entry.Action,
                ResourceType = entry.ResourceType,
                ResourceId = entry.ResourceId,
                AppId = entry.AppId,
                UserId = entry.UserId,
                BeforeJson = entry.Before is null ? null : JsonSerializer.Serialize(entry.Before, SerializerOptions),
                AfterJson = entry.After is null ? null : JsonSerializer.Serialize(entry.After, SerializerOptions),
                IpAddress = Truncate(entry.IpAddress, 45),
                UserAgent = Truncate(entry.UserAgent, 255)
            };

            _dbContext.AuditLogs.Add(row);
            await _dbContext.SaveChangesAsync(ct);
        }
        catch (Exception ex)
        {
            // Audit failures must not bubble up. The DB has the action's own
            // committed state; a missed log row is a follow-up cleanup, not a
            // reason to 500 the request.
            _logger?.LogWarning(ex, "Failed to write audit log entry for {Action} on {ResourceType}", entry.Action, entry.ResourceType);
        }
    }

    private static string? Truncate(string? value, int max)
    {
        if (string.IsNullOrEmpty(value)) return value;
        return value.Length <= max ? value : value[..max];
    }
}

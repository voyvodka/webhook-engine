namespace WebhookEngine.Core.Options;

public class DashboardAuthOptions
{
    public const string SectionName = "WebhookEngine:DashboardAuth";

    public string AdminEmail { get; set; } = "admin@example.com";
    public string AdminPassword { get; set; } = "changeme";
}

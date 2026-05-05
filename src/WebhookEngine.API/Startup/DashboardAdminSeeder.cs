using Microsoft.EntityFrameworkCore;
using WebhookEngine.API.Auth;
using WebhookEngine.Core.Entities;
using WebhookEngine.Core.Options;
using WebhookEngine.Infrastructure.Data;

namespace WebhookEngine.API.Startup;

public static class DashboardAdminSeeder
{
    public static async Task SeedAsync(
        WebhookDbContext dbContext,
        DashboardAuthOptions options,
        ILogger logger,
        CancellationToken ct = default)
    {
        var adminEmail = options.AdminEmail.Trim();
        var adminPassword = options.AdminPassword;

        if (string.IsNullOrWhiteSpace(adminEmail) || string.IsNullOrWhiteSpace(adminPassword))
        {
            logger.LogWarning("Dashboard admin seed skipped because email/password is empty.");
            return;
        }

        var exists = await dbContext.DashboardUsers
            .AsNoTracking()
            .AnyAsync(u => u.Email == adminEmail, ct);

        if (exists)
        {
            return;
        }

        var user = new DashboardUser
        {
            Email = adminEmail,
            PasswordHash = PasswordHasher.HashPassword(adminPassword),
            Role = "admin"
        };

        dbContext.DashboardUsers.Add(user);
        await dbContext.SaveChangesAsync(ct);

        // Intentionally omits the admin email from the log line. CodeQL's
        // cs/exposure-of-sensitive-information follows the value through any
        // helper, including a redaction wrapper, so the safest fix is simply
        // not to emit it. The deployment operator already knows the configured
        // email from appsettings / environment variables; the log only needs
        // to confirm that the seed ran.
        logger.LogInformation("Dashboard admin user seeded.");
    }
}

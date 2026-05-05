using Microsoft.EntityFrameworkCore;
using WebhookEngine.API.Auth;
using WebhookEngine.Core.Entities;
using WebhookEngine.Core.Options;
using WebhookEngine.Infrastructure.Data;

namespace WebhookEngine.API.Startup;

public static class DashboardAdminSeeder
{
    private static readonly string[] WeakDefaults =
    {
        "changeme", "password", "admin", "admin@example.com"
    };

    public static async Task SeedAsync(
        WebhookDbContext dbContext,
        DashboardAuthOptions options,
        ILogger logger,
        bool isDevelopment,
        CancellationToken ct = default)
    {
        var adminEmail = options.AdminEmail.Trim();
        var adminPassword = options.AdminPassword;

        if (string.IsNullOrWhiteSpace(adminEmail) || string.IsNullOrWhiteSpace(adminPassword))
        {
            logger.LogWarning("Dashboard admin seed skipped because email/password is empty.");
            return;
        }

        // Refuse to seed the shipped defaults (admin@example.com / changeme)
        // outside Development so a forgotten env-var override doesn't end up
        // creating a known-weak admin in production.
        var emailIsDefault = adminEmail.Equals("admin@example.com", StringComparison.OrdinalIgnoreCase);
        var passwordIsWeak = WeakDefaults.Any(d => string.Equals(adminPassword, d, StringComparison.OrdinalIgnoreCase))
            || adminPassword.Length < 12;

        if (!isDevelopment && (emailIsDefault || passwordIsWeak))
        {
            throw new InvalidOperationException(
                "Refusing to seed dashboard admin with default or weak credentials in non-Development environment. " +
                "Set WebhookEngine__DashboardAuth__AdminEmail and WebhookEngine__DashboardAuth__AdminPassword " +
                "(min 12 characters) before starting the host.");
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

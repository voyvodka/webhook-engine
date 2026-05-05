using Microsoft.EntityFrameworkCore;
using WebhookEngine.API.Auth;
using WebhookEngine.API.Services;
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

        logger.LogInformation("Dashboard admin user seeded for {Email}.", LogSanitizer.RedactEmail(adminEmail));
    }
}

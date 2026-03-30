using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WebhookEngine.Infrastructure.Migrations;

/// <inheritdoc />
public partial class AddIdempotencyWindowMinutesToApplications : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            ALTER TABLE applications
            ADD COLUMN idempotency_window_minutes integer NOT NULL DEFAULT 1440;
            """);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            ALTER TABLE applications
            DROP COLUMN IF EXISTS idempotency_window_minutes;
            """);
    }
}

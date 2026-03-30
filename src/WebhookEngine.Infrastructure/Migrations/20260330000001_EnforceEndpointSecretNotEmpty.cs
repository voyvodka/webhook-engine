using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WebhookEngine.Infrastructure.Migrations;

/// <summary>
/// CORR-03: Enforce that endpoints.secret_override, when provided, is not empty or whitespace-only.
/// Application.SigningSecret is already NOT NULL from InitialCreate.
/// Endpoint.SecretOverride remains nullable (NULL = use app-level secret).
///
/// D-05: Backfill empty/whitespace values to NULL before adding constraint.
/// D-06: CHECK constraint ensures the signing chain integrity at the database level.
/// </summary>
public partial class EnforceEndpointSecretNotEmpty : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // Step 1: Backfill — clean up empty/whitespace secret_override values to NULL
        // An empty override is meaningless; NULL means "use application-level secret"
        migrationBuilder.Sql("""
            UPDATE endpoints
            SET secret_override = NULL,
                updated_at = NOW()
            WHERE secret_override IS NOT NULL
              AND TRIM(secret_override) = '';
            """);

        // Step 2: Add CHECK constraint — value must be NULL or a non-empty trimmed string
        migrationBuilder.Sql("""
            ALTER TABLE endpoints
            ADD CONSTRAINT chk_endpoints_secret_override_not_empty
            CHECK (secret_override IS NULL OR TRIM(secret_override) <> '');
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            ALTER TABLE endpoints
            DROP CONSTRAINT IF EXISTS chk_endpoints_secret_override_not_empty;
            """);
    }
}

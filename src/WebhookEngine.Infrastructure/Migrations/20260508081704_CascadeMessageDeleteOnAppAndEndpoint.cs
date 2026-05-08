using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WebhookEngine.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class CascadeMessageDeleteOnAppAndEndpoint : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // EF Core's diff doesn't surface ON DELETE changes on an existing FK,
            // so we drop and re-create the two messages-table foreign keys with
            // CASCADE semantics. Without this, deleting an Application or
            // Endpoint with bound messages fails the foreign key constraint at
            // the database boundary (PostgreSQL surfaces it as a 500), defeating
            // the admin-side delete flow.
            migrationBuilder.Sql("""
                ALTER TABLE messages
                  DROP CONSTRAINT "FK_messages_applications_app_id",
                  ADD CONSTRAINT "FK_messages_applications_app_id"
                    FOREIGN KEY (app_id) REFERENCES applications (id)
                    ON DELETE CASCADE;
                """);

            migrationBuilder.Sql("""
                ALTER TABLE messages
                  DROP CONSTRAINT "FK_messages_endpoints_endpoint_id",
                  ADD CONSTRAINT "FK_messages_endpoints_endpoint_id"
                    FOREIGN KEY (endpoint_id) REFERENCES endpoints (id)
                    ON DELETE CASCADE;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Restore the previous NO ACTION semantics so a rollback puts the
            // schema back exactly where it was.
            migrationBuilder.Sql("""
                ALTER TABLE messages
                  DROP CONSTRAINT "FK_messages_applications_app_id",
                  ADD CONSTRAINT "FK_messages_applications_app_id"
                    FOREIGN KEY (app_id) REFERENCES applications (id);
                """);

            migrationBuilder.Sql("""
                ALTER TABLE messages
                  DROP CONSTRAINT "FK_messages_endpoints_endpoint_id",
                  ADD CONSTRAINT "FK_messages_endpoints_endpoint_id"
                    FOREIGN KEY (endpoint_id) REFERENCES endpoints (id);
                """);
        }
    }
}

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WebhookEngine.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddIdempotencyUniqueIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Defensive: NULL out duplicate idempotency_keys before creating the
            // unique index so this migration cannot fail on legacy rows. Keeps
            // the most-recently-created message for each (app, endpoint, key)
            // triple; older duplicates lose their idempotency_key (the rows
            // themselves are preserved). On a fresh deployment with no
            // duplicates this UPDATE matches zero rows and is a no-op.
            migrationBuilder.Sql("""
                UPDATE messages SET idempotency_key = NULL
                WHERE id IN (
                    SELECT id FROM (
                        SELECT id, ROW_NUMBER() OVER (
                            PARTITION BY app_id, endpoint_id, idempotency_key
                            ORDER BY created_at DESC, id DESC
                        ) AS rn
                        FROM messages
                        WHERE idempotency_key IS NOT NULL
                    ) ranked
                    WHERE rn > 1
                );
                """);

            migrationBuilder.CreateIndex(
                name: "idx_messages_app_endpoint_idempotency",
                table: "messages",
                columns: new[] { "app_id", "endpoint_id", "idempotency_key" },
                unique: true,
                filter: "idempotency_key IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "idx_messages_app_endpoint_idempotency",
                table: "messages");
        }
    }
}

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WebhookEngine.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddRetrySweepAndAttemptsCreatedAtIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "idx_messages_retry",
                table: "messages",
                column: "scheduled_at",
                filter: "status = 'Failed'");

            migrationBuilder.CreateIndex(
                name: "idx_message_attempts_created_at",
                table: "message_attempts",
                column: "created_at");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "idx_messages_retry",
                table: "messages");

            migrationBuilder.DropIndex(
                name: "idx_message_attempts_created_at",
                table: "message_attempts");
        }
    }
}

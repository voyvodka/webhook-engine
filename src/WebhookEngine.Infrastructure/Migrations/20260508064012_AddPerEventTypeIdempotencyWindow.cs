using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WebhookEngine.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddPerEventTypeIdempotencyWindow : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "idempotency_window_minutes",
                table: "event_types",
                type: "integer",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "idempotency_window_minutes",
                table: "event_types");
        }
    }
}

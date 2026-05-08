using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WebhookEngine.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddPerAppRetentionOverrides : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "retention_dead_letter_days",
                table: "applications",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "retention_delivered_days",
                table: "applications",
                type: "integer",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "retention_dead_letter_days",
                table: "applications");

            migrationBuilder.DropColumn(
                name: "retention_delivered_days",
                table: "applications");
        }
    }
}

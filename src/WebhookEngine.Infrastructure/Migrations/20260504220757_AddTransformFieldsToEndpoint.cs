using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WebhookEngine.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddTransformFieldsToEndpoint : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "transform_enabled",
                table: "endpoints",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "transform_expression",
                table: "endpoints",
                type: "character varying(4096)",
                maxLength: 4096,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "transform_validated_at",
                table: "endpoints",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "transform_enabled",
                table: "endpoints");

            migrationBuilder.DropColumn(
                name: "transform_expression",
                table: "endpoints");

            migrationBuilder.DropColumn(
                name: "transform_validated_at",
                table: "endpoints");
        }
    }
}

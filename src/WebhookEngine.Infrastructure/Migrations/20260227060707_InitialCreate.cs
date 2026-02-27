using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WebhookEngine.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "applications",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    api_key_prefix = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    api_key_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    signing_secret = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    retry_policy = table.Column<string>(type: "jsonb", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()"),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_applications", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "dashboard_users",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    email = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    password_hash = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    role = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false, defaultValue: "admin"),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()"),
                    last_login_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_dashboard_users", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "endpoints",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    app_id = table.Column<Guid>(type: "uuid", nullable: false),
                    url = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false, defaultValue: "Active"),
                    custom_headers = table.Column<string>(type: "jsonb", nullable: false, defaultValueSql: "'{}'"),
                    secret_override = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    metadata = table.Column<string>(type: "jsonb", nullable: false, defaultValueSql: "'{}'"),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()"),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_endpoints", x => x.id);
                    table.ForeignKey(
                        name: "FK_endpoints_applications_app_id",
                        column: x => x.app_id,
                        principalTable: "applications",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "event_types",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    app_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    description = table.Column<string>(type: "text", nullable: true),
                    schema_json = table.Column<string>(type: "jsonb", nullable: true),
                    is_archived = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_event_types", x => x.id);
                    table.ForeignKey(
                        name: "FK_event_types_applications_app_id",
                        column: x => x.app_id,
                        principalTable: "applications",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "endpoint_health",
                columns: table => new
                {
                    endpoint_id = table.Column<Guid>(type: "uuid", nullable: false),
                    circuit_state = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false, defaultValue: "Closed"),
                    consecutive_failures = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    last_failure_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    last_success_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    cooldown_until = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_endpoint_health", x => x.endpoint_id);
                    table.ForeignKey(
                        name: "FK_endpoint_health_endpoints_endpoint_id",
                        column: x => x.endpoint_id,
                        principalTable: "endpoints",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "endpoint_event_types",
                columns: table => new
                {
                    endpoint_id = table.Column<Guid>(type: "uuid", nullable: false),
                    event_type_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_endpoint_event_types", x => new { x.endpoint_id, x.event_type_id });
                    table.ForeignKey(
                        name: "FK_endpoint_event_types_endpoints_endpoint_id",
                        column: x => x.endpoint_id,
                        principalTable: "endpoints",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_endpoint_event_types_event_types_event_type_id",
                        column: x => x.event_type_id,
                        principalTable: "event_types",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "messages",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    app_id = table.Column<Guid>(type: "uuid", nullable: false),
                    endpoint_id = table.Column<Guid>(type: "uuid", nullable: false),
                    event_type_id = table.Column<Guid>(type: "uuid", nullable: false),
                    event_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    idempotency_key = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    payload = table.Column<string>(type: "jsonb", nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false, defaultValue: "Pending"),
                    attempt_count = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    max_retries = table.Column<int>(type: "integer", nullable: false, defaultValue: 7),
                    scheduled_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()"),
                    locked_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    locked_by = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    delivered_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_messages", x => x.id);
                    table.ForeignKey(
                        name: "FK_messages_applications_app_id",
                        column: x => x.app_id,
                        principalTable: "applications",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_messages_endpoints_endpoint_id",
                        column: x => x.endpoint_id,
                        principalTable: "endpoints",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_messages_event_types_event_type_id",
                        column: x => x.event_type_id,
                        principalTable: "event_types",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "message_attempts",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    message_id = table.Column<Guid>(type: "uuid", nullable: false),
                    endpoint_id = table.Column<Guid>(type: "uuid", nullable: false),
                    attempt_number = table.Column<int>(type: "integer", nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    status_code = table.Column<int>(type: "integer", nullable: true),
                    request_headers = table.Column<string>(type: "jsonb", nullable: true),
                    response_body = table.Column<string>(type: "text", nullable: true),
                    error = table.Column<string>(type: "text", nullable: true),
                    latency_ms = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_message_attempts", x => x.id);
                    table.ForeignKey(
                        name: "FK_message_attempts_endpoints_endpoint_id",
                        column: x => x.endpoint_id,
                        principalTable: "endpoints",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_message_attempts_messages_message_id",
                        column: x => x.message_id,
                        principalTable: "messages",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_applications_api_key_prefix",
                table: "applications",
                column: "api_key_prefix",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_dashboard_users_email",
                table: "dashboard_users",
                column: "email",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_endpoint_event_types_event_type_id",
                table: "endpoint_event_types",
                column: "event_type_id");

            migrationBuilder.CreateIndex(
                name: "idx_endpoints_app_id",
                table: "endpoints",
                column: "app_id");

            migrationBuilder.CreateIndex(
                name: "idx_endpoints_status",
                table: "endpoints",
                columns: new[] { "app_id", "status" });

            migrationBuilder.CreateIndex(
                name: "idx_event_types_app_id",
                table: "event_types",
                column: "app_id");

            migrationBuilder.CreateIndex(
                name: "IX_event_types_app_id_name",
                table: "event_types",
                columns: new[] { "app_id", "name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "idx_attempts_endpoint_status",
                table: "message_attempts",
                columns: new[] { "endpoint_id", "status", "created_at" },
                descending: new[] { false, false, true });

            migrationBuilder.CreateIndex(
                name: "idx_attempts_message_id",
                table: "message_attempts",
                column: "message_id");

            migrationBuilder.CreateIndex(
                name: "idx_messages_app_endpoint",
                table: "messages",
                columns: new[] { "app_id", "endpoint_id" });

            migrationBuilder.CreateIndex(
                name: "idx_messages_app_event_type",
                table: "messages",
                columns: new[] { "app_id", "event_type_id" });

            migrationBuilder.CreateIndex(
                name: "idx_messages_app_idempotency",
                table: "messages",
                columns: new[] { "app_id", "idempotency_key" },
                filter: "idempotency_key IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "idx_messages_app_status",
                table: "messages",
                columns: new[] { "app_id", "status" });

            migrationBuilder.CreateIndex(
                name: "idx_messages_created_at",
                table: "messages",
                columns: new[] { "app_id", "created_at" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "idx_messages_queue",
                table: "messages",
                column: "scheduled_at",
                filter: "status = 'Pending'");

            migrationBuilder.CreateIndex(
                name: "idx_messages_stale_locks",
                table: "messages",
                column: "locked_at",
                filter: "status = 'Sending' AND locked_at IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_messages_endpoint_id",
                table: "messages",
                column: "endpoint_id");

            migrationBuilder.CreateIndex(
                name: "IX_messages_event_type_id",
                table: "messages",
                column: "event_type_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "dashboard_users");

            migrationBuilder.DropTable(
                name: "endpoint_event_types");

            migrationBuilder.DropTable(
                name: "endpoint_health");

            migrationBuilder.DropTable(
                name: "message_attempts");

            migrationBuilder.DropTable(
                name: "messages");

            migrationBuilder.DropTable(
                name: "endpoints");

            migrationBuilder.DropTable(
                name: "event_types");

            migrationBuilder.DropTable(
                name: "applications");
        }
    }
}

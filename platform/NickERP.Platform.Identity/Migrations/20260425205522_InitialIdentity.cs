using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NickERP.Platform.Identity.Migrations
{
    /// <inheritdoc />
    public partial class InitialIdentity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "identity");

            migrationBuilder.CreateTable(
                name: "app_scopes",
                schema: "identity",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    app_name = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    tenant_id = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_app_scopes", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "service_tokens",
                schema: "identity",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    token_client_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    display_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    purpose = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    last_seen_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    tenant_id = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_service_tokens", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "users",
                schema: "identity",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    email = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: false),
                    normalized_email = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: false),
                    display_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    last_seen_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    tenant_id = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_users", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "service_token_scopes",
                schema: "identity",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    service_token_identity_id = table.Column<Guid>(type: "uuid", nullable: false),
                    app_scope_code = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    granted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    granted_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    revoked_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    revoked_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_service_token_scopes", x => x.id);
                    table.ForeignKey(
                        name: "FK_service_token_scopes_service_tokens_service_token_identity_~",
                        column: x => x.service_token_identity_id,
                        principalSchema: "identity",
                        principalTable: "service_tokens",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "user_scopes",
                schema: "identity",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    identity_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    app_scope_code = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    granted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    granted_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    revoked_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    revoked_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    notes = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    tenant_id = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_user_scopes", x => x.id);
                    table.ForeignKey(
                        name: "FK_user_scopes_users_identity_user_id",
                        column: x => x.identity_user_id,
                        principalSchema: "identity",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_app_scopes_tenant_id_app_name_is_active",
                schema: "identity",
                table: "app_scopes",
                columns: new[] { "tenant_id", "app_name", "is_active" });

            migrationBuilder.CreateIndex(
                name: "IX_app_scopes_tenant_id_code",
                schema: "identity",
                table: "app_scopes",
                columns: new[] { "tenant_id", "code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_service_token_scopes_service_token_identity_id",
                schema: "identity",
                table: "service_token_scopes",
                column: "service_token_identity_id");

            migrationBuilder.CreateIndex(
                name: "IX_service_token_scopes_tenant_id_service_token_identity_id_ap~",
                schema: "identity",
                table: "service_token_scopes",
                columns: new[] { "tenant_id", "service_token_identity_id", "app_scope_code" },
                unique: true,
                filter: "revoked_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_service_tokens_tenant_id_is_active",
                schema: "identity",
                table: "service_tokens",
                columns: new[] { "tenant_id", "is_active" });

            migrationBuilder.CreateIndex(
                name: "IX_service_tokens_tenant_id_token_client_id",
                schema: "identity",
                table: "service_tokens",
                columns: new[] { "tenant_id", "token_client_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_user_scopes_identity_user_id",
                schema: "identity",
                table: "user_scopes",
                column: "identity_user_id");

            migrationBuilder.CreateIndex(
                name: "IX_user_scopes_tenant_id_app_scope_code",
                schema: "identity",
                table: "user_scopes",
                columns: new[] { "tenant_id", "app_scope_code" });

            migrationBuilder.CreateIndex(
                name: "IX_user_scopes_tenant_id_identity_user_id_app_scope_code",
                schema: "identity",
                table: "user_scopes",
                columns: new[] { "tenant_id", "identity_user_id", "app_scope_code" },
                unique: true,
                filter: "revoked_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_users_last_seen_at",
                schema: "identity",
                table: "users",
                column: "last_seen_at");

            migrationBuilder.CreateIndex(
                name: "IX_users_tenant_id_is_active",
                schema: "identity",
                table: "users",
                columns: new[] { "tenant_id", "is_active" });

            migrationBuilder.CreateIndex(
                name: "IX_users_tenant_id_normalized_email",
                schema: "identity",
                table: "users",
                columns: new[] { "tenant_id", "normalized_email" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "app_scopes",
                schema: "identity");

            migrationBuilder.DropTable(
                name: "service_token_scopes",
                schema: "identity");

            migrationBuilder.DropTable(
                name: "user_scopes",
                schema: "identity");

            migrationBuilder.DropTable(
                name: "service_tokens",
                schema: "identity");

            migrationBuilder.DropTable(
                name: "users",
                schema: "identity");
        }
    }
}

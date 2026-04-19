using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NickComms.Gateway.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "api_keys",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    app_name = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    key_hash = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    key_prefix = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    last_used_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_api_keys", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "email_messages",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    from_email = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    from_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    to_email = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    to_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    subject = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    body = table.Column<string>(type: "text", nullable: false),
                    is_html = table.Column<bool>(type: "boolean", nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    batch_id = table.Column<Guid>(type: "uuid", nullable: true),
                    client_app = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    client_reference = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    error_message = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    sent_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_email_messages", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "otp_sessions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    phone_number = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    hubtel_request_id = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    prefix = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    client_app = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    verified = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_otp_sessions", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "sms_messages",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    sender_id = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    recipient = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    content = table.Column<string>(type: "text", nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    hubtel_message_id = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    hubtel_rate = table.Column<decimal>(type: "numeric", nullable: true),
                    batch_id = table.Column<Guid>(type: "uuid", nullable: true),
                    client_app = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    client_reference = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    error_message = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    sent_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_sms_messages", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_api_keys_app_name",
                table: "api_keys",
                column: "app_name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_api_keys_key_hash",
                table: "api_keys",
                column: "key_hash");

            migrationBuilder.CreateIndex(
                name: "IX_email_messages_batch_id",
                table: "email_messages",
                column: "batch_id",
                filter: "batch_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_email_messages_client_app_created_at",
                table: "email_messages",
                columns: new[] { "client_app", "created_at" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "IX_email_messages_to_email",
                table: "email_messages",
                column: "to_email");

            migrationBuilder.CreateIndex(
                name: "IX_otp_sessions_phone_number_created_at",
                table: "otp_sessions",
                columns: new[] { "phone_number", "created_at" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "IX_sms_messages_batch_id",
                table: "sms_messages",
                column: "batch_id",
                filter: "batch_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_sms_messages_client_app_created_at",
                table: "sms_messages",
                columns: new[] { "client_app", "created_at" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "IX_sms_messages_recipient",
                table: "sms_messages",
                column: "recipient");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "api_keys");

            migrationBuilder.DropTable(
                name: "email_messages");

            migrationBuilder.DropTable(
                name: "otp_sessions");

            migrationBuilder.DropTable(
                name: "sms_messages");
        }
    }
}

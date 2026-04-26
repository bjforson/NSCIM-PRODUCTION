using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NickComms.Gateway.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddOutboxColumns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "attempt_count",
                table: "sms_messages",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTime>(
                name: "next_attempt_at",
                table: "sms_messages",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AddColumn<DateTime>(
                name: "processing_started_at",
                table: "sms_messages",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "attachments_json",
                table: "email_messages",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "attempt_count",
                table: "email_messages",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTime>(
                name: "next_attempt_at",
                table: "email_messages",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AddColumn<DateTime>(
                name: "processing_started_at",
                table: "email_messages",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_sms_messages_outbox",
                table: "sms_messages",
                columns: new[] { "status", "next_attempt_at" });

            migrationBuilder.CreateIndex(
                name: "ix_email_messages_outbox",
                table: "email_messages",
                columns: new[] { "status", "next_attempt_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_sms_messages_outbox",
                table: "sms_messages");

            migrationBuilder.DropIndex(
                name: "ix_email_messages_outbox",
                table: "email_messages");

            migrationBuilder.DropColumn(
                name: "attempt_count",
                table: "sms_messages");

            migrationBuilder.DropColumn(
                name: "next_attempt_at",
                table: "sms_messages");

            migrationBuilder.DropColumn(
                name: "processing_started_at",
                table: "sms_messages");

            migrationBuilder.DropColumn(
                name: "attachments_json",
                table: "email_messages");

            migrationBuilder.DropColumn(
                name: "attempt_count",
                table: "email_messages");

            migrationBuilder.DropColumn(
                name: "next_attempt_at",
                table: "email_messages");

            migrationBuilder.DropColumn(
                name: "processing_started_at",
                table: "email_messages");
        }
    }
}

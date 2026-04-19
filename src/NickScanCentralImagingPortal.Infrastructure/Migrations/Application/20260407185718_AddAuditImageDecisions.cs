using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace NickScanCentralImagingPortal.Infrastructure.Migrations.Application
{
    /// <inheritdoc />
    public partial class AddAuditImageDecisions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "auditimagedecisions",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    auditdecisionid = table.Column<int>(type: "integer", nullable: false),
                    containernumber = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    scannertype = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    imageindex = table.Column<int>(type: "integer", nullable: false),
                    decision = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    notes = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    auditedby = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    auditedatutc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    createdatutc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_auditimagedecisions", x => x.id);
                    table.ForeignKey(
                        name: "fk_auditimagedecisions_auditdecisions_auditdecisionid",
                        column: x => x.auditdecisionid,
                        principalTable: "auditdecisions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_auditimagedecisions_auditdecisionid",
                table: "auditimagedecisions",
                column: "auditdecisionid");

            migrationBuilder.CreateIndex(
                name: "ix_auditimagedecisions_auditdecisionid_imageindex",
                table: "auditimagedecisions",
                columns: new[] { "auditdecisionid", "imageindex" });

            migrationBuilder.CreateIndex(
                name: "ix_auditimagedecisions_containernumber",
                table: "auditimagedecisions",
                column: "containernumber");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "auditimagedecisions");
        }
    }
}

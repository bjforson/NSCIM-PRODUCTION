using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace NickScanCentralImagingPortal.Infrastructure.Migrations.Application
{
    /// <inheritdoc />
    public partial class AddMatchQualityFlags : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "matchqualityflags",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    containernumber = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    scannertype = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    boedocumentid = table.Column<int>(type: "integer", nullable: true),
                    flagtype = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    severity = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    description = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    isresolved = table.Column<bool>(type: "boolean", nullable: false),
                    resolvedby = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    resolvedat = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    resolution = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    resolutionnotes = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    createdatutc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_matchqualityflags", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_matchqualityflags_containernumber",
                table: "matchqualityflags",
                column: "containernumber");

            migrationBuilder.CreateIndex(
                name: "ix_matchqualityflags_createdatutc",
                table: "matchqualityflags",
                column: "createdatutc");

            migrationBuilder.CreateIndex(
                name: "ix_matchqualityflags_flagtype",
                table: "matchqualityflags",
                column: "flagtype");

            migrationBuilder.CreateIndex(
                name: "ix_matchqualityflags_isresolved",
                table: "matchqualityflags",
                column: "isresolved");

            migrationBuilder.CreateIndex(
                name: "ix_matchqualityflags_isresolved_severity_createdatutc",
                table: "matchqualityflags",
                columns: new[] { "isresolved", "severity", "createdatutc" });

            migrationBuilder.CreateIndex(
                name: "ix_matchqualityflags_severity",
                table: "matchqualityflags",
                column: "severity");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "matchqualityflags");
        }
    }
}

using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NickScanCentralImagingPortal.Infrastructure.Migrations.Application
{
    /// <inheritdoc />
    public partial class DropDeadWaveFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "assignedtowaveid",
                table: "wavependingcontainers");

            migrationBuilder.DropColumn(
                name: "autoclosedate",
                table: "analysisparentgroups");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "assignedtowaveid",
                table: "wavependingcontainers",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "autoclosedate",
                table: "analysisparentgroups",
                type: "timestamp with time zone",
                nullable: true);
        }
    }
}

using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NickScanCentralImagingPortal.Infrastructure.Migrations.IcumDownloadsDb
{
    /// <inheritdoc />
    public partial class AddIngestionWarningsColumns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<double>(
                name: "averageaccuracypercent",
                table: "downloadedfiles",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "lowestaccuracycontainer",
                table: "downloadedfiles",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "lowestaccuracypercent",
                table: "downloadedfiles",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "partialdocumentcount",
                table: "downloadedfiles",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "perfectdocumentcount",
                table: "downloadedfiles",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "verificationdetails",
                table: "downloadedfiles",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "verifieddocumentcount",
                table: "downloadedfiles",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "cmrupgradedat",
                table: "boedocuments",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "containerremarks",
                table: "boedocuments",
                type: "character varying(4000)",
                maxLength: 4000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "containersize",
                table: "boedocuments",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "containerstatus",
                table: "boedocuments",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "driverlicense",
                table: "boedocuments",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "drivername",
                table: "boedocuments",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "hasingestionwarnings",
                table: "boedocuments",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "ingestionwarnings",
                table: "boedocuments",
                type: "character varying(4000)",
                maxLength: 4000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "masterblnumber",
                table: "boedocuments",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "originalclearancetype",
                table: "boedocuments",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "sealnumber",
                table: "boedocuments",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "truckplatenumber",
                table: "boedocuments",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_boedocument_hasingestionwarnings",
                table: "boedocuments",
                column: "hasingestionwarnings",
                filter: "\"HasIngestionWarnings\" = true");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_boedocument_hasingestionwarnings",
                table: "boedocuments");

            migrationBuilder.DropColumn(
                name: "averageaccuracypercent",
                table: "downloadedfiles");

            migrationBuilder.DropColumn(
                name: "lowestaccuracycontainer",
                table: "downloadedfiles");

            migrationBuilder.DropColumn(
                name: "lowestaccuracypercent",
                table: "downloadedfiles");

            migrationBuilder.DropColumn(
                name: "partialdocumentcount",
                table: "downloadedfiles");

            migrationBuilder.DropColumn(
                name: "perfectdocumentcount",
                table: "downloadedfiles");

            migrationBuilder.DropColumn(
                name: "verificationdetails",
                table: "downloadedfiles");

            migrationBuilder.DropColumn(
                name: "verifieddocumentcount",
                table: "downloadedfiles");

            migrationBuilder.DropColumn(
                name: "cmrupgradedat",
                table: "boedocuments");

            migrationBuilder.DropColumn(
                name: "containerremarks",
                table: "boedocuments");

            migrationBuilder.DropColumn(
                name: "containersize",
                table: "boedocuments");

            migrationBuilder.DropColumn(
                name: "containerstatus",
                table: "boedocuments");

            migrationBuilder.DropColumn(
                name: "driverlicense",
                table: "boedocuments");

            migrationBuilder.DropColumn(
                name: "drivername",
                table: "boedocuments");

            migrationBuilder.DropColumn(
                name: "hasingestionwarnings",
                table: "boedocuments");

            migrationBuilder.DropColumn(
                name: "ingestionwarnings",
                table: "boedocuments");

            migrationBuilder.DropColumn(
                name: "masterblnumber",
                table: "boedocuments");

            migrationBuilder.DropColumn(
                name: "originalclearancetype",
                table: "boedocuments");

            migrationBuilder.DropColumn(
                name: "sealnumber",
                table: "boedocuments");

            migrationBuilder.DropColumn(
                name: "truckplatenumber",
                table: "boedocuments");
        }
    }
}

using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NickScanCentralImagingPortal.Infrastructure.Migrations.Application
{
    /// <inheritdoc />
    public partial class AddSplitIntegrationColumns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // ── AnalysisRecords: per-record split tracking ──

            migrationBuilder.AddColumn<bool>(
                name: "ismulticontainerscan",
                table: "analysisrecords",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<Guid>(
                name: "splitjobid",
                table: "analysisrecords",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "splitposition",
                table: "analysisrecords",
                type: "character varying(10)",
                maxLength: 10,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "splitstatus",
                table: "analysisrecords",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "splitresultid",
                table: "analysisrecords",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "splitoptiona_resultid",
                table: "analysisrecords",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "splitoptionb_resultid",
                table: "analysisrecords",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_analysisrecords_splitjobid",
                table: "analysisrecords",
                column: "splitjobid",
                filter: "\"SplitJobId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_analysisrecords_splitstatus",
                table: "analysisrecords",
                column: "splitstatus",
                filter: "\"SplitStatus\" IS NOT NULL");

            // ── ImageAnalysisDecisions: link decision back to split ──

            migrationBuilder.AddColumn<Guid>(
                name: "splitjobid",
                table: "imageanalysisdecisions",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "splitresultid",
                table: "imageanalysisdecisions",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "splitchoicestrategy",
                table: "imageanalysisdecisions",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_analysisrecords_splitjobid",
                table: "analysisrecords");

            migrationBuilder.DropIndex(
                name: "ix_analysisrecords_splitstatus",
                table: "analysisrecords");

            migrationBuilder.DropColumn(name: "ismulticontainerscan", table: "analysisrecords");
            migrationBuilder.DropColumn(name: "splitjobid", table: "analysisrecords");
            migrationBuilder.DropColumn(name: "splitposition", table: "analysisrecords");
            migrationBuilder.DropColumn(name: "splitstatus", table: "analysisrecords");
            migrationBuilder.DropColumn(name: "splitresultid", table: "analysisrecords");
            migrationBuilder.DropColumn(name: "splitoptiona_resultid", table: "analysisrecords");
            migrationBuilder.DropColumn(name: "splitoptionb_resultid", table: "analysisrecords");

            migrationBuilder.DropColumn(name: "splitjobid", table: "imageanalysisdecisions");
            migrationBuilder.DropColumn(name: "splitresultid", table: "imageanalysisdecisions");
            migrationBuilder.DropColumn(name: "splitchoicestrategy", table: "imageanalysisdecisions");
        }
    }
}

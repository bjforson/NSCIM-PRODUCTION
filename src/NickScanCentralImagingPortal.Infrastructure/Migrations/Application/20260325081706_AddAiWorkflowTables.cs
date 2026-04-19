using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace NickScanCentralImagingPortal.Infrastructure.Migrations.Application
{
    /// <inheritdoc />
    public partial class AddAiWorkflowTables : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "originalscanrecordid",
                table: "fs6000scans",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "truckplate",
                table: "fs6000scans",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "errorpattern",
                table: "errorinvestigations",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(500)",
                oldMaxLength: 500);

            migrationBuilder.AlterColumn<string>(
                name: "consolidationdetails",
                table: "containercompletenessstatuses",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(200)",
                oldMaxLength: 200,
                oldNullable: true);

            migrationBuilder.AddColumn<int>(
                name: "originalscanrecordid",
                table: "asescans",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "aidatasetsnapshots",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    createdatutc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    createdby = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    filterjson = table.Column<string>(type: "text", nullable: false),
                    schemaversion = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    rowcountestimate = table.Column<int>(type: "integer", nullable: false),
                    exportpath = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    checksumsha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    notes = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_aidatasetsnapshots", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "aiimageanalysissuggestions",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    analysisgroupid = table.Column<Guid>(type: "uuid", nullable: true),
                    containernumber = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    scannertype = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    groupidentifier = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    modelid = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    modelversion = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    featureversion = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    suggestionpayloadjson = table.Column<string>(type: "text", nullable: true),
                    suggesteddecision = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    confidence = table.Column<double>(type: "double precision", nullable: true),
                    tier = table.Column<int>(type: "integer", nullable: false),
                    shadowmode = table.Column<bool>(type: "boolean", nullable: false),
                    createdatutc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    humanfinaldecision = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    humanreviewedby = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    resolvedatutc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    correctionreason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    resolveddiffersfromsuggestion = table.Column<bool>(type: "boolean", nullable: true),
                    eligiblefortrainingexport = table.Column<bool>(type: "boolean", nullable: false),
                    datasetoptin = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_aiimageanalysissuggestions", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "originalscanrecords",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    scannertype = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    originalcontainernumbers = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    derivedrecordcount = table.Column<int>(type: "integer", nullable: false),
                    picnumber = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    inspectionid = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    rawdata = table.Column<string>(type: "text", nullable: true),
                    sourcefilepath = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    scantime = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ingestedat = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_originalscanrecords", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_fs6000scans_originalscanrecordid",
                table: "fs6000scans",
                column: "originalscanrecordid");

            migrationBuilder.CreateIndex(
                name: "ix_asescans_originalscanrecordid",
                table: "asescans",
                column: "originalscanrecordid");

            migrationBuilder.CreateIndex(
                name: "ix_aidatasetsnapshots_createdatutc",
                table: "aidatasetsnapshots",
                column: "createdatutc");

            migrationBuilder.CreateIndex(
                name: "ix_aiimageanalysissuggestions_analysisgroupid",
                table: "aiimageanalysissuggestions",
                column: "analysisgroupid");

            migrationBuilder.CreateIndex(
                name: "ix_aiimageanalysissuggestions_containernumber_scannertype",
                table: "aiimageanalysissuggestions",
                columns: new[] { "containernumber", "scannertype" });

            migrationBuilder.CreateIndex(
                name: "ix_aiimageanalysissuggestions_resolvedatutc",
                table: "aiimageanalysissuggestions",
                column: "resolvedatutc");

            migrationBuilder.CreateIndex(
                name: "ix_originalscanrecords_ingestedat",
                table: "originalscanrecords",
                column: "ingestedat");

            migrationBuilder.CreateIndex(
                name: "ix_originalscanrecords_inspectionid",
                table: "originalscanrecords",
                column: "inspectionid");

            migrationBuilder.CreateIndex(
                name: "ix_originalscanrecords_originalcontainernumbers",
                table: "originalscanrecords",
                column: "originalcontainernumbers");

            migrationBuilder.CreateIndex(
                name: "ix_originalscanrecords_picnumber",
                table: "originalscanrecords",
                column: "picnumber");

            migrationBuilder.CreateIndex(
                name: "ix_originalscanrecords_scannertype",
                table: "originalscanrecords",
                column: "scannertype");

            migrationBuilder.AddForeignKey(
                name: "fk_asescans_originalscanrecords_originalscanrecordid",
                table: "asescans",
                column: "originalscanrecordid",
                principalTable: "originalscanrecords",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "fk_fs6000scans_originalscanrecords_originalscanrecordid",
                table: "fs6000scans",
                column: "originalscanrecordid",
                principalTable: "originalscanrecords",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_asescans_originalscanrecords_originalscanrecordid",
                table: "asescans");

            migrationBuilder.DropForeignKey(
                name: "fk_fs6000scans_originalscanrecords_originalscanrecordid",
                table: "fs6000scans");

            migrationBuilder.DropTable(
                name: "aidatasetsnapshots");

            migrationBuilder.DropTable(
                name: "aiimageanalysissuggestions");

            migrationBuilder.DropTable(
                name: "originalscanrecords");

            migrationBuilder.DropIndex(
                name: "ix_fs6000scans_originalscanrecordid",
                table: "fs6000scans");

            migrationBuilder.DropIndex(
                name: "ix_asescans_originalscanrecordid",
                table: "asescans");

            migrationBuilder.DropColumn(
                name: "originalscanrecordid",
                table: "fs6000scans");

            migrationBuilder.DropColumn(
                name: "truckplate",
                table: "fs6000scans");

            migrationBuilder.DropColumn(
                name: "originalscanrecordid",
                table: "asescans");

            migrationBuilder.AlterColumn<string>(
                name: "errorpattern",
                table: "errorinvestigations",
                type: "character varying(500)",
                maxLength: 500,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AlterColumn<string>(
                name: "consolidationdetails",
                table: "containercompletenessstatuses",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);
        }
    }
}

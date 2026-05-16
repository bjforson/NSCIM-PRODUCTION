using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace NickScanCentralImagingPortal.Infrastructure.Migrations.Application
{
    public partial class AddScanImageIdentity : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "scanimageassets",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    originalscanrecordid = table.Column<int>(type: "integer", nullable: true),
                    scannertype = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    scannernativeid = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    sourcecontainerlabel = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    assetkind = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    storagekind = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    sourcepath = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    localpath = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    mimetype = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    imagedisplayname = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    filesizebytes = table.Column<long>(type: "bigint", nullable: true),
                    contenthash = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    scantimeutc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    createdatutc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updatedatutc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_scanimageassets", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "sourcescancontainerlinks",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    scanimageassetid = table.Column<Guid>(type: "uuid", nullable: false),
                    originalscanrecordid = table.Column<int>(type: "integer", nullable: true),
                    scannertype = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    scannernativeid = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    containernumber = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    normalizedcontainernumber = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    sourcecontainerlabel = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    position = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    confidence = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    splitjobid = table.Column<Guid>(type: "uuid", nullable: true),
                    splitresultid = table.Column<Guid>(type: "uuid", nullable: true),
                    boedocumentid = table.Column<int>(type: "integer", nullable: true),
                    recordexpectedcontainerid = table.Column<int>(type: "integer", nullable: true),
                    createdatutc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updatedatutc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_sourcescancontainerlinks", x => x.id);
                    table.ForeignKey(
                        name: "fk_sscl_scanimageasset",
                        column: x => x.scanimageassetid,
                        principalTable: "scanimageassets",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            AddIdentityColumns(
                migrationBuilder,
                table: "analysisrecords",
                includeSplitColumns: false,
                includePositionColumn: false);
            AddIdentityColumns(
                migrationBuilder,
                table: "containercompletenessstatuses",
                includeSplitColumns: false,
                includePositionColumn: false);
            AddIdentityColumns(
                migrationBuilder,
                table: "icumssubmissionqueues",
                includeSplitColumns: false,
                includePositionColumn: false);
            AddIdentityColumns(
                migrationBuilder,
                table: "recordexpectedcontainers",
                includeSplitColumns: false,
                includePositionColumn: false);
            AddIdentityColumns(
                migrationBuilder,
                table: "containerscanqueues",
                includeSplitColumns: true,
                includePositionColumn: true);

            migrationBuilder.CreateIndex(
                name: "ix_scanimageassets_originalscanrecordid",
                table: "scanimageassets",
                column: "originalscanrecordid",
                filter: "\"originalscanrecordid\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_scanimageassets_scannertype_scannernativeid_assetkind",
                table: "scanimageassets",
                columns: new[] { "scannertype", "scannernativeid", "assetkind" },
                filter: "\"scannernativeid\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_scanimageassets_scantimeutc",
                table: "scanimageassets",
                column: "scantimeutc");

            migrationBuilder.CreateIndex(
                name: "ix_sscl_normcontainer_scannertype",
                table: "sourcescancontainerlinks",
                columns: new[] { "normalizedcontainernumber", "scannertype" });

            migrationBuilder.CreateIndex(
                name: "ix_sourcescancontainerlinks_originalscanrecordid",
                table: "sourcescancontainerlinks",
                column: "originalscanrecordid",
                filter: "\"originalscanrecordid\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_sourcescancontainerlinks_recordexpectedcontainerid",
                table: "sourcescancontainerlinks",
                column: "recordexpectedcontainerid",
                filter: "\"recordexpectedcontainerid\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_sourcescancontainerlinks_scanimageassetid",
                table: "sourcescancontainerlinks",
                column: "scanimageassetid");

            migrationBuilder.CreateIndex(
                name: "ix_sscl_scanimageassetid_normcontainer",
                table: "sourcescancontainerlinks",
                columns: new[] { "scanimageassetid", "normalizedcontainernumber" },
                unique: true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            DropIdentityColumns(migrationBuilder, "containerscanqueues", includeSplitColumns: true, includePositionColumn: true);
            DropIdentityColumns(migrationBuilder, "recordexpectedcontainers", includeSplitColumns: false, includePositionColumn: false);
            DropIdentityColumns(migrationBuilder, "icumssubmissionqueues", includeSplitColumns: false, includePositionColumn: false);
            DropIdentityColumns(migrationBuilder, "containercompletenessstatuses", includeSplitColumns: false, includePositionColumn: false);
            DropIdentityColumns(migrationBuilder, "analysisrecords", includeSplitColumns: false, includePositionColumn: false);

            migrationBuilder.DropTable(name: "sourcescancontainerlinks");
            migrationBuilder.DropTable(name: "scanimageassets");
        }

        private static void AddIdentityColumns(
            MigrationBuilder migrationBuilder,
            string table,
            bool includeSplitColumns,
            bool includePositionColumn)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "scanimageassetid",
                table: table,
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "originalscanrecordid",
                table: table,
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "sourcecontainerlabel",
                table: table,
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: $"ix_{table}_scanimageassetid",
                table: table,
                column: "scanimageassetid",
                filter: "\"scanimageassetid\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: $"ix_{table}_originalscanrecordid",
                table: table,
                column: "originalscanrecordid",
                filter: "\"originalscanrecordid\" IS NOT NULL");

            if (includePositionColumn)
            {
                migrationBuilder.AddColumn<string>(
                    name: "scancontainerposition",
                    table: table,
                    type: "character varying(20)",
                    maxLength: 20,
                    nullable: true);
            }

            if (includeSplitColumns)
            {
                migrationBuilder.AddColumn<Guid>(
                    name: "splitjobid",
                    table: table,
                    type: "uuid",
                    nullable: true);

                migrationBuilder.AddColumn<Guid>(
                    name: "splitresultid",
                    table: table,
                    type: "uuid",
                    nullable: true);

                migrationBuilder.CreateIndex(
                    name: $"ix_{table}_splitjobid",
                    table: table,
                    column: "splitjobid",
                    filter: "\"splitjobid\" IS NOT NULL");
            }
        }

        private static void DropIdentityColumns(
            MigrationBuilder migrationBuilder,
            string table,
            bool includeSplitColumns,
            bool includePositionColumn)
        {
            if (includeSplitColumns)
            {
                migrationBuilder.DropIndex(name: $"ix_{table}_splitjobid", table: table);
                migrationBuilder.DropColumn(name: "splitresultid", table: table);
                migrationBuilder.DropColumn(name: "splitjobid", table: table);
            }

            if (includePositionColumn)
                migrationBuilder.DropColumn(name: "scancontainerposition", table: table);

            migrationBuilder.DropIndex(name: $"ix_{table}_originalscanrecordid", table: table);
            migrationBuilder.DropIndex(name: $"ix_{table}_scanimageassetid", table: table);
            migrationBuilder.DropColumn(name: "sourcecontainerlabel", table: table);
            migrationBuilder.DropColumn(name: "originalscanrecordid", table: table);
            migrationBuilder.DropColumn(name: "scanimageassetid", table: table);
        }
    }
}

using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NickScanCentralImagingPortal.Infrastructure.Migrations.Application
{
    public partial class AddEagleA25ScannerTables : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "eaglea25scans",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    sourcescanid = table.Column<int>(type: "integer", nullable: false),
                    sourcescanguid = table.Column<Guid>(type: "uuid", nullable: false),
                    sourcescanentryid = table.Column<int>(type: "integer", nullable: false),
                    sourcemanifestid = table.Column<int>(type: "integer", nullable: false),
                    sourcemanifestguid = table.Column<Guid>(type: "uuid", nullable: false),
                    accession = table.Column<long>(type: "bigint", nullable: false),
                    scanaccession = table.Column<long>(type: "bigint", nullable: true),
                    cargosystemid = table.Column<int>(type: "integer", nullable: true),
                    locationid = table.Column<int>(type: "integer", nullable: true),
                    scandateutc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    scandatelocal = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    manifestcreatedateutc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    manifestcreatedatelocal = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    cargoidentifier = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    airwaybill = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    flightnumber = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    transittype = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    weight = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    company = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    quantity = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    quantitytype = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    originfrom = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    originto = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    comments = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    datapath = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    dataurl = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    xraydone = table.Column<bool>(type: "boolean", nullable: false),
                    readyinspect = table.Column<bool>(type: "boolean", nullable: false),
                    inspectdone = table.Column<bool>(type: "boolean", nullable: false),
                    inspectsuspicious = table.Column<bool>(type: "boolean", nullable: false),
                    searchfound = table.Column<bool>(type: "boolean", nullable: false),
                    searchdone = table.Column<bool>(type: "boolean", nullable: false),
                    archived = table.Column<bool>(type: "boolean", nullable: false),
                    syncstatus = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    syncedatutc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    createdatutc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updatedatutc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_eaglea25scans", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "eaglea25synclogs",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    startedatutc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    completedatutc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    lastsyncedaccession = table.Column<long>(type: "bigint", nullable: true),
                    scansread = table.Column<int>(type: "integer", nullable: false),
                    scansinserted = table.Column<int>(type: "integer", nullable: false),
                    scansupdated = table.Column<int>(type: "integer", nullable: false),
                    assetsread = table.Column<int>(type: "integer", nullable: false),
                    assetsinserted = table.Column<int>(type: "integer", nullable: false),
                    assetsupdated = table.Column<int>(type: "integer", nullable: false),
                    errormessage = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_eaglea25synclogs", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "eaglea25scanassets",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    eaglea25scanid = table.Column<Guid>(type: "uuid", nullable: false),
                    sourceextfileid = table.Column<int>(type: "integer", nullable: false),
                    sourceextfileguid = table.Column<Guid>(type: "uuid", nullable: false),
                    sourceextfiletypeid = table.Column<int>(type: "integer", nullable: false),
                    filetype = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    isxray = table.Column<bool>(type: "boolean", nullable: false),
                    mimetype = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    description = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    sourcepath = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    resolvedsourcepath = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    sourceurl = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    localpath = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    filesizebytes = table.Column<long>(type: "bigint", nullable: true),
                    sourcecreatedateutc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    syncedatutc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_eaglea25scanassets", x => x.id);
                    table.ForeignKey(
                        name: "fk_eaglea25scanassets_eaglea25scans_eaglea25scanid",
                        column: x => x.eaglea25scanid,
                        principalTable: "eaglea25scans",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(name: "ix_eaglea25scans_accession", table: "eaglea25scans", column: "accession", unique: true);
            migrationBuilder.CreateIndex(name: "ix_eaglea25scans_airwaybill", table: "eaglea25scans", column: "airwaybill");
            migrationBuilder.CreateIndex(name: "ix_eaglea25scans_cargoidentifier", table: "eaglea25scans", column: "cargoidentifier");
            migrationBuilder.CreateIndex(name: "ix_eaglea25scans_scandateutc", table: "eaglea25scans", column: "scandateutc");
            migrationBuilder.CreateIndex(name: "ix_eaglea25scans_sourcemanifestid", table: "eaglea25scans", column: "sourcemanifestid", unique: true);
            migrationBuilder.CreateIndex(name: "ix_eaglea25scans_sourcescanid", table: "eaglea25scans", column: "sourcescanid");
            migrationBuilder.CreateIndex(name: "ix_eaglea25scanassets_eaglea25scanid_filetype", table: "eaglea25scanassets", columns: new[] { "eaglea25scanid", "filetype" });
            migrationBuilder.CreateIndex(name: "ix_eaglea25scanassets_filetype", table: "eaglea25scanassets", column: "filetype");
            migrationBuilder.CreateIndex(name: "ix_eaglea25scanassets_sourceextfileid", table: "eaglea25scanassets", column: "sourceextfileid", unique: true);
            migrationBuilder.CreateIndex(name: "ix_eaglea25synclogs_startedatutc", table: "eaglea25synclogs", column: "startedatutc");
            migrationBuilder.CreateIndex(name: "ix_eaglea25synclogs_status", table: "eaglea25synclogs", column: "status");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "eaglea25scanassets");
            migrationBuilder.DropTable(name: "eaglea25synclogs");
            migrationBuilder.DropTable(name: "eaglea25scans");
        }
    }
}

using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace NickScanCentralImagingPortal.Infrastructure.Migrations.IcumDb
{
    /// <inheritdoc />
    public partial class InitialPostgreSql : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "icumbatchlogs",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    startdate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    enddate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    recordsprocessed = table.Column<int>(type: "integer", nullable: false),
                    status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    errormessage = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    createdat = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updatedat = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_icumbatchlogs", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "icumcontainerdata",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    containernumber = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    boedata = table.Column<string>(type: "text", nullable: true),
                    masterblnumber = table.Column<string>(type: "text", nullable: true),
                    housebl = table.Column<string>(type: "text", nullable: true),
                    rotationnumber = table.Column<string>(type: "text", nullable: true),
                    consigneename = table.Column<string>(type: "text", nullable: true),
                    shippername = table.Column<string>(type: "text", nullable: true),
                    countryoforigin = table.Column<string>(type: "text", nullable: true),
                    totaldutypaid = table.Column<decimal>(type: "numeric", nullable: true),
                    crmslevel = table.Column<string>(type: "text", nullable: true),
                    clearancetype = table.Column<string>(type: "text", nullable: true),
                    declarationnumber = table.Column<string>(type: "text", nullable: true),
                    containerweight = table.Column<decimal>(type: "numeric", nullable: true),
                    containerquantity = table.Column<int>(type: "integer", nullable: true),
                    containeriso = table.Column<string>(type: "text", nullable: true),
                    createdat = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updatedat = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_icumcontainerdata", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "icumdocuments",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    containernumber = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    documenttype = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    documentdata = table.Column<string>(type: "text", nullable: false),
                    createdat = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updatedat = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_icumdocuments", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "icummanifestitems",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    icumcontainerdataid = table.Column<int>(type: "integer", nullable: false),
                    housebl = table.Column<string>(type: "text", nullable: true),
                    hscode = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    description = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    quantity = table.Column<decimal>(type: "numeric", nullable: false),
                    unit = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    weight = table.Column<decimal>(type: "numeric", nullable: false),
                    itemfob = table.Column<decimal>(type: "numeric", nullable: false),
                    itemdutypaid = table.Column<decimal>(type: "numeric", nullable: false),
                    fobcurrency = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    countryoforigin = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    itemno = table.Column<int>(type: "integer", nullable: false),
                    cpc = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    createdat = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updatedat = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_icummanifestitems", x => x.id);
                    table.ForeignKey(
                        name: "fk_icummanifestitems_icumcontainerdata_icumcontainerdataid",
                        column: x => x.icumcontainerdataid,
                        principalTable: "icumcontainerdata",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_icumcontainerdata_clearancetype",
                table: "icumcontainerdata",
                column: "clearancetype");

            migrationBuilder.CreateIndex(
                name: "ix_icumcontainerdata_consigneename",
                table: "icumcontainerdata",
                column: "consigneename");

            migrationBuilder.CreateIndex(
                name: "ix_icumcontainerdata_containernumber",
                table: "icumcontainerdata",
                column: "containernumber",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_icumcontainerdata_countryoforigin",
                table: "icumcontainerdata",
                column: "countryoforigin");

            migrationBuilder.CreateIndex(
                name: "ix_icumcontainerdata_crmslevel",
                table: "icumcontainerdata",
                column: "crmslevel");

            migrationBuilder.CreateIndex(
                name: "ix_icumcontainerdata_declarationnumber",
                table: "icumcontainerdata",
                column: "declarationnumber");

            migrationBuilder.CreateIndex(
                name: "ix_icumcontainerdata_housebl",
                table: "icumcontainerdata",
                column: "housebl");

            migrationBuilder.CreateIndex(
                name: "ix_icumcontainerdata_masterblnumber",
                table: "icumcontainerdata",
                column: "masterblnumber");

            migrationBuilder.CreateIndex(
                name: "ix_icumcontainerdata_rotationnumber",
                table: "icumcontainerdata",
                column: "rotationnumber");

            migrationBuilder.CreateIndex(
                name: "ix_icumcontainerdata_shippername",
                table: "icumcontainerdata",
                column: "shippername");

            migrationBuilder.CreateIndex(
                name: "ix_icummanifestitems_countryoforigin",
                table: "icummanifestitems",
                column: "countryoforigin");

            migrationBuilder.CreateIndex(
                name: "ix_icummanifestitems_hscode",
                table: "icummanifestitems",
                column: "hscode");

            migrationBuilder.CreateIndex(
                name: "ix_icummanifestitems_icumcontainerdataid",
                table: "icummanifestitems",
                column: "icumcontainerdataid");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "icumbatchlogs");

            migrationBuilder.DropTable(
                name: "icumdocuments");

            migrationBuilder.DropTable(
                name: "icummanifestitems");

            migrationBuilder.DropTable(
                name: "icumcontainerdata");
        }
    }
}

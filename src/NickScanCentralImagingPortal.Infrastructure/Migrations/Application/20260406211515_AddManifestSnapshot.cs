using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace NickScanCentralImagingPortal.Infrastructure.Migrations.Application
{
    /// <inheritdoc />
    public partial class AddManifestSnapshot : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "manifestsnapshots",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    imageanalysisdecisionid = table.Column<int>(type: "integer", nullable: false),
                    boedocumentid = table.Column<int>(type: "integer", nullable: true),
                    snapshottakenatutc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    containernumber = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    scannertype = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    masterblnumber = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    houseblnumber = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    rotationnumber = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    declarationnumber = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    regimecode = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    clearancetype = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: true),
                    declaredgoodsdescription = table.Column<string>(type: "text", nullable: true),
                    declaredhscodesjson = table.Column<string>(type: "text", nullable: true),
                    declaredquantitiesjson = table.Column<string>(type: "text", nullable: true),
                    declaredvaluesjson = table.Column<string>(type: "text", nullable: true),
                    declaredlineitemcount = table.Column<int>(type: "integer", nullable: true),
                    totaldeclaredfob = table.Column<decimal>(type: "numeric", nullable: true),
                    fobcurrency = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: true),
                    totaldeclareddutypaid = table.Column<decimal>(type: "numeric", nullable: true),
                    totaldeclaredweight = table.Column<decimal>(type: "numeric", nullable: true),
                    countryoforigin = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    deliveryplace = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    importername = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    importeraddress = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    consigneename = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    consigneeaddress = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    shippername = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    crmslevel = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    isconsolidated = table.Column<bool>(type: "boolean", nullable: true),
                    rawmanifestjson = table.Column<string>(type: "text", nullable: true),
                    source = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_manifestsnapshots", x => x.id);
                    table.ForeignKey(
                        name: "fk_manifestsnapshots_imageanalysisdecisions_imageanalysisdecis~",
                        column: x => x.imageanalysisdecisionid,
                        principalTable: "imageanalysisdecisions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_manifestsnapshots_boedocumentid",
                table: "manifestsnapshots",
                column: "boedocumentid");

            migrationBuilder.CreateIndex(
                name: "ix_manifestsnapshots_containernumber",
                table: "manifestsnapshots",
                column: "containernumber");

            migrationBuilder.CreateIndex(
                name: "ix_manifestsnapshots_imageanalysisdecisionid",
                table: "manifestsnapshots",
                column: "imageanalysisdecisionid");

            migrationBuilder.CreateIndex(
                name: "ix_manifestsnapshots_snapshottakenatutc",
                table: "manifestsnapshots",
                column: "snapshottakenatutc");

            migrationBuilder.CreateIndex(
                name: "ix_manifestsnapshots_source",
                table: "manifestsnapshots",
                column: "source");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "manifestsnapshots");
        }
    }
}

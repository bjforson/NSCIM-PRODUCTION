using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NickScanCentralImagingPortal.Infrastructure.Migrations.Application
{
    /// <inheritdoc />
    public partial class AddWavePendingContainerFk : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddForeignKey(
                name: "fk_wavependingcontainers_analysisparentgroups_parentgroupid",
                table: "wavependingcontainers",
                column: "parentgroupid",
                principalTable: "analysisparentgroups",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_wavependingcontainers_analysisparentgroups_parentgroupid",
                table: "wavependingcontainers");
        }
    }
}

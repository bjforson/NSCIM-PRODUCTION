using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NickScanCentralImagingPortal.Infrastructure.Migrations.Application
{
    /// <inheritdoc />
    public partial class AddDecisionLinkageToContainerAnnotation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "imageanalysisdecisionid",
                table: "containerannotations",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_containerannotations_imageanalysisdecisionid",
                table: "containerannotations",
                column: "imageanalysisdecisionid");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_containerannotations_imageanalysisdecisionid",
                table: "containerannotations");

            migrationBuilder.DropColumn(
                name: "imageanalysisdecisionid",
                table: "containerannotations");
        }
    }
}

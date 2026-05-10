using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NickScanCentralImagingPortal.Infrastructure.Migrations.Application
{
    public partial class AddActiveAssignmentUniqueIndex : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DO $$
                BEGIN
                    IF EXISTS (
                        SELECT 1
                        FROM analysisassignments
                        WHERE state = 'Active'
                        GROUP BY groupid
                        HAVING COUNT(*) > 1
                    ) THEN
                        RAISE EXCEPTION 'Cannot create ux_analysisassignments_active_group: duplicate active assignments exist. Release or expire duplicate rows first.';
                    END IF;
                END $$;
                """);

            migrationBuilder.CreateIndex(
                name: "ux_analysisassignments_active_group",
                table: "analysisassignments",
                column: "groupid",
                unique: true,
                filter: "\"state\" = 'Active'");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ux_analysisassignments_active_group",
                table: "analysisassignments");
        }
    }
}

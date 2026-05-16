using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NickScanCentralImagingPortal.Infrastructure.Migrations.Application
{
    /// <summary>
    /// Adds stable alert fingerprints so repeated detection cycles update one
    /// open incident instead of creating and emailing a fresh dashboard alert.
    /// </summary>
    public partial class AddDashboardAlertKeys : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "alertkey",
                table: "dashboardalerts",
                type: "character varying(256)",
                maxLength: 256,
                nullable: false,
                defaultValue: "");

            migrationBuilder.Sql(@"
UPDATE dashboardalerts
SET alertkey = CASE
    WHEN type = 'DataIntegrity' THEN 'DataIntegrity:Dashboard'
    WHEN type = 'AuditPoolEmpty' THEN 'AuditPoolEmpty'
    WHEN type = 'Bottleneck' AND title ILIKE '%Ready stage%' THEN 'Bottleneck:Ready'
    WHEN type = 'Bottleneck' AND title ILIKE '%Audit stage%' THEN 'Bottleneck:Audit'
    WHEN type = 'DriftSweepHighCounts' THEN 'DriftSweepHighCounts'
    ELSE LEFT(type || ':' || title, 256)
END
WHERE alertkey = '';
");

            migrationBuilder.Sql(@"
WITH ranked_open AS (
    SELECT id,
           ROW_NUMBER() OVER (PARTITION BY alertkey ORDER BY raisedatutc DESC, id DESC) AS rn
    FROM dashboardalerts
    WHERE acknowledgedatutc IS NULL
)
UPDATE dashboardalerts d
SET acknowledgedatutc = d.raisedatutc,
    acknowledgedby = 'system-alertkey-dedupe'
FROM ranked_open r
WHERE d.id = r.id
  AND r.rn > 1;
");

            migrationBuilder.CreateIndex(
                name: "ix_dashboardalerts_alertkey",
                table: "dashboardalerts",
                column: "alertkey");

            migrationBuilder.CreateIndex(
                name: "ix_dashboardalerts_alertkey_ack_raisedatutc",
                table: "dashboardalerts",
                columns: new[] { "alertkey", "acknowledgedatutc", "raisedatutc" });
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_dashboardalerts_alertkey",
                table: "dashboardalerts");

            migrationBuilder.DropIndex(
                name: "ix_dashboardalerts_alertkey_ack_raisedatutc",
                table: "dashboardalerts");

            migrationBuilder.DropColumn(
                name: "alertkey",
                table: "dashboardalerts");
        }
    }
}

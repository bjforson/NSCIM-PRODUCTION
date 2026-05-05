using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace NickScanCentralImagingPortal.Infrastructure.Migrations.Application
{
    /// <summary>
    /// Sprint 5G3 / audit finding 8.25 — persist dashboard alerts so off-hours
    /// incidents survive when no operator has the dashboard open. Email-on-
    /// Critical wiring lives in <c>IDashboardAlertService</c> (Services layer).
    ///
    /// Phase-1 tenancy: <c>tenant_id</c> column gets a server-side default of
    /// <c>current_setting('app.tenant_id')::bigint</c> and a fail-closed
    /// <c>tenant_isolation_dashboardalerts</c> RLS policy, matching the
    /// pattern from tools/migrations/phase1-tenancy/20-nickscan-production-rls.sql
    /// + 24-force-rls-and-fail-closed.sql. Don't repeat the
    /// <c>analysisqueueentries</c> / <c>splitter_consensus_corpus</c> mistake
    /// of creating a post-rollout table without tenant_id (audit 7.02 / 7.03).
    /// </summary>
    public partial class AddDashboardAlerts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "dashboardalerts",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    type = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    severity = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    description = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    source = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    raisedatutc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    acknowledgedatutc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    acknowledgedby = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    emailsentatutc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    tenant_id = table.Column<long>(type: "bigint", nullable: false, defaultValueSql: "COALESCE(NULLIF(current_setting('app.tenant_id', true), ''), '1')::bigint")
                },
                constraints: table => table.PrimaryKey("pk_dashboardalerts", x => x.id));

            migrationBuilder.CreateIndex(name: "ix_dashboardalerts_type", table: "dashboardalerts", column: "type");
            migrationBuilder.CreateIndex(name: "ix_dashboardalerts_severity", table: "dashboardalerts", column: "severity");
            migrationBuilder.CreateIndex(name: "ix_dashboardalerts_raisedatutc", table: "dashboardalerts", column: "raisedatutc");
            migrationBuilder.CreateIndex(name: "ix_dashboardalerts_acknowledgedatutc", table: "dashboardalerts", column: "acknowledgedatutc");
            migrationBuilder.CreateIndex(name: "ix_dashboardalerts_type_title_raisedatutc", table: "dashboardalerts", columns: new[] { "type", "title", "raisedatutc" });
            migrationBuilder.CreateIndex(name: "ix_dashboardalerts_tenant_id", table: "dashboardalerts", columns: new[] { "tenant_id", "id" });

            // Phase-1 tenancy RLS — match 20-nickscan-production-rls.sql + 24-force-rls-and-fail-closed.sql
            migrationBuilder.Sql(@"
ALTER TABLE ""dashboardalerts"" ENABLE ROW LEVEL SECURITY;
ALTER TABLE ""dashboardalerts"" FORCE ROW LEVEL SECURITY;
DROP POLICY IF EXISTS ""tenant_isolation_dashboardalerts"" ON ""dashboardalerts"";
CREATE POLICY ""tenant_isolation_dashboardalerts"" ON ""dashboardalerts""
    USING (tenant_id = COALESCE(NULLIF(current_setting('app.tenant_id', true), ''), '0')::bigint)
    WITH CHECK (tenant_id = COALESCE(NULLIF(current_setting('app.tenant_id', true), ''), '0')::bigint);
");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"DROP POLICY IF EXISTS ""tenant_isolation_dashboardalerts"" ON ""dashboardalerts"";");
            migrationBuilder.DropTable(name: "dashboardalerts");
        }
    }
}

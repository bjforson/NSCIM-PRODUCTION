using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace NickScanCentralImagingPortal.Infrastructure.Migrations.Application
{
    /// <inheritdoc />
    public partial class AddDecisionAgent : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "decisionagentauditlogs",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    groupid = table.Column<Guid>(type: "uuid", nullable: false),
                    groupidentifier = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    totalscore = table.Column<double>(type: "double precision", nullable: false),
                    decision = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    isshadowmode = table.Column<bool>(type: "boolean", nullable: false),
                    processingdepthreached = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    conditionresultsjson = table.Column<string>(type: "text", nullable: true),
                    containercount = table.Column<int>(type: "integer", nullable: false),
                    containernumbers = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    decisionids = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    errormessage = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    processingtimems = table.Column<long>(type: "bigint", nullable: false),
                    createdatutc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    reversedatutc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    reversedby = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    reversalreason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_decisionagentauditlogs", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "decisionagentconditions",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    conditionkey = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    evaluatortype = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    weight = table.Column<double>(type: "double precision", nullable: false),
                    enabled = table.Column<bool>(type: "boolean", nullable: false),
                    sortorder = table.Column<int>(type: "integer", nullable: false),
                    dynamicfieldpath = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    dynamicoperator = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    dynamicvalue = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    createdatutc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updatedatutc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_decisionagentconditions", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "decisionagentsettings",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    enabled = table.Column<bool>(type: "boolean", nullable: false),
                    shadowmode = table.Column<bool>(type: "boolean", nullable: false),
                    allownormaldecisions = table.Column<bool>(type: "boolean", nullable: false),
                    allowabnormaldecisions = table.Column<bool>(type: "boolean", nullable: false),
                    normalthreshold = table.Column<double>(type: "double precision", nullable: false),
                    abnormalthreshold = table.Column<double>(type: "double precision", nullable: false),
                    processingdepthdecision = table.Column<bool>(type: "boolean", nullable: false),
                    processingdepthaudit = table.Column<bool>(type: "boolean", nullable: false),
                    processingdepthsubmission = table.Column<bool>(type: "boolean", nullable: false),
                    maxgroupspercycle = table.Column<int>(type: "integer", nullable: false),
                    createdatutc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updatedatutc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_decisionagentsettings", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_decisionagentauditlogs_createdatutc",
                table: "decisionagentauditlogs",
                column: "createdatutc");

            migrationBuilder.CreateIndex(
                name: "ix_decisionagentauditlogs_decision",
                table: "decisionagentauditlogs",
                column: "decision");

            migrationBuilder.CreateIndex(
                name: "ix_decisionagentauditlogs_groupid",
                table: "decisionagentauditlogs",
                column: "groupid");

            migrationBuilder.CreateIndex(
                name: "ix_decisionagentconditions_conditionkey",
                table: "decisionagentconditions",
                column: "conditionkey");

            migrationBuilder.CreateIndex(
                name: "ix_decisionagentconditions_enabled",
                table: "decisionagentconditions",
                column: "enabled");

            // Seed default settings row (disabled, shadow mode on)
            migrationBuilder.Sql(@"
                INSERT INTO decisionagentsettings (enabled, shadowmode, allownormaldecisions, allowabnormaldecisions, normalthreshold, abnormalthreshold, processingdepthdecision, processingdepthaudit, processingdepthsubmission, maxgroupspercycle, createdatutc)
                VALUES (false, true, true, false, 0.2, 0.7, true, false, false, 50, NOW())
            ");

            // Seed 10 built-in risk conditions
            migrationBuilder.Sql(@"
                INSERT INTO decisionagentconditions (name, conditionkey, evaluatortype, weight, enabled, sortorder, dynamicfieldpath, dynamicoperator, dynamicvalue, description, createdatutc)
                VALUES
                    ('CRMS Level Red',              'risk_red',            'BuiltIn', 0.20, true,  1, NULL, NULL, NULL, 'Flags cargo where CRMS risk level is Red — highest customs risk designation.', NOW()),
                    ('CRMS Level Yellow',           'risk_yellow',         'BuiltIn', 0.08, true,  2, NULL, NULL, NULL, 'Flags cargo where CRMS risk level is Yellow — medium customs risk.', NOW()),
                    ('Multiple House BLs',          'multiple_housebl',    'BuiltIn', 0.08, true,  3, NULL, NULL, NULL, 'Flags consolidated cargo with multiple House BLs — multiple consignees in one container.', NOW()),
                    ('Contains Vehicle',            'has_vehicle',         'BuiltIn', 0.10, true,  4, NULL, NULL, NULL, 'Flags cargo containing vehicles — high-duty commodities prone to undervaluation.', NOW()),
                    ('Has Used Items',              'has_used_items',      'BuiltIn', 0.08, true,  5, NULL, NULL, NULL, 'Detects used/second-hand items via HS codes and description keywords — misclassification risk.', NOW()),
                    ('Multiple Line Items',         'multiple_line_items', 'BuiltIn', 0.06, true,  6, NULL, NULL, NULL, 'Flags declarations with more than 3 line items — complexity increases concealment opportunity.', NOW()),
                    ('High-Risk Country of Origin', 'high_risk_country',   'BuiltIn', 0.12, true,  7, NULL, NULL, 'VE,CO,JM,HT,GY,SR', 'Flags cargo from configurable high-risk origin countries (comma-separated ISO codes).', NOW()),
                    ('Vague Goods Description',     'vague_description',   'BuiltIn', 0.08, true,  8, NULL, NULL, 'personal effects,general cargo,miscellaneous,household goods,various,sundry,mixed', 'Flags vague/generic descriptions that may conceal actual cargo contents.', NOW()),
                    ('High-Risk HS Code',           'high_risk_hs_code',   'BuiltIn', 0.10, true,  9, NULL, NULL, '22,24,28,29,36,71,84,85,86,87,93', 'Flags HS code chapters for controlled commodities: alcohol, tobacco, chemicals, precious metals, machinery, vehicles, arms.', NOW()),
                    ('Duty/Value Anomaly',          'duty_value_anomaly',  'BuiltIn', 0.10, true, 10, NULL, NULL, NULL, 'Detects suspiciously low duty-to-value ratios — primary indicator of under-declaration fraud.', NOW())
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "decisionagentauditlogs");

            migrationBuilder.DropTable(
                name: "decisionagentconditions");

            migrationBuilder.DropTable(
                name: "decisionagentsettings");
        }
    }
}

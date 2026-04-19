using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace NickScanCentralImagingPortal.Infrastructure.Migrations.Application
{
    /// <summary>
    /// AI Training Flywheel — Gap 1a (backend slice).
    ///
    /// Introduces controlled-vocabulary lookup tables for inspection findings:
    ///   - threatcategories          (security domain: weapons, drugs, contraband, hazmat, ...)
    ///   - revenueanomalycategories  (revenue assurance: undeclared goods, undervaluation, ...)
    ///
    /// Adds nullable FK columns to imageanalysisdecisions and containerannotations so
    /// that analyst decisions and drawn boxes can be tagged with structured categories
    /// instead of free text. Both columns are nullable so existing rows and existing
    /// front-end clients keep working unchanged.
    ///
    /// Seeds 13 security + 13 revenue categories drawn from the WCO commercial fraud
    /// taxonomy and Ghana customs operational realities. The lists are draft and the
    /// active set will be revised after the SME / GRA review (see
    /// C:\AI\category_review_handout.pdf).
    ///
    /// NOTE on snapshot drift: when this migration was scaffolded, the model snapshot
    /// was found to have drifted from the database — wave-processing tables and columns
    /// (analysisparentgroups, wavependingcontainers, several analysisgroups /
    /// analysissettings columns) had been applied to production via the manual
    /// wave_processing_schema.sql file but never had an EF migration generated for them.
    /// The auto-generated migration tried to bundle those changes here. They have been
    /// removed from this migration body to keep it focused on Gap 1a, BUT the snapshot
    /// now records the wave entities so the drift is closed at the snapshot level. A
    /// follow-up baseline migration may be needed depending on how production was
    /// originally bootstrapped.
    /// </summary>
    public partial class AddThreatAndRevenueAnomalyCategories : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // ── 1. New columns on existing tables ───────────────────────────────────
            migrationBuilder.AddColumn<int>(
                name: "revenueanomalycategoryid",
                table: "imageanalysisdecisions",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "threatcategoryid",
                table: "imageanalysisdecisions",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "revenueanomalycategoryid",
                table: "containerannotations",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "threatcategoryid",
                table: "containerannotations",
                type: "integer",
                nullable: true);

            // ── 2. New lookup tables ────────────────────────────────────────────────
            migrationBuilder.CreateTable(
                name: "revenueanomalycategories",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    code = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    displayname = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    isactive = table.Column<bool>(type: "boolean", nullable: false),
                    sortorder = table.Column<int>(type: "integer", nullable: false),
                    createdatutc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_revenueanomalycategories", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "threatcategories",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    code = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    displayname = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    isactive = table.Column<bool>(type: "boolean", nullable: false),
                    sortorder = table.Column<int>(type: "integer", nullable: false),
                    createdatutc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_threatcategories", x => x.id);
                });

            // ── 3. Indices ──────────────────────────────────────────────────────────
            migrationBuilder.CreateIndex(
                name: "ix_revenueanomalycategories_code",
                table: "revenueanomalycategories",
                column: "code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_revenueanomalycategories_isactive",
                table: "revenueanomalycategories",
                column: "isactive");

            migrationBuilder.CreateIndex(
                name: "ix_revenueanomalycategories_sortorder",
                table: "revenueanomalycategories",
                column: "sortorder");

            migrationBuilder.CreateIndex(
                name: "ix_threatcategories_code",
                table: "threatcategories",
                column: "code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_threatcategories_isactive",
                table: "threatcategories",
                column: "isactive");

            migrationBuilder.CreateIndex(
                name: "ix_threatcategories_sortorder",
                table: "threatcategories",
                column: "sortorder");

            // ── 4. Seed security domain (13 categories) ─────────────────────────────
            // Sourced from C:\AI\inspection_recorder.py + WCO commercial fraud
            // literature + Ghana operational realities (CITES wildlife trade,
            // controlled-pharmaceutical traffic via Ghana, ECOWAS transit). Subject to
            // SME sign-off — see C:\AI\category_review_handout.pdf.
            migrationBuilder.Sql(@"
                INSERT INTO threatcategories (code, displayname, description, isactive, sortorder, createdatutc)
                VALUES
                    ('weapon_firearm',                'Firearm',                       'Guns, parts, ammunition.', true,  1, NOW()),
                    ('weapon_bladed',                 'Bladed weapon',                 'Knives, machetes, swords beyond declared cutlery.', true,  2, NOW()),
                    ('explosive',                     'Explosive',                     'Explosives, detonators, blasting caps, precursors.', true,  3, NOW()),
                    ('narcotic',                      'Narcotic / controlled drug',    'Cannabis, cocaine, heroin, MDMA, methamphetamine.', true,  4, NOW()),
                    ('currency_undeclared',           'Undeclared currency',           'Cash bundles or FOREX bricks above the reporting threshold.', true,  5, NOW()),
                    ('wildlife_cites',                'Wildlife / CITES-listed',       'Ivory, pangolin scales, exotic skins, live animals.', true,  6, NOW()),
                    ('counterfeit_goods',             'Counterfeit / IP-infringing',   'Fake branded clothing, electronics, pharmaceuticals.', true,  7, NOW()),
                    ('pharmaceutical_controlled',     'Controlled pharmaceutical',     'Tramadol, codeine syrup, unregistered medicines.', true,  8, NOW()),
                    ('hazardous_material',            'Dangerous goods / hazmat',      'Improperly declared chemicals, radioactive, flammable.', true,  9, NOW()),
                    ('prohibited_used_goods',         'Prohibited used goods',         'Used undergarments and other items prohibited by Ghana import rules.', true, 10, NOW()),
                    ('human_remains_or_trafficking',  'Human-related concern',         'Trafficking or remains. Rare and serious.', true, 11, NOW()),
                    ('suspicious_anomaly',            'Suspicious anomaly',            'Operator flagged but cannot classify. Should be rare.', true, 12, NOW()),
                    ('other_security',                'Other (security)',              'Last resort. Free-text note required.', true, 13, NOW());
            ");

            // ── 5. Seed revenue assurance domain (13 categories) ────────────────────
            // Sourced from operator field notes + WCO commercial fraud taxonomy +
            // Ghana-specific transit-diversion casework + GRA's stated AI focus areas
            // (undervaluation, misclassification). Subject to SME sign-off.
            migrationBuilder.Sql(@"
                INSERT INTO revenueanomalycategories (code, displayname, description, isactive, sortorder, createdatutc)
                VALUES
                    ('revenue_undeclared_goods',          'Undeclared goods',                'Items visible in scan that are not on the manifest at all.', true,  1, NOW()),
                    ('revenue_underdeclared_quantity',    'Underdeclared quantity',          'Visible count exceeds declared count.', true,  2, NOW()),
                    ('revenue_misdescription',            'Misdescription of goods',         'Visible cargo does not match the declared description.', true,  3, NOW()),
                    ('revenue_misclassification',         'HS-code misclassification',       'Visible goods imply a different HS code than declared (different duty rate).', true,  4, NOW()),
                    ('revenue_undervaluation_visible',    'Visible undervaluation',          'High-value items declared as low-value categories. The most common form of customs fraud worldwide.', true,  5, NOW()),
                    ('revenue_origin_inconsistency',      'Origin inconsistency',            'Markings, branding, or packaging inconsistent with declared country of origin.', true,  6, NOW()),
                    ('revenue_concealment',               'Concealment',                     'Hidden compartments, false walls, items inside other items, rip-on/rip-off pattern.', true,  7, NOW()),
                    ('revenue_mixed_loading',             'Mixed loading / mis-stuffing',    'Legitimate declared cargo combined with undeclared extras in the same container.', true,  8, NOW()),
                    ('revenue_transit_anomaly',           'Transit diversion indicator',     'Container declared in-transit (Burkina, Mali, Niger via ECOWAS) but showing signs of intent to divert.', true,  9, NOW()),
                    ('revenue_restricted_unlicensed',     'Restricted goods without licence', 'Vehicles, alcohol, tobacco, controlled pharmaceuticals lacking required permits.', true, 10, NOW()),
                    ('revenue_excise_excess',             'Excise excess',                   'Tobacco, alcohol or fuel above declared quantities.', true, 11, NOW()),
                    ('revenue_seal_or_container_anomaly', 'Seal or container manipulation',  'Tampered seal, container number mismatch, signs of mid-route substitution.', true, 12, NOW()),
                    ('revenue_other',                     'Other (revenue)',                 'Last resort. Free-text note required.', true, 13, NOW());
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "revenueanomalycategories");

            migrationBuilder.DropTable(
                name: "threatcategories");

            migrationBuilder.DropColumn(
                name: "revenueanomalycategoryid",
                table: "imageanalysisdecisions");

            migrationBuilder.DropColumn(
                name: "threatcategoryid",
                table: "imageanalysisdecisions");

            migrationBuilder.DropColumn(
                name: "revenueanomalycategoryid",
                table: "containerannotations");

            migrationBuilder.DropColumn(
                name: "threatcategoryid",
                table: "containerannotations");
        }
    }
}

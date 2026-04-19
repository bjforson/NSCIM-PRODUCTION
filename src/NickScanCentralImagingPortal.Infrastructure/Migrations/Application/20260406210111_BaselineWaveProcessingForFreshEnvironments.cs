using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NickScanCentralImagingPortal.Infrastructure.Migrations.Application
{
    /// <summary>
    /// Baseline migration for the wave-processing schema.
    ///
    /// Background: the wave-processing tables (analysisparentgroups,
    /// wavependingcontainers) and the wave columns on analysisgroups /
    /// analysissettings were originally applied to production via the manual
    /// Migrations/wave_processing_schema.sql file. No EF migration was ever
    /// generated for them, leaving the model snapshot drifted from the database.
    /// The drift was discovered when the AddThreatAndRevenueAnomalyCategories
    /// migration was scaffolded; the snapshot has since been corrected, but
    /// environments where the manual SQL was never applied still have no record
    /// of these tables in the EF migration history.
    ///
    /// This migration closes that gap. It re-issues the same DDL as
    /// wave_processing_schema.sql, wrapped in IF NOT EXISTS guards so it is a
    /// no-op on production (where the tables already exist) and a real
    /// table-creation on fresh dev / test databases. The model snapshot is
    /// unchanged because EF already knows about these entities — this migration
    /// only exists to bring the database state in line with the snapshot on
    /// environments that were never bootstrapped via the manual SQL.
    ///
    /// Down() is intentionally empty: we cannot safely roll back wave processing
    /// because production has live data in these tables. If a roll back is
    /// genuinely needed it must be done manually with full data review.
    ///
    /// ─── DUPLICATION NOTE (intentional, do not delete either file) ───────────
    /// A second baseline migration with the same intent exists at
    /// 20260406220307_BaselineWaveProcessing.cs. Both were created independently
    /// in parallel sessions on the same day to fix the same drift. They were
    /// merged into main without conflict because they have different timestamps
    /// and different class names. They have both been applied to production
    /// (each as a separate row in __EFMigrationsHistory).
    ///
    /// They co-exist safely because:
    ///   - Both use IF NOT EXISTS guards on every CREATE TABLE / ADD COLUMN.
    ///   - Whichever runs first creates the schema, the other becomes a true no-op.
    ///   - The model snapshot is unaffected (EF already knew about these
    ///     entities before either migration was scaffolded).
    ///
    /// DO NOT DELETE EITHER FILE. The __EFMigrationsHistory table on every
    /// deployed environment now references both class names. Removing one from
    /// source control would cause EF to log "model has pending changes" warnings
    /// on startup and force operators to clean up the history table by hand.
    /// Cosmetic redundancy is the right trade-off here.
    /// </summary>
    public partial class BaselineWaveProcessingForFreshEnvironments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // ── 1. analysisparentgroups (wave parent group) ────────────────────────
            migrationBuilder.Sql(@"
                CREATE TABLE IF NOT EXISTS analysisparentgroups (
                    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
                    groupidentifier VARCHAR(150) NOT NULL,
                    scannertype VARCHAR(20),
                    totalexpectedcontainers INT NOT NULL DEFAULT 0,
                    completedwavecount INT NOT NULL DEFAULT 0,
                    status VARCHAR(20) NOT NULL DEFAULT 'Active',
                    autoclosedate TIMESTAMPTZ,
                    createdatutc TIMESTAMPTZ NOT NULL DEFAULT NOW(),
                    updatedatutc TIMESTAMPTZ
                );
            ");

            migrationBuilder.Sql(@"
                CREATE INDEX IF NOT EXISTS ix_analysisparentgroups_status
                    ON analysisparentgroups(status);
            ");

            // Some environments seeded via the older wave_processing_schema.sql may
            // be missing the completedwavecount, autoclosedate, and updatedatutc
            // columns that the C# entity now expects. Add them defensively.
            migrationBuilder.Sql(@"
                ALTER TABLE analysisparentgroups
                    ADD COLUMN IF NOT EXISTS completedwavecount INT NOT NULL DEFAULT 0;
            ");
            migrationBuilder.Sql(@"
                ALTER TABLE analysisparentgroups
                    ADD COLUMN IF NOT EXISTS autoclosedate TIMESTAMPTZ;
            ");
            migrationBuilder.Sql(@"
                ALTER TABLE analysisparentgroups
                    ADD COLUMN IF NOT EXISTS updatedatutc TIMESTAMPTZ;
            ");

            // ── 2. wavependingcontainers (wave intake queue) ───────────────────────
            migrationBuilder.Sql(@"
                CREATE TABLE IF NOT EXISTS wavependingcontainers (
                    id SERIAL PRIMARY KEY,
                    parentgroupid UUID NOT NULL REFERENCES analysisparentgroups(id),
                    containernumber VARCHAR(50) NOT NULL,
                    scannertype VARCHAR(20),
                    firstseenutc TIMESTAMPTZ NOT NULL DEFAULT NOW(),
                    becamereadyutc TIMESTAMPTZ,
                    assignedtowaveid UUID,
                    status VARCHAR(20) NOT NULL DEFAULT 'Pending'
                );
            ");

            migrationBuilder.Sql(@"
                CREATE INDEX IF NOT EXISTS ix_wavependingcontainers_parentgroupid
                    ON wavependingcontainers(parentgroupid);
            ");

            // Defensive: older schema versions may not have assignedtowaveid.
            migrationBuilder.Sql(@"
                ALTER TABLE wavependingcontainers
                    ADD COLUMN IF NOT EXISTS assignedtowaveid UUID;
            ");

            // ── 3. analysisgroups wave columns ─────────────────────────────────────
            migrationBuilder.Sql(@"
                ALTER TABLE analysisgroups
                    ADD COLUMN IF NOT EXISTS parentgroupid UUID
                        REFERENCES analysisparentgroups(id);
            ");
            migrationBuilder.Sql(@"
                ALTER TABLE analysisgroups
                    ADD COLUMN IF NOT EXISTS wavenumber INT;
            ");
            migrationBuilder.Sql(@"
                ALTER TABLE analysisgroups
                    ADD COLUMN IF NOT EXISTS wavecreatedreason VARCHAR(50);
            ");
            migrationBuilder.Sql(@"
                ALTER TABLE analysisgroups
                    ADD COLUMN IF NOT EXISTS aicargosummary VARCHAR(2000);
            ");

            migrationBuilder.Sql(@"
                CREATE INDEX IF NOT EXISTS ix_analysisgroups_parentgroupid
                    ON analysisgroups(parentgroupid);
            ");

            // ── 4. analysissettings wave settings ──────────────────────────────────
            migrationBuilder.Sql(@"
                ALTER TABLE analysissettings
                    ADD COLUMN IF NOT EXISTS enablewaveprocessing BOOLEAN NOT NULL DEFAULT false;
            ");
            migrationBuilder.Sql(@"
                ALTER TABLE analysissettings
                    ADD COLUMN IF NOT EXISTS waveminbatchsize INT NOT NULL DEFAULT 3;
            ");
            migrationBuilder.Sql(@"
                ALTER TABLE analysissettings
                    ADD COLUMN IF NOT EXISTS wavetimeouthours INT NOT NULL DEFAULT 24;
            ");
            migrationBuilder.Sql(@"
                ALTER TABLE analysissettings
                    ADD COLUMN IF NOT EXISTS waveautoclosedays INT NOT NULL DEFAULT 30;
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Intentionally empty. See class summary — wave processing carries live
            // production data and must not be auto-rolled-back.
        }
    }
}

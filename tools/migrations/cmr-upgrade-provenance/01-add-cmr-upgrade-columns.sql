-- ─────────────────────────────────────────────────────────────────────────────
-- 1.13.0 — CMR upgrade provenance columns
--
-- Adds two nullable columns to boedocuments to record CMR→IM/EX upgrade history:
--
--   originalclearancetype  — the clearance type at first ingest, before any
--                            upgrade. NULL means "current type is original,
--                            never upgraded". Set once on first upgrade and
--                            immutable thereafter (the application enforces
--                            immutability via COALESCE in the UPDATE statement).
--
--   cmrupgradedat          — when the upgrade happened. NULL means "never
--                            upgraded". Set once on first upgrade.
--
-- Both columns nullable so this migration is non-blocking on the existing
-- ~62k boedocuments rows. Idempotent: re-running is a no-op.
--
-- Database: nickscan_downloads (the IcumDownloadsDbContext target)
--
-- Run with:
--   PGPASSWORD=... psql -h localhost -U postgres -d nickscan_downloads \
--     -v ON_ERROR_STOP=1 -f tools/migrations/cmr-upgrade-provenance/01-add-cmr-upgrade-columns.sql
-- ─────────────────────────────────────────────────────────────────────────────

\timing on

BEGIN;

ALTER TABLE boedocuments
    ADD COLUMN IF NOT EXISTS originalclearancetype varchar(20) NULL,
    ADD COLUMN IF NOT EXISTS cmrupgradedat        timestamptz   NULL;

-- Lightweight index so the diagnostics endpoint can quickly count upgrades
-- without scanning the whole table. Partial index keeps it tiny.
CREATE INDEX IF NOT EXISTS idx_boedocuments_cmrupgradedat
    ON boedocuments (cmrupgradedat)
    WHERE cmrupgradedat IS NOT NULL;

COMMIT;

-- Verify
\d boedocuments

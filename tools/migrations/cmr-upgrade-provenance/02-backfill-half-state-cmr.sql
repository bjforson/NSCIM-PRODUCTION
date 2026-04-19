-- ─────────────────────────────────────────────────────────────────────────────
-- 1.13.0 — Backfill the 998 half-state CMR rows
--
-- Context. Some ICUMS messages arrive with `clearancetype = 'CMR'` but already
-- carry a populated `declarationnumber`, `regimecode`, and `crmslevel`. These
-- look like CMR rows on the surface but they're actually fully-formed import
-- declarations that ICUMS' port-side subsystem continues to emit on the cargo
-- movement document type. The existing CMR→IM lifecycle service in
-- IcumJsonIngestionService never gets a chance to upgrade them because no
-- distinct IM message ever arrives — the IM data is already inside the CMR
-- message.
--
-- This script flips those rows to their proper IM clearance type and stamps
-- the provenance columns so we can still tell they originally arrived as CMR.
-- All 998 rows in the current production snapshot have import-side WCO regime
-- codes (40 / 70 / 80 / 90), so the upgrade target is unambiguously 'IM' for
-- every row.
--
-- Idempotent: re-runs find no matching rows because the first run flips all
-- of them out of the WHERE clause.
--
-- Run AFTER 01-add-cmr-upgrade-columns.sql with:
--   PGPASSWORD=... psql -h localhost -U postgres -d nickscan_downloads \
--     -v ON_ERROR_STOP=1 -f tools/migrations/cmr-upgrade-provenance/02-backfill-half-state-cmr.sql
-- ─────────────────────────────────────────────────────────────────────────────

\timing on

BEGIN;

-- Snapshot before
SELECT
    'before' AS phase,
    COUNT(*) FILTER (WHERE clearancetype = 'CMR' AND declarationnumber IS NOT NULL AND declarationnumber <> '') AS half_state_cmr,
    COUNT(*) FILTER (WHERE originalclearancetype IS NOT NULL) AS already_provenance_tagged
FROM boedocuments;

-- The actual backfill
WITH targets AS (
    SELECT id
    FROM boedocuments
    WHERE clearancetype = 'CMR'
      AND declarationnumber IS NOT NULL
      AND declarationnumber <> ''
)
UPDATE boedocuments b
   SET originalclearancetype = COALESCE(b.originalclearancetype, 'CMR'),
       cmrupgradedat        = COALESCE(b.cmrupgradedat, now()),
       clearancetype        = 'IM',
       updatedat            = now()
  FROM targets t
 WHERE b.id = t.id;

-- Snapshot after
SELECT
    'after' AS phase,
    COUNT(*) FILTER (WHERE clearancetype = 'CMR' AND declarationnumber IS NOT NULL AND declarationnumber <> '') AS half_state_cmr,
    COUNT(*) FILTER (WHERE originalclearancetype = 'CMR') AS provenance_tagged_cmr
FROM boedocuments;

COMMIT;

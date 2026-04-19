-- ─────────────────────────────────────────────────────────────────────────────
-- 2026-04-19 — Retroactive half-state CMR backfill, regime-40 only
--
-- Context. The 1.13.0 implicit-upgrade handler in IcumJsonIngestionService
-- (see 02-backfill-half-state-cmr.sql for the original backfill) catches
-- CMR-typed messages that already carry a declaration + regime code and flips
-- them to IM/EX in-memory before the save path runs. That worked for the 998
-- rows known at 1.13.0 ship time. It has continued to catch new ones on ingest
-- (>1,200 upgrades in `boedocuments` as of today).
--
-- However, 17 regime-40 CMR rows ingested around the 1.13.0 rollout window
-- (2026-04-08 to 2026-04-10) slipped through — most likely ingested on an
-- instance that hadn't picked up the implicit-upgrade code yet, or before the
-- backfill script ran. They've sat as CMR-with-declaration ever since.
-- `updatedat = createdat` on all 17, confirming no upgrade ever fired.
-- `CMRRedownloadQueue` is empty, so nothing is re-fetching them.
--
-- This script applies the same rule the implicit-upgrade handler would apply
-- today, but scoped to the 17 stuck rows:
--
--   clearancetype='CMR' AND regimecode='40' AND declarationnumber <> ''
--   → clearancetype='IM', stamp provenance, touch updatedat.
--
-- Scope note: regime-80 CMR-with-declaration rows are intentionally NOT
-- touched by this script. Per operator guidance (2026-04-19), the 21 regime-80
-- Red CMRs under declaration 80426261787 are correct-by-design as CMR (transit
-- procedure); the broader 1.13.0 rule that flips regime-80 to IM does not
-- apply to that specific set. The original 02- backfill SQL does not
-- distinguish, so do not re-run it — use this one instead.
--
-- Idempotent: re-runs find no matching rows because the first run flips them
-- all out of the WHERE clause.
--
-- Run with:
--   PGPASSWORD=... psql -h localhost -U postgres -d nickscan_downloads \
--     -v ON_ERROR_STOP=1 -f tools/migrations/cmr-upgrade-provenance/03-backfill-regime40-half-state-cmr.sql
-- ─────────────────────────────────────────────────────────────────────────────

\timing on

BEGIN;

-- Snapshot before
SELECT
    'before' AS phase,
    COUNT(*) FILTER (WHERE clearancetype = 'CMR' AND regimecode = '40'
                       AND declarationnumber IS NOT NULL AND declarationnumber <> '') AS stuck_regime40_cmr,
    COUNT(*) FILTER (WHERE clearancetype = 'CMR' AND regimecode = '80'
                       AND declarationnumber IS NOT NULL AND declarationnumber <> '') AS untouched_regime80_cmr
FROM boedocuments;

-- The actual backfill
WITH targets AS (
    SELECT id
    FROM boedocuments
    WHERE clearancetype = 'CMR'
      AND regimecode = '40'
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
    COUNT(*) FILTER (WHERE clearancetype = 'CMR' AND regimecode = '40'
                       AND declarationnumber IS NOT NULL AND declarationnumber <> '') AS stuck_regime40_cmr,
    COUNT(*) FILTER (WHERE clearancetype = 'CMR' AND regimecode = '80'
                       AND declarationnumber IS NOT NULL AND declarationnumber <> '') AS untouched_regime80_cmr
FROM boedocuments;

COMMIT;

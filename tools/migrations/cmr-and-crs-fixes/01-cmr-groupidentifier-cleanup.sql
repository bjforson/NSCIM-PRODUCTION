-- ─────────────────────────────────────────────────────────────────────────────
-- 1.19.0 — CMR groupidentifier cleanup + CRS re-validation trigger
--
-- Two distinct fixes that both ship in 1.19.0:
--
-- PART 1: Clean up the 650 CMR rows that were marked Status=Complete with
--         empty groupidentifier and are therefore invisible to IntakeWorker.
--         1.19.0 code prevents new rows landing in this state. This script
--         heals the historical residue.
--
-- PART 2: Trigger a re-run of RunPostICUMSValidationWorkflowAsync on all
--         multi-container OriginalScanRecord rows that have no corresponding
--         CrossRecordScan entry. The 1.19.0 code fix (removing the
--         settledCount trap) means the validator will now process them
--         correctly; this script "unskips" them by making the
--         alreadyCrossRecord check miss.
--
-- Idempotent. Safe to re-run.
--
-- Run with:
--   PGPASSWORD=... psql -h localhost -U postgres -d nickscan_production \
--     -v ON_ERROR_STOP=1 \
--     -f tools/migrations/cmr-and-crs-fixes/01-cmr-groupidentifier-cleanup.sql
-- ─────────────────────────────────────────────────────────────────────────────

\timing on

BEGIN;

-- ── PART 1: CMR Status fix ──────────────────────────────────────────────
\echo === PART 1: CMR rows stuck invisible ===

SELECT 'BEFORE' AS phase,
       COUNT(*) FILTER (WHERE clearancetype = 'CMR'
                          AND (groupidentifier IS NULL OR groupidentifier = '')
                          AND status = 'Complete') AS stuck_cmr_complete,
       COUNT(*) FILTER (WHERE clearancetype = 'CMR'
                          AND status = 'AwaitingDeclaration') AS already_healed
FROM containercompletenessstatuses;

-- Flip stuck CMR rows to AwaitingDeclaration / Pending so the 1.13.0 CMR→IM/EX
-- lifecycle service can re-run them when a declaration arrives.
UPDATE containercompletenessstatuses
   SET status = 'AwaitingDeclaration',
       workflowstage = 'Pending',
       updatedat = now()
 WHERE clearancetype = 'CMR'
   AND (groupidentifier IS NULL OR groupidentifier = '')
   AND status = 'Complete';

SELECT 'AFTER' AS phase,
       COUNT(*) FILTER (WHERE clearancetype = 'CMR'
                          AND (groupidentifier IS NULL OR groupidentifier = '')
                          AND status = 'Complete') AS stuck_cmr_complete,
       COUNT(*) FILTER (WHERE clearancetype = 'CMR'
                          AND status = 'AwaitingDeclaration') AS healed
FROM containercompletenessstatuses;

-- ── PART 2: CRS backfill ───────────────────────────────────────────────
-- We don't re-run the validator from SQL directly (it's a C# service). The
-- existing RunPostICUMSValidationWorkflowAsync worker will pick these up on
-- its next tick after 1.19.0 deploys, because the code-level settledCount
-- short-circuit has been removed.
--
-- Here we just report the gap so the operator can see the expected backlog.
\echo === PART 2: CRS backfill gap (post-1.19.0 code fix will catch these) ===

WITH multi_container_originals AS (
    SELECT osr.id, osr.originalcontainernumbers, osr.scannertype, osr.inspectionid
    FROM originalscanrecords osr
    WHERE osr.derivedrecordcount >= 2
),
already_has_crs AS (
    SELECT DISTINCT mco.id
    FROM multi_container_originals mco
    JOIN crossrecordscans crs
      ON crs.scannerrecordid IS NOT NULL
     AND crs.originalscanrecord ILIKE mco.originalcontainernumbers
)
SELECT
    COUNT(*) AS multi_container_total,
    COUNT(*) FILTER (WHERE id IN (SELECT id FROM already_has_crs)) AS with_crs,
    COUNT(*) FILTER (WHERE id NOT IN (SELECT id FROM already_has_crs)) AS without_crs
FROM multi_container_originals;

COMMIT;

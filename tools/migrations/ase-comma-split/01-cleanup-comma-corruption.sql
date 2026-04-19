-- ─────────────────────────────────────────────────────────────────────────────
-- 1.18.0 — ASE comma-split corruption cleanup
--
-- The ASE scanner integration was writing comma-joined container numbers
-- ("C1, C2") as a single string into every downstream table when a single
-- inspection covered multiple containers. 1.18.0 patches the upstream
-- AseDatabaseSyncService to split at the queue publish step; this script
-- cleans up the already-corrupted data it produced between 2026-03-18 and
-- the 1.18.0 deploy.
--
-- Scope at audit time (2026-04-08):
--   106 analysisrecords     rows with comma in containernumber
--    25 analysisgroups      rows with comma in groupidentifier
--   263 containercompletenessstatuses rows with comma in containernumber
--  2629 containerscanqueues rows with comma in containernumber
--
-- Strategy — SOFT approach: we don't try to split the corrupt rows into
-- correct-looking pairs. Instead we:
--   1. Release any active analyst assignments on the corrupt groups so
--      analysts stop seeing empty ICUMS panels
--   2. Delete the corrupt analysisrecords, analysisgroups, containercompletenessstatuses
--   3. Mark the corrupt containerscanqueues rows as 'Cancelled' so they
--      don't get re-consumed
--   4. Leave the asescans rows AS-IS (they are the audit trail and the
--      1.18.0 fix will now publish split items on the next sync cycle,
--      but the source rows are already ingested so they won't re-trigger
--      unless the sync replays them — which it won't because
--      _lastSyncedInspectionId already advanced past them)
--
-- Because 1.18.0 stops producing new corrupt rows, and the downstream
-- reconciliation worker will build proper per-container RecordCompletenessStatus
-- entries from ICUMS on its next tick, the analyst workflow gets the
-- correct per-container view automatically once the fix ships.
--
-- Idempotent: re-running is safe (no rows matching the filter second time).
--
-- Run with:
--   PGPASSWORD=... psql -h localhost -U postgres -d nickscan_production \
--     -v ON_ERROR_STOP=1 \
--     -f tools/migrations/ase-comma-split/01-cleanup-comma-corruption.sql
-- ─────────────────────────────────────────────────────────────────────────────

\timing on

BEGIN;

-- ── Phase 1: Snapshot BEFORE ────────────────────────────────────────────
SELECT 'BEFORE' AS phase,
       (SELECT COUNT(*) FROM analysisrecords          WHERE containernumber LIKE '%,%') AS analysis_records_corrupt,
       (SELECT COUNT(*) FROM analysisgroups           WHERE groupidentifier LIKE '%,%') AS analysis_groups_corrupt,
       (SELECT COUNT(*) FROM containercompletenessstatuses WHERE containernumber LIKE '%,%') AS ccs_corrupt,
       (SELECT COUNT(*) FROM containerscanqueues      WHERE containernumber LIKE '%,%') AS queue_corrupt,
       (SELECT COUNT(*) FROM analysisassignments a
          JOIN analysisgroups g ON g.id = a.groupid
         WHERE g.groupidentifier LIKE '%,%' AND a.state = 'Active') AS active_assignments_on_corrupt_groups;

-- ── Phase 2: Release active analyst assignments on corrupt groups ──
--
-- This unblocks the analyst queue so people don't see empty-ICUMS groups.
UPDATE analysisassignments a
   SET state = 'Released',
       updatedatutc = now()
  FROM analysisgroups g
 WHERE a.groupid = g.id
   AND g.groupidentifier LIKE '%,%'
   AND a.state = 'Active';

-- ── Phase 3: Delete corrupt ImageAnalysisDecisions ──
--
-- Some corrupt groups already have decisions logged against them. Those
-- decisions reference the comma-concatenated container string which can't
-- be joined to anything useful. Delete them so the AI training flywheel
-- doesn't export nonsense training data.
DELETE FROM imageanalysisdecisions
 WHERE containernumber LIKE '%,%';

-- ── Phase 4: Delete corrupt AnalysisRecord rows ──
DELETE FROM analysisrecords
 WHERE containernumber LIKE '%,%';

-- ── Phase 5: Delete corrupt AnalysisGroup rows ──
-- (FK cascade from analysisrecords is already satisfied by Phase 4)
DELETE FROM analysisgroups
 WHERE groupidentifier LIKE '%,%';

-- ── Phase 6: Delete corrupt ContainerCompletenessStatus rows ──
DELETE FROM containercompletenessstatuses
 WHERE containernumber LIKE '%,%';

-- ── Phase 7: Cancel corrupt ContainerScanQueue rows ──
--
-- These were produced BEFORE the 1.18.0 fix. We can't re-publish them as
-- split items because the original ASE scan data is already captured in
-- asescans (and the worker's watermark has moved past them). Mark them
-- as Cancelled with a note so they don't trip anyone looking at queue
-- health metrics, and so downstream retries don't pick them back up.
UPDATE containerscanqueues
   SET status = 'Cancelled',
       errormessage = COALESCE(errormessage || ' | ', '') || '1.18.0 ASE comma-split cleanup: superseded',
       updatedat = now()
 WHERE containernumber LIKE '%,%'
   AND status NOT IN ('Completed', 'Cancelled');

-- ── Phase 8: Snapshot AFTER ────────────────────────────────────────────
SELECT 'AFTER' AS phase,
       (SELECT COUNT(*) FROM analysisrecords          WHERE containernumber LIKE '%,%') AS analysis_records_corrupt,
       (SELECT COUNT(*) FROM analysisgroups           WHERE groupidentifier LIKE '%,%') AS analysis_groups_corrupt,
       (SELECT COUNT(*) FROM containercompletenessstatuses WHERE containernumber LIKE '%,%') AS ccs_corrupt,
       (SELECT COUNT(*) FROM containerscanqueues      WHERE containernumber LIKE '%,%' AND status NOT IN ('Completed','Cancelled')) AS queue_corrupt_live,
       (SELECT COUNT(*) FROM analysisassignments a
          JOIN analysisgroups g ON g.id = a.groupid
         WHERE g.groupidentifier LIKE '%,%' AND a.state = 'Active') AS active_assignments_on_corrupt_groups;

COMMIT;

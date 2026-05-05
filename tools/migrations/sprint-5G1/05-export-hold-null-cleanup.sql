-- 2026-05-05  Sprint 5G1 follow-up Step A — Audit finding 7.07 (closure pass)
--
-- Per the investigation report at docs/audit/2026-05-05/follow-up-7.07-investigation.md
-- (Agent 1.1, §3 hypothesis (c) and §5 recommendation), there are 26 active CCS rows
-- in the NULL-boedocumentid / NULL-clearancetype / hasicumsdata=true / NO-active-CBR
-- shape. They are stale residuals from manual bulk-unmatch ops on 2026-05-03 and
-- 2026-05-04 plus a small handful of pre-2.16.5 race-window leftovers.
--
-- Distribution at probe time (2026-05-05 18:30Z):
--   13 rows: workflowstage=Export-Hold, scannertype=FS6000  (the 7.04 cohort)
--    9 rows: workflowstage=ImageAnalysis, scannertype=FS6000
--    2 rows: workflowstage=ImageAnalysis, scannertype=ASE  (containernumber='Unknown')
--    2 rows: workflowstage=Submitted, scannertype=FS6000  (MSMU2303342, TTNU1063629)
--   Total: 26
--
-- All 26 rows have:
--   - hasicumsdata = true
--   - boedocumentid IS NULL (already)
--   - clearancetype IS NULL (already)
--   - NO active containerboerelations row for the same containernumber
--
-- Treatment per Agent 1.1's §5 sketch: flip hasicumsdata=false. Boedocumentid and
-- clearancetype are already NULL; no further write needed on those columns. This
-- exits the rows from the 7.07 candidate predicate. Sprint 5G1's Step 4 already
-- backfilled the 12 rows that DID have an active CBR; this completes the cohort.
--
-- Sanity cap: 50 rows. The transaction will roll back if the affected count exceeds
-- the cap (defensive — current count is 26, audit_was 30, 1.1 confirmed shape).
--
-- Run as: postgres superuser (DDL not strictly needed but DML on RLS-FORCED
-- tenant_isolation tables benefits from superuser). RLS bypass via nscim_app
-- with `SET LOCAL app.tenant_id = '1'` is also fine; we use postgres for
-- consistency with other Sprint 5G1 migrations.
--
-- Idempotent: re-run is a no-op (the predicate matches no rows after first run).

\set ON_ERROR_STOP on

BEGIN;

-- 1. Snapshot pre-state for audit log
\echo '## PRE-STATE'
SELECT count(*) AS candidates_total
FROM containercompletenessstatuses ccs
WHERE ccs.hasicumsdata = true
  AND ccs.boedocumentid IS NULL
  AND ccs.clearancetype IS NULL
  AND NOT EXISTS (
      SELECT 1 FROM containerboerelations cbr
      WHERE cbr.containernumber = ccs.containernumber
        AND cbr.isactive = true);

SELECT workflowstage, scannertype, count(*) AS rows
FROM containercompletenessstatuses ccs
WHERE ccs.hasicumsdata = true
  AND ccs.boedocumentid IS NULL
  AND ccs.clearancetype IS NULL
  AND NOT EXISTS (
      SELECT 1 FROM containerboerelations cbr
      WHERE cbr.containernumber = ccs.containernumber
        AND cbr.isactive = true)
GROUP BY workflowstage, scannertype
ORDER BY workflowstage, scannertype;

-- 2. Sanity cap — refuse if more than 50 candidates
DO $$
DECLARE
    n integer;
BEGIN
    SELECT count(*) INTO n
    FROM containercompletenessstatuses ccs
    WHERE ccs.hasicumsdata = true
      AND ccs.boedocumentid IS NULL
      AND ccs.clearancetype IS NULL
      AND NOT EXISTS (
          SELECT 1 FROM containerboerelations cbr
          WHERE cbr.containernumber = ccs.containernumber
            AND cbr.isactive = true);
    IF n > 50 THEN
        RAISE EXCEPTION 'Step A sanity cap: % candidates exceeds 50 — aborting.', n;
    END IF;
    RAISE NOTICE 'Step A pre-state: % candidates (cap=50, expected ~26).', n;
END $$;

-- 3. Flip hasicumsdata=false. boedocumentid and clearancetype are already NULL,
--    so this single column update is sufficient to exit the 7.07 predicate.
UPDATE containercompletenessstatuses ccs
   SET hasicumsdata = false,
       updatedat    = now() AT TIME ZONE 'UTC'
 WHERE ccs.hasicumsdata = true
   AND ccs.boedocumentid IS NULL
   AND ccs.clearancetype IS NULL
   AND NOT EXISTS (
       SELECT 1 FROM containerboerelations cbr
       WHERE cbr.containernumber = ccs.containernumber
         AND cbr.isactive = true);

\echo '## POST-STATE'
SELECT count(*) AS candidates_remaining
FROM containercompletenessstatuses ccs
WHERE ccs.hasicumsdata = true
  AND ccs.boedocumentid IS NULL
  AND ccs.clearancetype IS NULL
  AND NOT EXISTS (
      SELECT 1 FROM containerboerelations cbr
      WHERE cbr.containernumber = ccs.containernumber
        AND cbr.isactive = true);
-- Expected: 0

-- The 7.07 follow-up predicate (any CCS hasicumsdata=true with no active CBR)
-- should now be substantially reduced. Some race-window rows (the 119
-- ASE/CMR/AwaitingDeclaration/Pending cohort with boedocumentid SET) will
-- remain — those are expected pending state per Agent 1.1's §5; the
-- CompletenessOrchestrator will reconcile them on its next sweep.

COMMIT;

\echo '## OK — Sprint 5G1 follow-up Step A complete (26 NULL/NULL no-active-CBR rows flipped to hasicumsdata=false)'

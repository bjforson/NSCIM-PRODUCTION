-- 2026-05-05  Sprint 5G1 follow-up Step B — Audit finding 7.01 (FK4/FK5 unblock)
--
-- Sprint 5G1 Step 5 documented two FKs that could not be added because the parent
-- column `containercompletenessstatuses.containernumber` was not unique:
--
--   FK4: containerboerelations.containernumber → ccs.containernumber
--   FK5: imageanalysisdecisions.containernumber → ccs.containernumber
--
-- Investigation (Team 1.2 probe, 2026-05-05 18:50Z) found:
--   - 40 distinct dup containernumbers (audit reported 41, slight drift over 11h)
--   - 81 total dup rows
--   - 38 are same-scanner duplicates (semantically identical: same boe, stage, flags
--     — divergent count = 0). Likely double-write bug from the 2026-03 / 2026-05-05
--     ingestion path.
--   - 2 are cross-scanner duplicates (MEDU7718311, MSBU1802425) where the same
--     physical container has both an FS6000 leg and an ASE leg. These are
--     semantically distinct (different scanners, different stages, different
--     boedocumentid in MSBU1802425's case). Per audit 3.15 (cross-scanner
--     ambiguity), these are legitimate distinct records.
--   - 39 rows are not-MAX(id) per (containernumber, scannertype) — these are the
--     deletion targets.
--
-- Treatment:
--   1. Delete the 39 same-scanner-dup rows (keep MAX(id)). CCS is denormalization /
--      cache, not source-of-truth — no audit-trail loss. AR/IAD reference by
--      containernumber, not by CCS.id, so they automatically re-bind to surviving
--      row.
--   2. Add UNIQUE constraint on (containernumber, scannertype) — composite, not
--      single-column, because the 2 cross-scanner cases must coexist.
--
-- The composite UNIQUE means FK4/FK5 cannot be added to ccs.containernumber alone.
-- Documented for Agent 1.3 (FK pass): the FKs would need a composite parent
-- (containernumber, scannertype) on both sides, which requires schema work on
-- containerboerelations + imageanalysisdecisions to add scannertype as part of the
-- referencing tuple. Out of scope for Step B; flagged for follow-up.
--
-- Sanity cap: 100 rows total deletion. Current count is 39. STOP if >100.
--
-- Run as: postgres superuser (DDL).
-- Idempotent: re-run is a no-op (DELETE matches nothing on second run; UNIQUE
-- constraint added with DO-NOT-EXIST guard).

\set ON_ERROR_STOP on

BEGIN;

-- 1. Snapshot pre-state
\echo '## PRE-STATE'
SELECT count(*) AS distinct_dup_containers
FROM (
  SELECT containernumber FROM containercompletenessstatuses
  GROUP BY containernumber HAVING count(*) > 1
) sub;

SELECT count(*) AS total_dup_rows
FROM containercompletenessstatuses ccs
WHERE ccs.containernumber IN (
  SELECT containernumber FROM containercompletenessstatuses
  GROUP BY containernumber HAVING count(*) > 1
);

SELECT count(*) AS deletion_targets
FROM containercompletenessstatuses ccs
WHERE ccs.id NOT IN (
  SELECT max(id) FROM containercompletenessstatuses
  GROUP BY containernumber, scannertype
);

-- Cross-scanner cohort (kept as legitimate distinct rows)
SELECT containernumber, count(DISTINCT scannertype) AS scanners,
       string_agg(DISTINCT scannertype, ',' ORDER BY scannertype) AS scanner_list
FROM containercompletenessstatuses
GROUP BY containernumber
HAVING count(DISTINCT scannertype) > 1
ORDER BY containernumber;

-- 2. Sanity cap — refuse if > 100 deletion targets
DO $$
DECLARE
    n integer;
BEGIN
    SELECT count(*) INTO n FROM containercompletenessstatuses ccs
    WHERE ccs.id NOT IN (
        SELECT max(id) FROM containercompletenessstatuses
        GROUP BY containernumber, scannertype);
    IF n > 100 THEN
        RAISE EXCEPTION 'Step B sanity cap: % deletion targets exceeds 100 — aborting.', n;
    END IF;
    RAISE NOTICE 'Step B pre-state: % deletion targets (cap=100, expected ~39).', n;
END $$;

-- 3. Delete the same-scanner-dup rows (keep MAX(id) per composite key)
\echo '## DEDUP'
DELETE FROM containercompletenessstatuses ccs
 WHERE ccs.id NOT IN (
     SELECT max(id) FROM containercompletenessstatuses
     GROUP BY containernumber, scannertype);
-- Expected: 39

-- 4. Verify zero composite duplicates remain
\echo '## POST-DEDUP composite-dup check'
DO $$
DECLARE
    n integer;
BEGIN
    SELECT count(*) INTO n FROM (
        SELECT containernumber, scannertype FROM containercompletenessstatuses
        GROUP BY containernumber, scannertype HAVING count(*) > 1
    ) sub;
    IF n > 0 THEN
        RAISE EXCEPTION 'Post-dedup composite dups remain: % — aborting.', n;
    END IF;
    RAISE NOTICE 'Post-dedup composite dups: 0 — UNIQUE constraint creation will succeed.';
END $$;

-- 5. Add the composite UNIQUE constraint.
-- IMPORTANT: We use a partial-style approach via a UNIQUE INDEX so we can document
-- the rationale. ALTER TABLE ... ADD CONSTRAINT ... UNIQUE creates a backing index;
-- a CREATE UNIQUE INDEX achieves the same end result and accepts IF NOT EXISTS.
--
-- The UNIQUE on single column (containernumber) is NOT possible while the 2
-- cross-scanner records exist. We chose composite (containernumber, scannertype).
CREATE UNIQUE INDEX IF NOT EXISTS ix_ccs_containernumber_scannertype_unique
    ON containercompletenessstatuses(containernumber, scannertype);

\echo '## POST-INDEX'
SELECT indexname, indexdef
FROM pg_indexes
WHERE tablename = 'containercompletenessstatuses'
  AND indexname = 'ix_ccs_containernumber_scannertype_unique';

COMMIT;

\echo '## OK — Sprint 5G1 follow-up Step B complete (39 same-scanner dups deleted; UNIQUE(containernumber, scannertype) added)'
\echo '## NOTE: FK4/FK5 (cbr/iad → ccs by containernumber) still cannot be added —'
\echo '##       parent is unique only on the composite key. Future fix would require'
\echo '##       composite reference on the child side. Flagged for Agent 1.3.'

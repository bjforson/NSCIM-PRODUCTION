-- 2026-05-05  Sprint 5G1 Step 2 — Audit finding 7.05
--
-- Eighteen distinct containers carry multiple isactive=true rows in
-- containerboerelations (37 excess rows total). Smoking gun is MRKU2369877
-- with 3 identical active rows (id 375/376/377, identical icumsboeid+scannertype
-- +createdat) — a triple-write bug from the 2026-03 ingestion path.
--
-- Decision per audit: keep the most recent (MAX(id)) per (containernumber,
-- isactive=true) group, deactivate the rest. Then add a partial unique index
-- so the bug cannot recur.
--
-- The partial unique index creation is the post-write integrity check —
-- it would fail if duplicates remained.
--
-- Run as: postgres superuser (DDL).
-- Idempotent: re-run is a no-op (UPDATE matches no rows on second run, and
-- CREATE INDEX uses IF NOT EXISTS).

\set ON_ERROR_STOP on

BEGIN;

-- 1. Snapshot pre-state for audit log
\echo '## PRE-STATE'
SELECT count(*) AS containers_with_dup_active_cbr
FROM (
  SELECT containernumber FROM containerboerelations
  WHERE isactive = true
  GROUP BY containernumber HAVING count(*) > 1
) sub;

SELECT count(*) AS excess_active_cbr_rows
FROM containerboerelations cbr
WHERE cbr.isactive = true
  AND cbr.id NOT IN (
    SELECT max(id) FROM containerboerelations
    WHERE isactive = true
    GROUP BY containernumber
  )
  AND cbr.containernumber IN (
    SELECT containernumber FROM containerboerelations
    WHERE isactive = true
    GROUP BY containernumber HAVING count(*) > 1
  );

-- 2. Deactivate all but the most-recent (MAX(id)) per container
UPDATE containerboerelations cbr
   SET isactive = false,
       lastvalidatedat = now() AT TIME ZONE 'UTC',
       notes = COALESCE(cbr.notes, '') ||
               CASE WHEN COALESCE(cbr.notes, '') = '' THEN '' ELSE ' | ' END ||
               '[5G1 dedup ' || to_char(now() AT TIME ZONE 'UTC', 'YYYY-MM-DD') ||
               '] superseded by id=' ||
               (SELECT max(id) FROM containerboerelations cbr2
                WHERE cbr2.containernumber = cbr.containernumber AND cbr2.isactive = true)::text
 WHERE cbr.isactive = true
   AND cbr.containernumber IN (
     SELECT containernumber FROM containerboerelations
     WHERE isactive = true
     GROUP BY containernumber HAVING count(*) > 1
   )
   AND cbr.id NOT IN (
     SELECT max(id) FROM containerboerelations
     WHERE isactive = true
     GROUP BY containernumber
   );

\echo '## POST-DEDUP'
SELECT count(*) AS containers_with_dup_active_cbr_after
FROM (
  SELECT containernumber FROM containerboerelations
  WHERE isactive = true
  GROUP BY containernumber HAVING count(*) > 1
) sub;
-- Expected: 0

-- 3. Add partial unique index to prevent recurrence.
-- The index will FAIL TO CREATE if any duplicates remain — this is the
-- correctness guarantee for the dedup step above.
CREATE UNIQUE INDEX IF NOT EXISTS ix_cbr_active_per_container
    ON containerboerelations(containernumber)
    WHERE isactive = true;

\echo '## POST-INDEX'
SELECT indexname, indexdef
FROM pg_indexes
WHERE tablename = 'containerboerelations'
  AND indexname = 'ix_cbr_active_per_container';

COMMIT;

\echo '## OK — Sprint 5G1 Step 2 complete (7.05 dedup + partial unique index)'

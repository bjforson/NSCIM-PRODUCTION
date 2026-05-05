-- 2026-05-05  Sprint 5G1 Step 5 — Audit finding 7.01
--
-- Add in-DB foreign-key constraints on the cargo-pipeline core. Per audit
-- 7.01, only auditdecisions.imageanalysisdecisionid currently has a real FK
-- (1 of 6 expected). Sprint 5G1 adds the well-bounded ones.
--
-- Step 1 (Sprint 5G1) cleared orphans for these candidates:
--   FK1: analysisassignments.groupid → analysisgroups.id          (0 violations)
--   FK2: analysisrecords.groupid       → analysisgroups.id          (0 violations)
--   FK3: analysisqueueentries.assignmentid → analysisassignments.id (0 violations)
--
-- SKIPPED:
--   FK4: containerboerelations.containernumber → ccs.containernumber
--        Reasons:
--          (a) CCS.containernumber is NOT unique (41 duplicates today,
--              including MRKU2369877 with 3 rows) — FK targets must be
--              UNIQUE or PRIMARY KEY.
--          (b) 480 violations remained at probe time (cf. audit 7.08:
--              481 active CBRs without CCS — pending-state by design).
--   FK5: imageanalysisdecisions.containernumber → ccs.containernumber
--        Reason: same uniqueness blocker as FK4. The 392 orphans from
--        7.06 are now archived (Step 1), so violations=0 — but a FK to a
--        non-unique parent column cannot be created.
--        A future fix would either:
--          * promote CCS.containernumber to a unique constraint (would
--            require collapsing the 41 dup rows first — non-trivial), or
--          * skip the row-level FK and keep the in-app discipline
--            (current state).
--
-- ON DELETE NO ACTION (default) — we want the constraint to FAIL the
-- DELETE rather than cascade. This is consistent with the existing
-- auditdecisions FK and the audit's recommendation.
--
-- Run as: postgres superuser (DDL).
-- Idempotent: re-run is a no-op (constraints use IF NOT EXISTS via
-- DO block since ALTER TABLE ... ADD CONSTRAINT does not support
-- IF NOT EXISTS in PG18).

\set ON_ERROR_STOP on

BEGIN;

-- FK1: analysisassignments.groupid → analysisgroups.id
DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM pg_constraint
        WHERE conname = 'fk_analysisassignments_analysisgroups_groupid'
    ) THEN
        ALTER TABLE analysisassignments
            ADD CONSTRAINT fk_analysisassignments_analysisgroups_groupid
            FOREIGN KEY (groupid) REFERENCES analysisgroups(id)
            ON DELETE NO ACTION ON UPDATE CASCADE;
    END IF;
END $$;

-- FK2: analysisrecords.groupid → analysisgroups.id
DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM pg_constraint
        WHERE conname = 'fk_analysisrecords_analysisgroups_groupid'
    ) THEN
        ALTER TABLE analysisrecords
            ADD CONSTRAINT fk_analysisrecords_analysisgroups_groupid
            FOREIGN KEY (groupid) REFERENCES analysisgroups(id)
            ON DELETE NO ACTION ON UPDATE CASCADE;
    END IF;
END $$;

-- FK3: analysisqueueentries.assignmentid → analysisassignments.id
DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM pg_constraint
        WHERE conname = 'fk_analysisqueueentries_analysisassignments_assignmentid'
    ) THEN
        ALTER TABLE analysisqueueentries
            ADD CONSTRAINT fk_analysisqueueentries_analysisassignments_assignmentid
            FOREIGN KEY (assignmentid) REFERENCES analysisassignments(id)
            ON DELETE NO ACTION ON UPDATE CASCADE;
    END IF;
END $$;

-- Verify
\echo '## POST-STATE — FKs on cargo-pipeline core'
SELECT tc.table_name, tc.constraint_name, kcu.column_name,
       ccu.table_name  AS parent_table,
       ccu.column_name AS parent_column,
       rc.delete_rule
FROM information_schema.table_constraints tc
JOIN information_schema.key_column_usage kcu
  ON tc.constraint_name = kcu.constraint_name AND tc.table_schema = kcu.table_schema
JOIN information_schema.constraint_column_usage ccu
  ON tc.constraint_name = ccu.constraint_name AND tc.table_schema = ccu.table_schema
JOIN information_schema.referential_constraints rc
  ON rc.constraint_name = tc.constraint_name AND rc.constraint_schema = tc.table_schema
WHERE tc.table_schema='public' AND tc.constraint_type='FOREIGN KEY'
  AND tc.table_name IN ('analysisassignments','analysisrecords','analysisqueueentries',
                         'auditdecisions','containerboerelations','imageanalysisdecisions')
ORDER BY tc.table_name, tc.constraint_name;

COMMIT;

\echo '## OK — Sprint 5G1 Step 5 complete (3 cargo-pipeline FKs added; FK4/FK5 documented as skipped)'

-- ─────────────────────────────────────────────────────────────────────────────
-- 1.15.0 — Link AnalysisGroup rows to RecordCompletenessStatus
--
-- Phase 2 of Option C: add the new FK column on analysisgroups and backfill it
-- from existing data by matching GroupIdentifier (= declaration number in the
-- non-consolidated case) to the new record table.
--
-- Strictly additive. The old parentgroupid column stays in place for backward
-- compat; 1.16.0 will drop it.
--
-- Idempotent: re-runs are safe.
-- ─────────────────────────────────────────────────────────────────────────────

\timing on

BEGIN;

-- Add the column
ALTER TABLE analysisgroups
    ADD COLUMN IF NOT EXISTS recordcompletenessstatusid INT NULL;

-- Index for join performance
CREATE INDEX IF NOT EXISTS ix_analysisgroups_recordcompletenessstatusid
    ON analysisgroups (recordcompletenessstatusid)
    WHERE recordcompletenessstatusid IS NOT NULL;

-- Backfill: match existing AnalysisGroup rows to RecordCompletenessStatus rows
-- by GroupIdentifier (or NormalizedGroupIdentifier) = DeclarationNumber. The
-- non-consolidated majority match cleanly; Pattern A consolidated groups with
-- a container-number GroupIdentifier won't match (they stay unlinked until
-- 1.15.0 creates them record-first).
UPDATE analysisgroups ag
   SET recordcompletenessstatusid = r.id
  FROM recordcompletenessstatuses r
 WHERE ag.recordcompletenessstatusid IS NULL
   AND (
           ag.groupidentifier = r.declarationnumber
        OR ag.normalizedgroupidentifier = r.declarationnumber
       );

-- Report backfill outcome
SELECT
    'AnalysisGroup rows total' AS metric, COUNT(*) AS value FROM analysisgroups
UNION ALL SELECT 'Linked to a record', COUNT(*) FROM analysisgroups WHERE recordcompletenessstatusid IS NOT NULL
UNION ALL SELECT 'Still unlinked',     COUNT(*) FROM analysisgroups WHERE recordcompletenessstatusid IS NULL;

COMMIT;

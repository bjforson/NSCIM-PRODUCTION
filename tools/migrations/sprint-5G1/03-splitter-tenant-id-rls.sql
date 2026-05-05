-- 2026-05-05  Sprint 5G1 Step 4 — Audit finding 7.03
--
-- splitter_consensus_corpus is the splitter-training-data table (24 rows of
-- training data + image bytea, 14 columns). It was added post phase-1 RLS
-- rollout and never tenanted. Same pattern as analysisqueueentries (Step 3).
--
-- Per audit: "Either tenant it (per phase-1 pattern) or move to a dedicated
-- training DB. Either is fine; current state is the inconsistent one." We
-- choose the phase-1 pattern for consistency.
--
-- Run as: postgres superuser (DDL).
-- Idempotent: re-run is a no-op.

\set ON_ERROR_STOP on

BEGIN;

-- 1. Add tenant_id column with phase-1 default expression
ALTER TABLE splitter_consensus_corpus
    ADD COLUMN IF NOT EXISTS tenant_id BIGINT NOT NULL
    DEFAULT COALESCE(NULLIF(current_setting('app.tenant_id', true), ''), '1')::bigint;

-- 2. Backfill (defensive)
UPDATE splitter_consensus_corpus
   SET tenant_id = 1
 WHERE tenant_id IS NULL OR tenant_id = 0;

-- 3. Index — note splitter_consensus_corpus uses snake_case columns; PK col
-- name is `id` per the existing schema (Probe DbIntegrity3 confirmed).
CREATE INDEX IF NOT EXISTS ix_splitter_consensus_corpus_tenant_id
    ON splitter_consensus_corpus(tenant_id, id);

-- 4. Enable + force RLS
ALTER TABLE splitter_consensus_corpus ENABLE ROW LEVEL SECURITY;
ALTER TABLE splitter_consensus_corpus FORCE ROW LEVEL SECURITY;

-- 5. Policy
DROP POLICY IF EXISTS tenant_isolation_splitter_consensus_corpus ON splitter_consensus_corpus;
CREATE POLICY tenant_isolation_splitter_consensus_corpus ON splitter_consensus_corpus
    USING (tenant_id = COALESCE(NULLIF(current_setting('app.tenant_id', true), ''), '0')::bigint)
    WITH CHECK (tenant_id = COALESCE(NULLIF(current_setting('app.tenant_id', true), ''), '0')::bigint);

-- 6. Grants
GRANT SELECT, INSERT, UPDATE, DELETE ON splitter_consensus_corpus TO nscim_app;

-- 7. Verify
\echo '## POST-STATE'
SELECT relname,
       relrowsecurity AS rls_enabled,
       relforcerowsecurity AS rls_forced,
       (SELECT count(*) FROM pg_policy WHERE polrelid = c.oid) AS policy_count
FROM pg_class c
WHERE relname = 'splitter_consensus_corpus';
-- Expected: rls_enabled=true, rls_forced=true, policy_count=1

SELECT count(*) AS rows_in_scc FROM splitter_consensus_corpus;

COMMIT;

\echo '## OK — Sprint 5G1 Step 4 complete (7.03 tenant_id + RLS on splitter_consensus_corpus)'

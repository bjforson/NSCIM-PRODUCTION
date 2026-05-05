-- 2026-05-05  Sprint 5G1 Step 3 — Audit finding 7.02
--
-- analysisqueueentries was added by EF migration
-- 20260423180227_AddSplitJobIdToCrossRecordScans (post phase-1 RLS rollout)
-- and never tenanted. Per topology agent + finding 7.02, this is the only
-- operationally-active table in nickscan_production without RLS.
--
-- Today the risk is latent (single-tenant prod) but the table drives
-- cross-tenant assignment eligibility.
--
-- This migration:
--   1. Adds tenant_id BIGINT NOT NULL with phase-1-style default
--   2. Backfills any existing rows (defensive, default takes care of new rows)
--   3. Enables + forces RLS
--   4. Creates tenant_isolation_analysisqueueentries policy
--   5. Adds the standard tenant_id + id covering index
--   6. Grants nscim_app the standard rights (already have implicit, but explicit
--      for the new column is harmless)
--
-- Run as: postgres superuser (DDL).
-- Idempotent: re-run is a no-op (IF NOT EXISTS / DROP IF EXISTS / etc.).

\set ON_ERROR_STOP on

BEGIN;

-- 1. Add tenant_id column with phase-1 default expression
ALTER TABLE analysisqueueentries
    ADD COLUMN IF NOT EXISTS tenant_id BIGINT NOT NULL
    DEFAULT COALESCE(NULLIF(current_setting('app.tenant_id', true), ''), '1')::bigint;

-- 2. Backfill (defensive — column-level NOT NULL DEFAULT already covers existing rows)
UPDATE analysisqueueentries
   SET tenant_id = 1
 WHERE tenant_id IS NULL OR tenant_id = 0;

-- 3. Index — same shape as the rest of the phase-1 family
CREATE INDEX IF NOT EXISTS ix_analysisqueueentries_tenant_id
    ON analysisqueueentries(tenant_id, assignmentid);

-- 4. Enable + force RLS (matches the 24-force-rls-and-fail-closed.sql pattern)
ALTER TABLE analysisqueueentries ENABLE ROW LEVEL SECURITY;
ALTER TABLE analysisqueueentries FORCE ROW LEVEL SECURITY;

-- 5. Policy with fail-closed default ('0' for unset app.tenant_id)
DROP POLICY IF EXISTS tenant_isolation_analysisqueueentries ON analysisqueueentries;
CREATE POLICY tenant_isolation_analysisqueueentries ON analysisqueueentries
    USING (tenant_id = COALESCE(NULLIF(current_setting('app.tenant_id', true), ''), '0')::bigint)
    WITH CHECK (tenant_id = COALESCE(NULLIF(current_setting('app.tenant_id', true), ''), '0')::bigint);

-- 6. Grants (re-confirm; nscim_app already has the underlying privileges from
-- the parent table, but explicit for clarity)
GRANT SELECT, INSERT, UPDATE, DELETE ON analysisqueueentries TO nscim_app;

-- 7. Verify
\echo '## POST-STATE'
SELECT relname,
       relrowsecurity AS rls_enabled,
       relforcerowsecurity AS rls_forced,
       (SELECT count(*) FROM pg_policy WHERE polrelid = c.oid) AS policy_count
FROM pg_class c
WHERE relname = 'analysisqueueentries';
-- Expected: rls_enabled=true, rls_forced=true, policy_count=1

SELECT count(*) AS rows_in_aqe FROM analysisqueueentries;

COMMIT;

\echo '## OK — Sprint 5G1 Step 3 complete (7.02 tenant_id + RLS on analysisqueueentries)'

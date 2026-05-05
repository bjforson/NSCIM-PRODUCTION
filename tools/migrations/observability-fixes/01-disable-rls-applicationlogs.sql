-- 2026-05-05  Audit finding 8.01 — applicationlogs PG sink dead since 2026-04-25
--
-- Per the 2026-05-05 cold audit (docs/audit/2026-05-05/08-observability.md
-- finding 8.01 + REPORT.md Theme A), the Serilog PostgreSQL sink had been
-- silently rejecting every INSERT into applicationlogs since the phase-1 RLS
-- rollout deployed 2026-04-25 19:53:21 UTC. The sink uses raw Npgsql (not the
-- EF TenantConnectionInterceptor), and the connection-options approach
-- (Options=-c app.tenant_id=1, landed in 3f984f3) appears to work for direct
-- INSERTs but does not propagate through the sink's batch-COPY path.
--
-- Decision: per the audit's preferred fix, REMOVE RLS from applicationlogs.
-- Reasoning:
--   - applicationlogs is an OPS table, not tenant-business data.
--   - Single-tenant production today; multi-tenant policies for ops data are
--     a Phase-2 concern, not a Phase-1 invariant.
--   - The protective intent is satisfied by cluster-level access control
--     (only nscim_app and postgres roles can connect).
--   - The downside of preserving RLS — silent log loss — is far worse than
--     the upside.
--
-- Applied: 2026-05-05 ~11:33 UTC against production. Verified live:
-- 9 rows landed within 15 seconds of policy drop.
--
-- Run as: postgres superuser (DDL).
-- Idempotent: re-run is a no-op.

\set ON_ERROR_STOP on

BEGIN;

-- Drop the per-row policy
DROP POLICY IF EXISTS tenant_isolation_applicationlogs ON applicationlogs;

-- Disable RLS at the table level
ALTER TABLE applicationlogs DISABLE ROW LEVEL SECURITY;
ALTER TABLE applicationlogs NO FORCE ROW LEVEL SECURITY;

-- Document the decision so future operators know why this table is
-- intentionally outside the phase-1 RLS pattern.
COMMENT ON TABLE applicationlogs IS
  'Ops/diagnostic log table. RLS removed 2026-05-05 per audit finding 8.01: '
  'tenant_id propagation through the Serilog PostgreSQL sink''s batch-COPY '
  'path could not be made reliable, and applicationlogs is ops data not '
  'tenant data. Phase-2 multi-tenancy for ops surfaces is a separate '
  'design decision (likely: per-tenant log views, not RLS at the row level).';

-- Verify post-state
SELECT relname,
       relrowsecurity AS rls_enabled,
       relforcerowsecurity AS rls_forced,
       (SELECT count(*) FROM pg_policy WHERE polrelid = c.oid) AS policy_count
FROM pg_class c
WHERE relname = 'applicationlogs';
-- Expected: rls_enabled=false, rls_forced=false, policy_count=0

COMMIT;

-- =============================================================================
-- NICKSCAN ERP SOLUTION - Phase 1 - Harden tenant RLS (2026-04-25)
--
-- This migration tightens every existing tenant_isolation_* policy in two
-- ways:
--
--   1. ALTER TABLE ... FORCE ROW LEVEL SECURITY
--      Without FORCE, the table OWNER bypasses RLS. Tables in our public
--      schema are owned by `postgres` which is also a superuser (so it
--      bypasses anyway today), but FORCE closes the door if a future
--      non-superuser owner ever appears, and it's standard hardening.
--
--   2. Replace the COALESCE default '1' with '0' (fail-closed)
--      The legacy policy expression
--          tenant_id = COALESCE(NULLIF(current_setting('app.tenant_id'),''), '1')::bigint
--      treated any session that didn't SET app.tenant_id as tenant 1 — which
--      meant every connection saw all data, since today every real row IS
--      tenant 1. As of the companion code commit, the
--      TenantConnectionInterceptor explicitly sets app.tenant_id on every
--      pooled connection, so the COALESCE branch is no longer needed for
--      the happy path. Switching the default to '0' makes any session
--      that bypasses the interceptor (manual psql, unwired future module,
--      misconfigured background job) see zero rows instead of all rows.
--
-- This is idempotent — re-running it just rewrites every policy with the
-- same fail-closed body and re-asserts FORCE.
--
-- Apply order:  run on nickscan_production, then nick_comms, then nickhr.
-- (See the dispatch loop at the bottom of this file for the canonical
--  per-database invocation block, OR call psql three times.)
-- =============================================================================

DO $$
DECLARE
    rec   RECORD;
    expr  TEXT := 'tenant_id = (COALESCE(NULLIF(current_setting(''app.tenant_id'', true), ''''), ''0'')::bigint)';
    n_pol INT  := 0;
    n_tbl INT  := 0;
BEGIN
    FOR rec IN
        SELECT n.nspname AS schema_name,
               c.relname AS table_name,
               p.polname AS policy_name
        FROM pg_class c
        JOIN pg_namespace n ON n.oid = c.relnamespace
        JOIN pg_policy   p ON p.polrelid = c.oid
        WHERE c.relkind  = 'r'
          AND n.nspname  = 'public'
          AND c.relrowsecurity = true
          AND p.polname LIKE 'tenant_isolation_%'
        ORDER BY c.relname, p.polname
    LOOP
        -- Recreate the policy with the fail-closed default. CREATE POLICY's
        -- default command set is ALL, and supplying USING + WITH CHECK with
        -- the same expression matches the original semantics exactly.
        EXECUTE format(
            'DROP POLICY %I ON %I.%I',
            rec.policy_name, rec.schema_name, rec.table_name);
        EXECUTE format(
            'CREATE POLICY %I ON %I.%I FOR ALL USING (%s) WITH CHECK (%s)',
            rec.policy_name, rec.schema_name, rec.table_name, expr, expr);
        n_pol := n_pol + 1;
    END LOOP;

    -- Apply FORCE to every table that has RLS enabled, even if no
    -- tenant_isolation_* policy was found (defensive — a future custom
    -- policy still benefits from FORCE).
    FOR rec IN
        SELECT n.nspname AS schema_name, c.relname AS table_name
        FROM pg_class c
        JOIN pg_namespace n ON n.oid = c.relnamespace
        WHERE c.relkind = 'r'
          AND n.nspname = 'public'
          AND c.relrowsecurity = true
          AND c.relforcerowsecurity = false
        ORDER BY c.relname
    LOOP
        EXECUTE format(
            'ALTER TABLE %I.%I FORCE ROW LEVEL SECURITY',
            rec.schema_name, rec.table_name);
        n_tbl := n_tbl + 1;
    END LOOP;

    RAISE NOTICE '24-force-rls-and-fail-closed: rewrote % policies, forced RLS on % tables', n_pol, n_tbl;
END $$;

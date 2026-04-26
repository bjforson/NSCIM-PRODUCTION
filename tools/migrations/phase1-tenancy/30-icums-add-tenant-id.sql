-- =============================================================================
-- NICKSCAN ERP SOLUTION - Phase 1 - Add tenant_id + RLS to nickscan_icums
--
-- The 4 tables in nickscan_icums (icumbatchlogs, icumcontainerdata,
-- icumdocuments, icummanifestitems) hold customs/cargo data per tenant in
-- the multi-tenancy model. Until now they had no tenant_id column at all,
-- so RLS couldn't apply — making this a Phase-2-completing piece of the
-- tenancy story.
--
-- This script:
--   1. Adds `tenant_id BIGINT NOT NULL DEFAULT 1` to every table
--      (existing rows backfill to tenant 1, the operations tenant).
--   2. Adds a (tenant_id, id) covering index to keep PK lookups O(log n)
--      under the RLS predicate.
--   3. Enables ROW LEVEL SECURITY + FORCE ROW LEVEL SECURITY on every table.
--   4. Adds a tenant_isolation_<table> policy with the same fail-closed
--      COALESCE expression used by 24-force-rls-and-fail-closed.sql
--      (default '0' so an unwired connection sees no rows).
--
-- The policies will start filtering immediately because the
-- TenantConnectionInterceptor already pushes app.tenant_id on every pooled
-- connection (commit 9d05726). EF entity classes don't yet have a TenantId
-- property — that's intentional. The DB-level DEFAULT 1 backfills new rows
-- and keeps the C# code unchanged for now; the entity-side ITenantOwned
-- adoption is a follow-up when tenant 2 is provisioned.
--
-- Idempotent: re-running just re-asserts the column (IF NOT EXISTS), the
-- policy (DROP IF EXISTS + CREATE), and FORCE RLS.
-- =============================================================================

\echo Applying tenancy rollout to nickscan_icums

DO $$
DECLARE
    rec RECORD;
    expr TEXT := 'tenant_id = (COALESCE(NULLIF(current_setting(''app.tenant_id'', true), ''''), ''0'')::bigint)';
    n_tables INT := 0;
BEGIN
    FOR rec IN
        SELECT c.relname AS table_name
        FROM pg_class c
        JOIN pg_namespace n ON n.oid = c.relnamespace
        WHERE c.relkind = 'r'
          AND n.nspname = 'public'
          AND c.relname NOT LIKE '\_\_%'
        ORDER BY c.relname
    LOOP
        -- 1. Add tenant_id (default 1 = ops tenant). Existing rows take the default.
        EXECUTE format(
            'ALTER TABLE public.%I ADD COLUMN IF NOT EXISTS tenant_id BIGINT NOT NULL DEFAULT 1',
            rec.table_name);

        -- 2. Composite (tenant_id, ...) index. Postgres can scan the existing
        --    PK index when filtering by tenant, but a small composite keeps
        --    range scans cheap as cardinality grows. Same shape as
        --    "ix_<table>_tenant_id" used by other phase-1 migrations.
        EXECUTE format(
            'CREATE INDEX IF NOT EXISTS ix_%I_tenant_id ON public.%I(tenant_id)',
            rec.table_name, rec.table_name);

        -- 3. Enable + force RLS.
        EXECUTE format('ALTER TABLE public.%I ENABLE ROW LEVEL SECURITY', rec.table_name);
        EXECUTE format('ALTER TABLE public.%I FORCE ROW LEVEL SECURITY', rec.table_name);

        -- 4. Recreate the tenant_isolation policy with the fail-closed default.
        EXECUTE format('DROP POLICY IF EXISTS tenant_isolation_%I ON public.%I',
            rec.table_name, rec.table_name);
        EXECUTE format(
            'CREATE POLICY tenant_isolation_%I ON public.%I FOR ALL USING (%s) WITH CHECK (%s)',
            rec.table_name, rec.table_name, expr, expr);

        n_tables := n_tables + 1;
    END LOOP;

    RAISE NOTICE '30-icums-add-tenant-id: rolled out tenancy on % tables', n_tables;
END $$;

-- Grant nscim_app the privileges it needs on the new column. Existing grants
-- don't auto-propagate to new columns when SELECT is granted column-level
-- (we use table-level grants here, so the new column is implicitly covered).
-- This is a no-op if grants are already correct.
DO $$
BEGIN
    IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'nscim_app') THEN
        GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA public TO nscim_app;
    END IF;
END $$;

\echo Done with nickscan_icums

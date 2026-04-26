-- =============================================================================
-- NICKSCAN ERP SOLUTION - Phase 1 - Add tenant_id + RLS to nickscan_downloads
--
-- The 11 tables in nickscan_downloads (archivedfiles, boedocuments,
-- cmrredownloadqueue, cmrvalidationmetrics, containerdownloadhistory,
-- downloadedfiles, failedprocessingqueue, icumsdownloadqueue, ingestionlogs,
-- manifestitems, vehicleimports) hold per-tenant download/CMR/manifest
-- artefacts. Same rollout pattern as 30-icums-add-tenant-id.sql — see that
-- file for the design rationale and idempotency story.
-- =============================================================================

\echo Applying tenancy rollout to nickscan_downloads

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
        EXECUTE format(
            'ALTER TABLE public.%I ADD COLUMN IF NOT EXISTS tenant_id BIGINT NOT NULL DEFAULT 1',
            rec.table_name);

        EXECUTE format(
            'CREATE INDEX IF NOT EXISTS ix_%I_tenant_id ON public.%I(tenant_id)',
            rec.table_name, rec.table_name);

        EXECUTE format('ALTER TABLE public.%I ENABLE ROW LEVEL SECURITY', rec.table_name);
        EXECUTE format('ALTER TABLE public.%I FORCE ROW LEVEL SECURITY', rec.table_name);

        EXECUTE format('DROP POLICY IF EXISTS tenant_isolation_%I ON public.%I',
            rec.table_name, rec.table_name);
        EXECUTE format(
            'CREATE POLICY tenant_isolation_%I ON public.%I FOR ALL USING (%s) WITH CHECK (%s)',
            rec.table_name, rec.table_name, expr, expr);

        n_tables := n_tables + 1;
    END LOOP;

    RAISE NOTICE '31-downloads-add-tenant-id: rolled out tenancy on % tables', n_tables;
END $$;

DO $$
BEGIN
    IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'nscim_app') THEN
        GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA public TO nscim_app;
    END IF;
END $$;

\echo Done with nickscan_downloads

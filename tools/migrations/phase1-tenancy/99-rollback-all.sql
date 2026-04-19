-- =============================================================================
-- NICKSCAN ERP SOLUTION — Phase 1 — Full rollback
--
-- Run against EACH module DB individually:
--   psql -h localhost -U postgres -d nickscan_production -f 99-rollback-all.sql
--   psql -h localhost -U postgres -d nickhr             -f 99-rollback-all.sql
--   psql -h localhost -U postgres -d nick_comms         -f 99-rollback-all.sql
--
-- This drops the tenant_id column, removes RLS policies, and disables RLS.
-- The platform DB (nick_platform) is NOT touched by this script — drop it
-- manually if you also want to remove the canonical tenants table.
--
-- WARNING: backup first.
-- =============================================================================
BEGIN;
SET LOCAL search_path = public;

DO $$
DECLARE
    r RECORD;
BEGIN
    FOR r IN
        SELECT table_name FROM information_schema.tables
        WHERE table_schema = 'public' AND table_type = 'BASE TABLE'
          AND table_name <> '__EFMigrationsHistory'
    LOOP
        -- Drop policy if it exists
        EXECUTE format('DROP POLICY IF EXISTS %I ON %I',
                       'tenant_isolation_' || r.table_name, r.table_name);
        -- Disable RLS
        EXECUTE format('ALTER TABLE %I DISABLE ROW LEVEL SECURITY', r.table_name);
        -- Drop the index
        EXECUTE format('DROP INDEX IF EXISTS %I', 'ix_' || r.table_name || '_tenant_id');
        -- Drop the column
        EXECUTE format('ALTER TABLE %I DROP COLUMN IF EXISTS tenant_id', r.table_name);
    END LOOP;
END $$;

COMMIT;

\echo
\echo === Rollback complete. Re-run 90-verify.sql to confirm. ===

-- =============================================================================
-- NICKSCAN ERP SOLUTION — Phase 1 — Post-migration verification
--
-- Run against each module DB to confirm the migration succeeded.
--
--   psql -h localhost -U postgres -d <dbname> -f 90-verify.sql
-- =============================================================================

\echo
\echo === Tables WITHOUT a tenant_id column ===
SELECT t.table_name
FROM information_schema.tables t
WHERE t.table_schema = 'public'
  AND t.table_type = 'BASE TABLE'
  AND t.table_name <> '__EFMigrationsHistory'
  AND NOT EXISTS (
    SELECT 1 FROM information_schema.columns c
    WHERE c.table_schema = t.table_schema
      AND c.table_name = t.table_name
      AND c.column_name = 'tenant_id')
ORDER BY t.table_name;

\echo
\echo === Tables with rows where tenant_id IS NULL or != 1 (should be empty) ===
DO $$
DECLARE
    r RECORD;
    cnt BIGINT;
BEGIN
    FOR r IN
        SELECT table_name FROM information_schema.tables
        WHERE table_schema = 'public' AND table_type = 'BASE TABLE'
          AND table_name <> '__EFMigrationsHistory'
          AND EXISTS (
            SELECT 1 FROM information_schema.columns
            WHERE table_schema = 'public' AND table_name = information_schema.tables.table_name
              AND column_name = 'tenant_id')
    LOOP
        EXECUTE format('SELECT COUNT(*) FROM %I WHERE tenant_id IS NULL OR tenant_id <> 1', r.table_name) INTO cnt;
        IF cnt > 0 THEN
            RAISE NOTICE 'BAD: % has % rows with non-1 tenant_id', r.table_name, cnt;
        END IF;
    END LOOP;
END $$;

\echo
\echo === Tables WITHOUT Row-Level Security enabled ===
SELECT c.relname AS table_name
FROM pg_class c
JOIN pg_namespace n ON n.oid = c.relnamespace
WHERE n.nspname = 'public'
  AND c.relkind = 'r'
  AND c.relname <> '__EFMigrationsHistory'
  AND NOT c.relrowsecurity
ORDER BY c.relname;

\echo
\echo === Tables WITHOUT a tenant_isolation policy ===
SELECT c.relname AS table_name
FROM pg_class c
JOIN pg_namespace n ON n.oid = c.relnamespace
WHERE n.nspname = 'public'
  AND c.relkind = 'r'
  AND c.relname <> '__EFMigrationsHistory'
  AND NOT EXISTS (
    SELECT 1 FROM pg_policy p
    WHERE p.polrelid = c.oid AND p.polname LIKE 'tenant_isolation_%')
ORDER BY c.relname;

\echo
\echo === Summary ===
SELECT 'tenant_id columns added' AS metric, COUNT(*)
FROM information_schema.columns
WHERE table_schema = 'public' AND column_name = 'tenant_id'
UNION ALL
SELECT 'RLS-enabled tables', COUNT(*)
FROM pg_class c JOIN pg_namespace n ON n.oid = c.relnamespace
WHERE n.nspname = 'public' AND c.relkind = 'r' AND c.relrowsecurity
UNION ALL
SELECT 'tenant_isolation policies', COUNT(*)
FROM pg_policy WHERE polname LIKE 'tenant_isolation_%';

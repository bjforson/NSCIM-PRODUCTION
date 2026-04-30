-- =============================================================================
-- Postgres Row-Level Security (RLS) for NickFinance multi-tenant isolation.
--
-- Defence-in-depth atop the EF query filters that already enforce
-- `WHERE tenant_id = ?` from C#. Even if a code path forgets the filter
-- (or a future Razor page constructs a hand-written query and skips it),
-- the database refuses to return rows from other tenants.
--
-- How it works:
--   * Every business table that has a `tenant_id` column gets RLS enabled.
--   * A single policy per table — `tenant_isolation` — uses
--     `current_setting('nickerp.current_tenant_id', true)` to scope rows.
--   * The WebApp's `TenantSessionInterceptor` prepends
--       `SET nickerp.current_tenant_id = '<tenant>'`
--     to every command, so each query session is scoped to the
--     authenticated tenant.
--   * When the variable is *unset* (NULL), the policy permits all rows.
--     This keeps the bootstrap CLI (which runs as `postgres` superuser
--     and never sets the GUC) working unchanged. Postgres superusers
--     also bypass RLS by default, so this is belt-and-braces.
--
-- Idempotent — re-runnable. ALTER TABLE ... ENABLE ROW LEVEL SECURITY is
-- a no-op when already enabled; CREATE POLICY drops + recreates.
--
-- Run as: psql -h localhost -U postgres -d nickhr -f apply-rls-policies.sql
-- (or via NickFinance.Database.Bootstrap which applies it after migrations).
-- =============================================================================

DO $$
DECLARE
    r RECORD;
    counter INT := 0;
BEGIN
    FOR r IN
        SELECT n.nspname AS schema_name, c.relname AS table_name
        FROM pg_class c
        JOIN pg_namespace n ON n.oid = c.relnamespace
        WHERE c.relkind = 'r'
          AND n.nspname IN ('finance','petty_cash','coa','ar','ap','banking','fixed_assets','budgeting','identity')
          AND EXISTS (
              SELECT 1 FROM pg_attribute a
              WHERE a.attrelid = c.oid
                AND a.attname = 'tenant_id'
                AND NOT a.attisdropped
          )
        ORDER BY n.nspname, c.relname
    LOOP
        EXECUTE format('ALTER TABLE %I.%I ENABLE ROW LEVEL SECURITY', r.schema_name, r.table_name);
        EXECUTE format('DROP POLICY IF EXISTS tenant_isolation ON %I.%I', r.schema_name, r.table_name);
        EXECUTE format($f$
            CREATE POLICY tenant_isolation ON %I.%I
                USING (
                    current_setting('nickerp.current_tenant_id', true) IS NULL
                    OR current_setting('nickerp.current_tenant_id', true) = ''
                    OR tenant_id = current_setting('nickerp.current_tenant_id', true)::bigint
                )
                WITH CHECK (
                    current_setting('nickerp.current_tenant_id', true) IS NULL
                    OR current_setting('nickerp.current_tenant_id', true) = ''
                    OR tenant_id = current_setting('nickerp.current_tenant_id', true)::bigint
                )
        $f$, r.schema_name, r.table_name);
        counter := counter + 1;
        RAISE NOTICE 'RLS enabled on %.%', r.schema_name, r.table_name;
    END LOOP;
    RAISE NOTICE 'Done — RLS applied on % tables.', counter;
END $$;

-- nscim_app must NOT have BYPASSRLS — the policy is what protects us.
-- postgres superuser bypasses RLS by default (used by bootstrap CLI for
-- DDL); that's expected and fine.
ALTER ROLE nscim_app NOBYPASSRLS;

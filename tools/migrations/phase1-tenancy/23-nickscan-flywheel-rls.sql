-- =============================================================================
-- NICKSCAN ERP SOLUTION - Phase 1 (follow-up) - Enable RLS on post-Phase-1 tables
--
-- Companion to 13-nickscan-flywheel-add-tenant-id.sql. Enables row-level
-- security on the 5 tables added after Phase 1 landed, using the same
-- policy shape as 20-nickscan-production-rls.sql: filter by
-- current_setting('app.tenant_id'), default to tenant 1 if unset. The
-- TenantOwnedEntityInterceptor sets this session variable on every
-- connection; the postgres superuser bypasses RLS via BYPASSRLS (default).
--
-- Idempotent. Safe to re-run — DROP POLICY IF EXISTS + CREATE POLICY.
--
-- MUST BE RUN AFTER 13-nickscan-flywheel-add-tenant-id.sql (the policies
-- reference the tenant_id column).
--
-- Run as:
--   psql -h localhost -U postgres -d nickscan_production -f 23-nickscan-flywheel-rls.sql
-- =============================================================================
BEGIN;
SET LOCAL search_path = public;

-- threatcategories
ALTER TABLE "threatcategories" ENABLE ROW LEVEL SECURITY;
DROP POLICY IF EXISTS "tenant_isolation_threatcategories" ON "threatcategories";
CREATE POLICY "tenant_isolation_threatcategories" ON "threatcategories"
    USING (tenant_id = COALESCE(NULLIF(current_setting('app.tenant_id', true), ''), '1')::bigint)
    WITH CHECK (tenant_id = COALESCE(NULLIF(current_setting('app.tenant_id', true), ''), '1')::bigint);

-- revenueanomalycategories
ALTER TABLE "revenueanomalycategories" ENABLE ROW LEVEL SECURITY;
DROP POLICY IF EXISTS "tenant_isolation_revenueanomalycategories" ON "revenueanomalycategories";
CREATE POLICY "tenant_isolation_revenueanomalycategories" ON "revenueanomalycategories"
    USING (tenant_id = COALESCE(NULLIF(current_setting('app.tenant_id', true), ''), '1')::bigint)
    WITH CHECK (tenant_id = COALESCE(NULLIF(current_setting('app.tenant_id', true), ''), '1')::bigint);

-- manifestsnapshots
ALTER TABLE "manifestsnapshots" ENABLE ROW LEVEL SECURITY;
DROP POLICY IF EXISTS "tenant_isolation_manifestsnapshots" ON "manifestsnapshots";
CREATE POLICY "tenant_isolation_manifestsnapshots" ON "manifestsnapshots"
    USING (tenant_id = COALESCE(NULLIF(current_setting('app.tenant_id', true), ''), '1')::bigint)
    WITH CHECK (tenant_id = COALESCE(NULLIF(current_setting('app.tenant_id', true), ''), '1')::bigint);

-- matchqualityflags
ALTER TABLE "matchqualityflags" ENABLE ROW LEVEL SECURITY;
DROP POLICY IF EXISTS "tenant_isolation_matchqualityflags" ON "matchqualityflags";
CREATE POLICY "tenant_isolation_matchqualityflags" ON "matchqualityflags"
    USING (tenant_id = COALESCE(NULLIF(current_setting('app.tenant_id', true), ''), '1')::bigint)
    WITH CHECK (tenant_id = COALESCE(NULLIF(current_setting('app.tenant_id', true), ''), '1')::bigint);

-- auditimagedecisions
ALTER TABLE "auditimagedecisions" ENABLE ROW LEVEL SECURITY;
DROP POLICY IF EXISTS "tenant_isolation_auditimagedecisions" ON "auditimagedecisions";
CREATE POLICY "tenant_isolation_auditimagedecisions" ON "auditimagedecisions"
    USING (tenant_id = COALESCE(NULLIF(current_setting('app.tenant_id', true), ''), '1')::bigint)
    WITH CHECK (tenant_id = COALESCE(NULLIF(current_setting('app.tenant_id', true), ''), '1')::bigint);

COMMIT;

-- Verification (run manually after the transaction commits):
--   SELECT relname, relrowsecurity FROM pg_class
--   WHERE relname IN ('threatcategories','revenueanomalycategories',
--                     'manifestsnapshots','matchqualityflags','auditimagedecisions')
--   ORDER BY relname;
-- Expected: 5 rows, all with relrowsecurity = t.
--
--   SELECT polname, tablename FROM pg_policies WHERE tablename IN
--     ('threatcategories','revenueanomalycategories','manifestsnapshots',
--      'matchqualityflags','auditimagedecisions') ORDER BY tablename;
-- Expected: 5 rows, one policy per table.

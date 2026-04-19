-- =============================================================================
-- NICKSCAN ERP SOLUTION - Phase 1 (follow-up) - Add tenant_id to post-Phase-1 tables
--
-- Context: Phase 1 (commit 6de4f1a3, April 2026) added tenant_id + RLS to 74
-- NSCIM tables. Five new tables were created AFTER Phase 1 and missed the
-- original pass:
--   - threatcategories              (1.9.0 AI flywheel Gap 1a)
--   - revenueanomalycategories      (1.9.0 AI flywheel Gap 1a)
--   - manifestsnapshots             (1.9.0 AI flywheel Gap 0)
--   - matchqualityflags             (1.10.0 Match Correction Tool)
--   - auditimagedecisions           (1.10.0 per-image audit)
--
-- Without this script, the 5 new tables lack the `tenant_id` column and the
-- composite (tenant_id, id) index that every other production table has,
-- which will cause tenant-aware queries to fail once NSCIM goes multi-tenant.
-- Prod is currently single-tenant so nothing is broken today; this is
-- preventive.
--
-- This script follows the exact pattern from 10-nickscan-production-add-tenant-id.sql:
--   ALTER TABLE ... ADD COLUMN IF NOT EXISTS tenant_id BIGINT NOT NULL DEFAULT 1;
--   CREATE INDEX IF NOT EXISTS ix_<table>_tenant_id ON <table> (tenant_id, id);
--
-- Idempotent. Safe to re-run.
--
-- Run as:
--   psql -h localhost -U postgres -d nickscan_production -f 13-nickscan-flywheel-add-tenant-id.sql
-- =============================================================================
BEGIN;
SET LOCAL search_path = public;

-- threatcategories
ALTER TABLE "threatcategories" ADD COLUMN IF NOT EXISTS tenant_id BIGINT NOT NULL DEFAULT 1;
CREATE INDEX IF NOT EXISTS "ix_threatcategories_tenant_id" ON "threatcategories" (tenant_id, "id");

-- revenueanomalycategories
ALTER TABLE "revenueanomalycategories" ADD COLUMN IF NOT EXISTS tenant_id BIGINT NOT NULL DEFAULT 1;
CREATE INDEX IF NOT EXISTS "ix_revenueanomalycategories_tenant_id" ON "revenueanomalycategories" (tenant_id, "id");

-- manifestsnapshots
ALTER TABLE "manifestsnapshots" ADD COLUMN IF NOT EXISTS tenant_id BIGINT NOT NULL DEFAULT 1;
CREATE INDEX IF NOT EXISTS "ix_manifestsnapshots_tenant_id" ON "manifestsnapshots" (tenant_id, "id");

-- matchqualityflags
ALTER TABLE "matchqualityflags" ADD COLUMN IF NOT EXISTS tenant_id BIGINT NOT NULL DEFAULT 1;
CREATE INDEX IF NOT EXISTS "ix_matchqualityflags_tenant_id" ON "matchqualityflags" (tenant_id, "id");

-- auditimagedecisions
ALTER TABLE "auditimagedecisions" ADD COLUMN IF NOT EXISTS tenant_id BIGINT NOT NULL DEFAULT 1;
CREATE INDEX IF NOT EXISTS "ix_auditimagedecisions_tenant_id" ON "auditimagedecisions" (tenant_id, "id");

COMMIT;

-- Verification (run manually after the transaction commits):
--   SELECT table_name FROM information_schema.columns
--   WHERE column_name = 'tenant_id'
--     AND table_name IN ('threatcategories','revenueanomalycategories',
--                        'manifestsnapshots','matchqualityflags','auditimagedecisions')
--   ORDER BY table_name;
-- Expected: 5 rows.

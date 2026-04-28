-- =============================================================================
-- Phase-1 follow-up: compound (tenant_id, hot_filter) indexes for queue tables
-- =============================================================================
-- Issue:
--   The 30/31 migrations created simple (tenant_id) indexes only on the
--   nickscan_icums and nickscan_downloads queue tables. Under RLS-filtered
--   range queries (status='Pending' / order by created_at desc) the planner
--   falls back to a bitmap scan + heap re-check rather than walking a covering
--   index. At low tenant counts that's invisible; under multi-tenant load it
--   becomes a stair-step latency cliff.
--
-- Fix:
--   Add (tenant_id, status) and (tenant_id, created_at DESC) compound indexes
--   on the high-churn queues. CONCURRENTLY so the build does not block writes
--   to the live tables; IF NOT EXISTS so re-running is a no-op.
--
-- Apply (using the migration-runner tool because psql is not installed
-- on the deploy box):
--     dotnet run --project tools\migration-runner -- 40-add-queue-compound-indexes.sql
--
-- Or with psql (if available):
--     psql -f 40-add-queue-compound-indexes.sql
--
-- Constraint: CREATE INDEX CONCURRENTLY cannot run inside a transaction block.
-- The migration-runner connects with autocommit; psql defaults to autocommit
-- on \c. If you wrap this file in BEGIN/COMMIT manually, the CONCURRENTLY
-- builds will fail with "CREATE INDEX CONCURRENTLY cannot run inside a
-- transaction block".
--
-- Rollback: DROP INDEX IF EXISTS ix_<name>;
-- =============================================================================

-- nickscan_downloads --------------------------------------------------------
\c nickscan_downloads

CREATE INDEX CONCURRENTLY IF NOT EXISTS ix_cmrredownloadqueue_tenant_status
    ON cmrredownloadqueue (tenant_id, status);

CREATE INDEX CONCURRENTLY IF NOT EXISTS ix_failedprocessingqueue_tenant_status
    ON failedprocessingqueue (tenant_id, status);

CREATE INDEX CONCURRENTLY IF NOT EXISTS ix_icumsdownloadqueue_tenant_status
    ON icumsdownloadqueue (tenant_id, status);

-- Postgres lowercases unquoted identifiers, so EF's "CreatedAt" property lands
-- on disk as `createdat`. The original audit suggested `created_at`; that is wrong
-- for this schema.
CREATE INDEX CONCURRENTLY IF NOT EXISTS ix_containerdownloadhistory_tenant_created
    ON containerdownloadhistory (tenant_id, createdat DESC);

CREATE INDEX CONCURRENTLY IF NOT EXISTS ix_ingestionlogs_tenant_created
    ON ingestionlogs (tenant_id, createdat DESC);

-- nickscan_icums ------------------------------------------------------------
\c nickscan_icums

CREATE INDEX CONCURRENTLY IF NOT EXISTS ix_icumbatchlogs_tenant_created
    ON icumbatchlogs (tenant_id, createdat DESC);

-- icumcontainerdata has no `manifest_id`; the BOE-key analog used in ICUMS
-- workflow lookups is `declarationnumber` (verified via information_schema).
CREATE INDEX CONCURRENTLY IF NOT EXISTS ix_icumcontainerdata_tenant_declaration
    ON icumcontainerdata (tenant_id, declarationnumber);

-- =============================================================================
-- Verification (run AFTER the indexes are built):
--
--   \c nickscan_downloads
--   EXPLAIN ANALYZE
--     SELECT * FROM icumsdownloadqueue
--     WHERE tenant_id = current_setting('app.tenant_id', true)::int
--       AND status = 'Pending'
--     LIMIT 50;
--
--   The plan should pick `ix_icumsdownloadqueue_tenant_status` as an Index
--   Scan, not a Bitmap Heap Scan + Recheck. If it still picks bitmap, run
--   ANALYZE on the table to refresh statistics:
--     ANALYZE icumsdownloadqueue;
-- =============================================================================

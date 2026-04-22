-- =====================================================================
-- Add ingestion-warnings columns to boedocuments (Postgres / live DB)
-- =====================================================================
-- Purpose: Surface ValidateCriticalFieldsAsync + ValidateIngestedDocumentAsync
-- outputs as queryable columns instead of log-only.
--
-- Idempotent: safe to re-run (IF NOT EXISTS guards everywhere).
--
-- Apply with:
--   psql -h localhost -U postgres -d nickscan_downloads -f add_ingestion_warnings_columns_pg.sql
-- =====================================================================

BEGIN;

ALTER TABLE boedocuments
  ADD COLUMN IF NOT EXISTS hasingestionwarnings boolean NOT NULL DEFAULT false;

ALTER TABLE boedocuments
  ADD COLUMN IF NOT EXISTS ingestionwarnings varchar(4000) NULL;

-- Filtered index so the "show me warning records" query stays fast
-- even as the table grows (most rows will have hasingestionwarnings=false).
CREATE INDEX IF NOT EXISTS "IX_BOEDocument_HasIngestionWarnings"
  ON boedocuments (hasingestionwarnings)
  WHERE hasingestionwarnings = true;

COMMIT;

-- Verify:
--   \d boedocuments
--   SELECT COUNT(*) FROM boedocuments WHERE hasingestionwarnings = true;
--   SELECT id, containernumber, clearancetype, ingestionwarnings
--   FROM boedocuments WHERE hasingestionwarnings = true LIMIT 10;

-- =============================================================================
-- drop-vestigial-icum-tables.sql
-- -----------------------------------------------------------------------------
-- Purpose:
--   Drop the two vestigial ICUMS mirror tables from nickscan_production that
--   have never been populated and are no longer referenced by any runtime
--   code path after commit c759f42d redirected IntakeWorker and
--   ImageAnalysisOrchestratorService to IcumDownloadsDbContext.BOEDocuments
--   (nickscan_downloads) as the canonical ICUMS BOE source.
--
-- Targets (both confirmed empty as of 2026-04-06):
--   nickscan_production.icummanifestitems  (0 rows, FK to icumcontainerdata)
--   nickscan_production.icumcontainerdata  (0 rows)
--
-- Why hand-written instead of EF migration:
--   The ApplicationDbContextModelSnapshot has drifted from the live schema
--   (wave processing was deployed directly without updating the snapshot).
--   Any `dotnet ef migrations add` call tries to reconcile that drift and
--   would entangle wave-processing CreateTable operations with this simple
--   drop. This script is applied out-of-band; a separate snapshot
--   reconciliation task is tracked as follow-up.
--
-- Related files (still reference IcumContainerData / IcumManifestItem via
-- IcumDbContext, which binds to nickscan_icums and is the correct home):
--   - src/NickScanCentralImagingPortal.Infrastructure/Data/IcumDbContext.cs
--   - src/NickScanCentralImagingPortal.Infrastructure/Repositories/IcumRepository.cs
--   - src/NickScanCentralImagingPortal.Services/Dashboard/ComprehensiveDashboardService.cs
--
-- How to run:
--   psql -h localhost -U postgres -d nickscan_production \
--        -f tools/backfill/drop-vestigial-icum-tables.sql
--
-- The script is wrapped in a transaction with ROLLBACK by default for safety.
-- To apply, comment out ROLLBACK and uncomment COMMIT at the bottom.
-- =============================================================================

\set ON_ERROR_STOP on
\timing on

BEGIN;

\echo
\echo === PRE-CHECK: confirm both tables are empty ===

SELECT 'icummanifestitems' AS tbl, COUNT(*) AS row_count FROM icummanifestitems
UNION ALL
SELECT 'icumcontainerdata', COUNT(*) FROM icumcontainerdata;

-- Verify no other tables reference these via foreign key
-- (self-test: should return only the intra-group FK on icummanifestitems)
\echo
\echo === FK check: who references icumcontainerdata or icummanifestitems? ===

SELECT conrelid::regclass AS source_table,
       conname,
       pg_get_constraintdef(oid) AS definition
FROM pg_constraint
WHERE contype = 'f'
  AND (confrelid::regclass::text IN ('icumcontainerdata', 'icummanifestitems')
       OR conrelid::regclass::text IN ('icumcontainerdata', 'icummanifestitems'));

-- -----------------------------------------------------------------------------
-- Drop order: icummanifestitems first (it has an FK to icumcontainerdata),
-- then icumcontainerdata.
-- -----------------------------------------------------------------------------
\echo
\echo === DROP TABLES ===

DROP TABLE IF EXISTS icummanifestitems CASCADE;
DROP TABLE IF EXISTS icumcontainerdata CASCADE;

\echo
\echo === POST-CHECK: confirm tables are gone ===

SELECT table_name
FROM information_schema.tables
WHERE table_schema = 'public'
  AND table_name IN ('icumcontainerdata', 'icummanifestitems');
-- Expected: 0 rows

\echo
\echo === POST-CHECK: no dangling FKs ===

SELECT conrelid::regclass AS source_table,
       conname
FROM pg_constraint
WHERE contype = 'f'
  AND (confrelid::regclass::text IN ('icumcontainerdata', 'icummanifestitems')
       OR conrelid::regclass::text IN ('icumcontainerdata', 'icummanifestitems'));
-- Expected: 0 rows

-- -----------------------------------------------------------------------------
-- Default: ROLLBACK. Uncomment COMMIT to apply.
-- -----------------------------------------------------------------------------

ROLLBACK;
-- COMMIT;

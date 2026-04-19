-- ============================================================================
-- backfill-multi-container-relations.sql
-- ----------------------------------------------------------------------------
-- Purpose: Repair the 244 "multi-container" rows in
--          nickscan_production.containerboerelations where containernumber is
--          a comma-joined string (e.g. "MSMU2238000, MSMU1593191") and
--          icumsboeid = 0.
--
-- Root cause:
--   The upstream ASE scanner database stores one "inspection" row per scan
--   image/truck. When a single truck carries two containers, the ASE
--   `icf.FieldValue` returns a comma-joined string for that single inspection.
--   AseDatabaseSyncService persists that verbatim into asescans.containernumber,
--   and later ContainerDataMapperService stores the same verbatim string into
--   containerboerelations.containernumber. The mapper then tries to look up
--   a BOE document matching the full merged string, which never succeeds
--   (BOE docs are keyed on a single container number), so icumsboeid stays 0.
--
-- Investigation findings (2026-04-06):
--   - 244 rows total, every single one contains exactly 2 containers (never 3+)
--   - All rows are scannertype='ASE', relationtype='Primary'
--   - 100% of the individual container numbers have a matching
--     boedocuments row in nickscan_downloads with processingstatus='Transferred'
--   - 28 pairs share the same declarationnumber (same BOE -> genuine 2-container
--     consolidated cargo under one declaration)
--   - 216 pairs have different declarationnumbers (two unrelated BOEs that
--     happened to be scanned in one ASE inspection)
--   - 2 separate rows have containernumber='Unknown' and no way to correlate
--     back to a specific asescans row (ScannerDataId is Guid.GetHashCode()
--     which is not reproducible in PostgreSQL). These are LEFT ALONE.
--
-- Strategy: SPLIT ROWS (Strategy A)
--   For each of the 244 multi-container rows:
--     1. Resolve c1 -> boedoc1.id and c2 -> boedoc2.id via containernumber
--        against the latest Transferred boedocuments row
--     2. INSERT two new rows, one per individual container, each with its own
--        icumsboeid. Preserve the original scannerdataid on both children so
--        the ASE scan image still links to both container records
--     3. DELETE the original merged row
--
--   This preserves the full scan image linkage (both child rows point at the
--   same asescans entry via scannerdataid) while giving each container its
--   own properly-resolved BOE relation. Both "consolidated" and "separate"
--   sub-cases are handled identically — each container gets its correct BOE.
--
-- Why not Strategy B (resolve first container only)?
--   For 216 of 244 rows the two containers belong to DIFFERENT BOEs. Writing
--   just the first BOE would leave the second container's BOE permanently
--   orphaned and would silently misattribute the scan to only one of them.
--
-- Notes:
--   - Uses dblink (already installed) to reach nickscan_downloads.boedocuments
--   - Wrapped in BEGIN / ROLLBACK. Change ROLLBACK to COMMIT after review
--   - If you want to see exactly what would change, run as-is: the SELECTs
--     at the bottom print pre- and post-state inside the transaction before
--     ROLLBACK discards it
--   - The 2 'Unknown' rows are NOT touched by this script
--   - The NICKSCAN_DB_PASSWORD env var must be set in the shell. Invoke with:
--       psql -h localhost -U postgres -d nickscan_production \
--         -v pw="$NICKSCAN_DB_PASSWORD" \
--         -f backfill-multi-container-relations.sql
-- ============================================================================

\set ON_ERROR_STOP on
\timing on

BEGIN;

-- ----------------------------------------------------------------------------
-- 1. Load transferred BOE documents from nickscan_downloads into a temp table
-- ----------------------------------------------------------------------------
CREATE TEMP TABLE _boedocs ON COMMIT DROP AS
SELECT * FROM dblink(
  'host=localhost port=5432 dbname=nickscan_downloads user=postgres password=' || quote_literal(:'pw'),
  'SELECT containernumber::text, id::integer, declarationnumber::text
     FROM boedocuments
    WHERE processingstatus = ''Transferred'''
) AS t(containernumber TEXT, id INTEGER, declarationnumber TEXT);

CREATE INDEX ON _boedocs(containernumber);

-- ----------------------------------------------------------------------------
-- 2. Pre-check: source rows we are going to rewrite
-- ----------------------------------------------------------------------------
CREATE TEMP TABLE _source ON COMMIT DROP AS
SELECT
  r.id                                           AS source_id,
  r.containernumber                              AS merged,
  trim(split_part(r.containernumber, ',', 1))    AS c1,
  trim(split_part(r.containernumber, ',', 2))    AS c2,
  r.scannerdataid,
  r.scannertype,
  r.relationtype,
  r.createdat,
  r.notes,
  r.tenant_id
FROM containerboerelations r
WHERE r.icumsboeid = 0
  AND r.containernumber LIKE '%,%'
  AND r.containernumber NOT LIKE '%,%,%';  -- safety: refuse 3+ container strings

-- Resolve both containers against boedocs (latest matching row wins)
CREATE TEMP TABLE _resolved ON COMMIT DROP AS
SELECT
  s.*,
  (SELECT id FROM _boedocs WHERE containernumber = s.c1 ORDER BY id DESC LIMIT 1) AS boe1,
  (SELECT id FROM _boedocs WHERE containernumber = s.c2 ORDER BY id DESC LIMIT 1) AS boe2,
  (SELECT declarationnumber FROM _boedocs WHERE containernumber = s.c1 ORDER BY id DESC LIMIT 1) AS decl1,
  (SELECT declarationnumber FROM _boedocs WHERE containernumber = s.c2 ORDER BY id DESC LIMIT 1) AS decl2
FROM _source s;

\echo '--- PRE-CHECK: source row counts ---'
SELECT COUNT(*) AS source_rows FROM _source;

\echo '--- PRE-CHECK: resolution coverage ---'
SELECT
  COUNT(*)                                                       AS total,
  COUNT(*) FILTER (WHERE boe1 IS NOT NULL AND boe2 IS NOT NULL)  AS both_resolved,
  COUNT(*) FILTER (WHERE boe1 IS NULL OR  boe2 IS NULL)          AS unresolved,
  COUNT(*) FILTER (WHERE boe1 IS NOT NULL AND boe2 IS NOT NULL
                     AND decl1 = decl2 AND decl1 <> '')          AS same_declaration_pairs,
  COUNT(*) FILTER (WHERE boe1 IS NOT NULL AND boe2 IS NOT NULL
                     AND (decl1 <> decl2 OR decl1 = '' OR decl1 IS NULL))
                                                                 AS different_declaration_pairs
FROM _resolved;

\echo '--- PRE-CHECK: unresolved rows (should be 0) ---'
SELECT source_id, merged, c1, c2, boe1, boe2
  FROM _resolved
 WHERE boe1 IS NULL OR boe2 IS NULL;

-- ----------------------------------------------------------------------------
-- 3. INSERT child rows (one per individual container) for every fully
--    resolved source row. Children inherit scannerdataid/type/createdat/tenant
--    so the ASE scan image still resolves to both containers.
-- ----------------------------------------------------------------------------
WITH ins AS (
  INSERT INTO containerboerelations (
      containernumber, scannerdataid, scannertype, icumsboeid,
      relationtype, createdat, notes, isactive, tenant_id
  )
  SELECT c1, scannerdataid, scannertype, boe1,
         relationtype, createdat,
         COALESCE(notes, '') ||
           ' [split from merged row id=' || source_id || ', pair=' || c2 || ']',
         TRUE, tenant_id
    FROM _resolved
   WHERE boe1 IS NOT NULL AND boe2 IS NOT NULL
  UNION ALL
  SELECT c2, scannerdataid, scannertype, boe2,
         relationtype, createdat,
         COALESCE(notes, '') ||
           ' [split from merged row id=' || source_id || ', pair=' || c1 || ']',
         TRUE, tenant_id
    FROM _resolved
   WHERE boe1 IS NOT NULL AND boe2 IS NOT NULL
  RETURNING id
)
SELECT COUNT(*) AS inserted_children FROM ins
\gset

\echo '--- inserted_children (should be 2 * number of resolved source rows) ---'
SELECT :inserted_children AS inserted_children;

-- ----------------------------------------------------------------------------
-- 4. DELETE the original merged rows that were successfully split
-- ----------------------------------------------------------------------------
WITH del AS (
  DELETE FROM containerboerelations r
   USING _resolved rs
   WHERE r.id = rs.source_id
     AND rs.boe1 IS NOT NULL
     AND rs.boe2 IS NOT NULL
  RETURNING r.id
)
SELECT COUNT(*) AS deleted_merged FROM del
\gset

\echo '--- deleted_merged (should equal source_rows when fully resolved) ---'
SELECT :deleted_merged AS deleted_merged;

-- ----------------------------------------------------------------------------
-- 5. Post-check: confirm no multi-container rows remain
-- ----------------------------------------------------------------------------
\echo '--- POST-CHECK: remaining comma-joined rows (should be 0) ---'
SELECT COUNT(*) AS remaining_multi
  FROM containerboerelations
 WHERE containernumber LIKE '%,%';

\echo '--- POST-CHECK: remaining icumsboeid=0 rows (should drop by 244) ---'
SELECT COUNT(*) AS remaining_zero_boe
  FROM containerboerelations
 WHERE icumsboeid = 0;

\echo '--- POST-CHECK: sample of newly inserted child rows ---'
SELECT id, containernumber, scannerdataid, scannertype, icumsboeid, relationtype, notes
  FROM containerboerelations
 WHERE notes LIKE '%[split from merged row id=%'
 ORDER BY id DESC
 LIMIT 10;

-- ----------------------------------------------------------------------------
-- ROLLBACK by default. Review the pre/post numbers above. If correct,
-- change ROLLBACK below to COMMIT and re-run.
-- ----------------------------------------------------------------------------
ROLLBACK;
-- COMMIT;

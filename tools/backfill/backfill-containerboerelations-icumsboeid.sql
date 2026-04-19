-- =============================================================================
-- Backfill: containerboerelations.icumsboeid
-- =============================================================================
-- CONTEXT
--   ContainerBOERelation.ICUMSBOEId is a foreign key to
--   nickscan_downloads.boedocuments.id (despite the name - confirmed via
--   ContainerDataMapperService.cs:287-311).
--
--   As of 2026-04-06 all 2,697 rows in nickscan_production.containerboerelations
--   have icumsboeid = 0 because ContainerDataMapperService.GetPendingMappingsAsync
--   hard-coded the placeholder and never resolved it. Code fix committed as
--   c759f42d; this script repairs the historical rows.
--
--   Match rule: containerboerelations.containernumber = boedocuments.containernumber
--               tie-broken by boedocuments.createdat DESC (latest wins)
--   Source:     nickscan_downloads.boedocuments WHERE processingstatus = 'Transferred'
--
-- DRY-RUN RESULTS (2026-04-06)
--   Total relations  : 2,697
--   Broken (= 0)     : 2,697
--   Matchable        : 2,451   (will be repaired)
--   Unmatchable      :   246
--     - Multi-container comma strings : 244 (cannot be auto-repaired)
--     - Literal 'Unknown' containers  :   2 (ASE scans with no container)
--     - Genuinely missing BOE docs    :   0
--
-- HOW TO RUN (after review)
--   1. Back up the table first:
--        pg_dump -h localhost -U postgres -d nickscan_production \
--                -t containerboerelations \
--                > containerboerelations_backup_YYYYMMDD.sql
--
--   2. Execute this script:
--        psql -h localhost -U postgres -d nickscan_production \
--             -f backfill-containerboerelations-icumsboeid.sql
--
--   3. Review the dry-run output. The script ends with ROLLBACK; by default.
--      To actually apply changes, edit the final line: comment out ROLLBACK;
--      and uncomment COMMIT;
-- =============================================================================

\set ON_ERROR_STOP on
\timing on

-- Requires dblink (already installed in nickscan_production as of 2026-04-06).
CREATE EXTENSION IF NOT EXISTS dblink;

BEGIN;

-- -----------------------------------------------------------------------------
-- Step 1: Build resolved (containernumber -> boedocument id) mapping from
--         the downloads database. DISTINCT ON + ORDER BY createdat DESC
--         picks the most recently created BOE doc per container.
-- -----------------------------------------------------------------------------
--
-- IMPORTANT: replace <PASSWORD> below with the postgres password before running,
-- OR pre-set it via a .pgpass entry / foreign server so it does not live in
-- this file. Do NOT commit a real password to git.
--
CREATE TEMP TABLE boe_resolved AS
SELECT DISTINCT ON (containernumber) containernumber, id
FROM dblink(
  'host=localhost port=5432 dbname=nickscan_downloads user=postgres password=<PASSWORD>',
  'SELECT containernumber, id, createdat
     FROM boedocuments
    WHERE processingstatus = ''Transferred''
    ORDER BY containernumber, createdat DESC'
) AS t(containernumber TEXT, id INTEGER, createdat TIMESTAMP);

CREATE INDEX ON boe_resolved (containernumber);

\echo
\echo === PRE-UPDATE VERIFICATION ===

SELECT COUNT(*) AS boe_resolved_rows FROM boe_resolved;

SELECT COUNT(*) AS total_relations,
       COUNT(*) FILTER (WHERE icumsboeid = 0) AS broken_before,
       COUNT(*) FILTER (WHERE icumsboeid > 0) AS already_ok_before
FROM containerboerelations;

SELECT COUNT(*) AS matchable
FROM containerboerelations cb
INNER JOIN boe_resolved br ON br.containernumber = cb.containernumber
WHERE cb.icumsboeid = 0;

SELECT COUNT(*) AS unmatchable_multi_container
FROM containerboerelations cb
LEFT JOIN boe_resolved br ON br.containernumber = cb.containernumber
WHERE cb.icumsboeid = 0
  AND br.id IS NULL
  AND cb.containernumber LIKE '%,%';

SELECT COUNT(*) AS unmatchable_other
FROM containerboerelations cb
LEFT JOIN boe_resolved br ON br.containernumber = cb.containernumber
WHERE cb.icumsboeid = 0
  AND br.id IS NULL
  AND cb.containernumber NOT LIKE '%,%';

-- -----------------------------------------------------------------------------
-- Step 2: Perform the UPDATE. Only touches rows where a match exists.
--         Multi-container strings and 'Unknown' rows are left alone.
-- -----------------------------------------------------------------------------
\echo
\echo === APPLYING UPDATE ===

UPDATE containerboerelations cb
   SET icumsboeid = br.id
  FROM boe_resolved br
 WHERE cb.containernumber = br.containernumber
   AND cb.icumsboeid = 0;

\echo
\echo === POST-UPDATE VERIFICATION ===

SELECT COUNT(*) AS total_relations,
       COUNT(*) FILTER (WHERE icumsboeid = 0) AS still_broken,
       COUNT(*) FILTER (WHERE icumsboeid > 0) AS repaired
FROM containerboerelations;

-- Spot-check: the three containers for declaration 70326209501
-- Expected after repair: icumsboeid set to 32958, 33045, 33211
SELECT cb.id, cb.containernumber, cb.icumsboeid
FROM containerboerelations cb
WHERE cb.containernumber IN ('HPCU4742359', 'GAOU7497630', 'TIIU4058685')
ORDER BY cb.containernumber;

-- Sample 10 repaired rows
SELECT id, containernumber, icumsboeid
FROM containerboerelations
WHERE icumsboeid > 0
ORDER BY id
LIMIT 10;

-- Remaining broken rows (should all be multi-container strings or 'Unknown')
SELECT id, containernumber
FROM containerboerelations
WHERE icumsboeid = 0
ORDER BY id
LIMIT 20;

-- -----------------------------------------------------------------------------
-- Step 3: Finalise. Script is safe by default: rolls back unless you edit
--         these two lines.
-- -----------------------------------------------------------------------------

ROLLBACK;
-- COMMIT;

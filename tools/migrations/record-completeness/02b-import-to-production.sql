-- ─────────────────────────────────────────────────────────────────────────────
-- 1.14.0 — Record Completeness backfill (Phase 2: import into nickscan_production)
--
-- Reads the CSV produced by 02a-export-from-icums.sql and builds
-- RecordCompletenessStatus + RecordExpectedContainer rows from it. Preserves any
-- existing state the container-level pipeline has already captured by checking
-- ContainerCompletenessStatus and flipping AwaitingScan → Pending / Ready
-- accordingly.
--
-- Idempotent: re-running is safe via ON CONFLICT DO NOTHING on the unique keys.
-- ─────────────────────────────────────────────────────────────────────────────

\timing on

BEGIN;

-- Load the CSV into a temp table
CREATE TEMP TABLE icums_import (
    declarationnumber   text,
    containernumber     text,
    clearancetype       text,
    regimecode          text,
    primary_boe_id      int,
    rotationnumber      text,
    blnumber            text,
    housebl             text,
    consigneename       text
);

\copy icums_import FROM 'C:/Users/Administrator/AppData/Local/Temp/record_backfill_icums.csv' CSV HEADER

SELECT 'loaded from csv' AS phase, COUNT(*) AS rows, COUNT(DISTINCT declarationnumber) AS distinct_declarations FROM icums_import;

-- Detect Pattern A: containers that appear on multiple declarations
CREATE TEMP TABLE pattern_a_containers AS
SELECT containernumber
FROM icums_import
GROUP BY containernumber
HAVING COUNT(DISTINCT declarationnumber) > 1;

SELECT 'pattern A containers' AS phase, COUNT(*) AS rows FROM pattern_a_containers;

-- ── Phase 2a: Insert RecordCompletenessStatus rows, one per declaration ──
INSERT INTO recordcompletenessstatuses (
    declarationnumber, clearancetype, regimecode, primaryboedocumentid,
    rotationnumber, blnumber, containergroupkey, scannertype,
    totalexpectedcontainers, containersawaitingscan, containersscanned, containersready,
    containersdecided, containerssubmitted, containersnoimage, containersnoscan,
    status, workflowstage,
    firstseenutc, lastnewcontaineratutc,
    declarationsjson,
    createdatutc, updatedatutc, tenant_id
)
SELECT
    decl.declarationnumber,
    MAX(decl.clearancetype) AS clearancetype,
    MAX(decl.regimecode)    AS regimecode,
    MAX(decl.primary_boe_id) AS primaryboedocumentid,
    MAX(decl.rotationnumber) AS rotationnumber,
    MAX(decl.blnumber)       AS blnumber,
    -- Pattern A container group key: set when this declaration has exactly 1 container
    -- AND that container appears in other declarations
    CASE
        WHEN COUNT(*) = 1
             AND MIN(decl.containernumber) IN (SELECT containernumber FROM pattern_a_containers)
        THEN MIN(decl.containernumber)
        ELSE NULL
    END AS containergroupkey,
    NULL AS scannertype,
    COUNT(*) AS totalexpectedcontainers,
    COUNT(*) AS containersawaitingscan,  -- initial state: all awaiting
    0, 0, 0, 0, 0, 0,                    -- all other buckets start at 0
    'Pending', 'Pending',
    now(), now(),
    NULL,  -- declarationsjson will be backfilled in a follow-up update
    now(), now(), 1
FROM icums_import decl
GROUP BY decl.declarationnumber
ON CONFLICT (declarationnumber, COALESCE(scannertype, '')) DO NOTHING;

SELECT 'recordcompletenessstatuses after insert' AS phase, COUNT(*) AS rows FROM recordcompletenessstatuses;

-- ── Phase 2b: Insert RecordExpectedContainer rows, one per (declaration, container) ──
INSERT INTO recordexpectedcontainers (
    recordid, containernumber, status, boedocumentid, housebl, consigneename,
    firstseenutc, tenant_id
)
SELECT
    r.id,
    ii.containernumber,
    'AwaitingScan',
    ii.primary_boe_id,
    ii.housebl,
    ii.consigneename,
    now(),
    1
FROM icums_import ii
JOIN recordcompletenessstatuses r ON r.declarationnumber = ii.declarationnumber
ON CONFLICT (recordid, containernumber) DO NOTHING;

SELECT 'recordexpectedcontainers after insert' AS phase, COUNT(*) AS rows FROM recordexpectedcontainers;

-- ── Phase 2c: Reconcile initial state against existing ContainerCompletenessStatus ──
-- For expected containers that ALREADY have container-level state, promote them:
--   has images        → Ready
--   scanned no images → Pending
UPDATE recordexpectedcontainers rec
SET
    status = CASE WHEN ccs.hasimagedata THEN 'Ready' ELSE 'Pending' END,
    scannedatutc = now(),
    becamereadyutc = CASE WHEN ccs.hasimagedata THEN now() ELSE NULL END,
    inspectionid = ccs.inspectionid,
    scannertype = ccs.scannertype
FROM containercompletenessstatuses ccs
WHERE rec.containernumber = ccs.containernumber
  AND rec.status = 'AwaitingScan';

SELECT 'expected containers promoted from existing completeness state' AS phase,
       status, COUNT(*) AS rows
FROM recordexpectedcontainers
GROUP BY status
ORDER BY status;

-- ── Phase 2d: Recompute rollup counts on all records based on child state ──
UPDATE recordcompletenessstatuses r
SET
    totalexpectedcontainers = rollups.total,
    containersawaitingscan  = rollups.awaiting,
    containersscanned       = rollups.scanned,
    containersready         = rollups.ready,
    containersdecided       = rollups.decided,
    containerssubmitted     = rollups.submitted,
    containersnoimage       = rollups.noimage,
    containersnoscan        = rollups.noscan,
    status = CASE
        WHEN rollups.submitted = rollups.total AND rollups.total > 0 THEN 'Completed'
        WHEN rollups.ready > 0 AND rollups.awaiting = 0 AND rollups.scanned = 0 THEN 'Ready'
        WHEN rollups.ready > 0 OR rollups.scanned > 0 THEN 'PartiallyReady'
        ELSE 'Pending'
    END,
    workflowstage = CASE
        WHEN rollups.ready > 0 THEN 'ImageAnalysis'
        ELSE 'Pending'
    END,
    firstreadyatutc = CASE WHEN rollups.ready > 0 THEN now() ELSE NULL END,
    updatedatutc = now(),
    lastcheckedatutc = now()
FROM (
    SELECT
        recordid,
        COUNT(*) AS total,
        COUNT(*) FILTER (WHERE status = 'AwaitingScan') AS awaiting,
        COUNT(*) FILTER (WHERE status = 'Pending')     AS scanned,
        COUNT(*) FILTER (WHERE status = 'Ready')       AS ready,
        COUNT(*) FILTER (WHERE status = 'Decided')     AS decided,
        COUNT(*) FILTER (WHERE status = 'Submitted')   AS submitted,
        COUNT(*) FILTER (WHERE status = 'NoImageAvailable') AS noimage,
        COUNT(*) FILTER (WHERE status = 'NoScanReceived')  AS noscan
    FROM recordexpectedcontainers
    GROUP BY recordid
) rollups
WHERE r.id = rollups.recordid;

-- ── Verification summary ──
SELECT 'final' AS phase, status, COUNT(*) AS records, SUM(totalexpectedcontainers) AS expected, SUM(containersready) AS ready, SUM(containersawaitingscan) AS awaiting
FROM recordcompletenessstatuses
GROUP BY status
ORDER BY records DESC;

SELECT 'records total' AS metric, COUNT(*) AS value FROM recordcompletenessstatuses
UNION ALL SELECT 'expected containers total', COUNT(*) FROM recordexpectedcontainers
UNION ALL SELECT 'multi-container records', COUNT(*) FROM recordcompletenessstatuses WHERE totalexpectedcontainers > 1
UNION ALL SELECT 'pattern A records', COUNT(*) FROM recordcompletenessstatuses WHERE containergroupkey IS NOT NULL;

COMMIT;

-- Smoking-gun verification: the gypsum declaration from the earlier investigation
SELECT r.declarationnumber, r.totalexpectedcontainers, r.containersawaitingscan,
       r.containersready, r.containersscanned, r.status
FROM recordcompletenessstatuses r
WHERE r.declarationnumber = '40126052701';

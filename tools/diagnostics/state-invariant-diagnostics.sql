-- Repo-wide state invariant diagnostics.
--
-- Run against the application PostgreSQL database before and after deploying
-- the state ownership fixes. Every query returns rows that deserve triage.

\echo '## CCS marked Complete without required evidence'
SELECT id, containernumber, scannertype, inspectionid, status, workflowstage,
       hasscannerdata, hasicumsdata, hasimagedata, clearancetype, groupidentifier,
       updatedat
FROM containercompletenessstatuses
WHERE status = 'Complete'
  AND (
      COALESCE(hasscannerdata, false) = false
      OR COALESCE(hasicumsdata, false) = false
      OR COALESCE(hasimagedata, false) = false
      OR (clearancetype = 'CMR' AND NULLIF(BTRIM(groupidentifier), '') IS NULL)
  )
ORDER BY updatedat DESC NULLS LAST
LIMIT 200;

\echo '## CCS advanced to ImageAnalysis while still awaiting CMR declaration'
SELECT id, containernumber, scannertype, inspectionid, status, workflowstage,
       clearancetype, groupidentifier, updatedat
FROM containercompletenessstatuses
WHERE workflowstage = 'ImageAnalysis'
  AND clearancetype = 'CMR'
  AND NULLIF(BTRIM(groupidentifier), '') IS NULL
ORDER BY updatedat DESC NULLS LAST
LIMIT 200;

\echo '## Duplicate CCS rows under service identity'
SELECT containernumber, scannertype, COALESCE(inspectionid, '') AS inspection_identity,
       COUNT(*) AS row_count, MIN(id) AS first_id, MAX(id) AS last_id
FROM containercompletenessstatuses
GROUP BY containernumber, scannertype, COALESCE(inspectionid, '')
HAVING COUNT(*) > 1
ORDER BY row_count DESC, containernumber, scannertype, inspection_identity
LIMIT 200;

\echo '## ICUMS submission rows with unknown statuses'
SELECT status, COUNT(*) AS row_count
FROM icumssubmissionqueues
WHERE status NOT IN ('Pending', 'Processing', 'Submitted', 'Failed', 'Cancelled')
GROUP BY status
ORDER BY row_count DESC, status;

\echo '## Manual ICUMS retries stranded outside worker-consumed statuses'
SELECT id, containernumber, scannertype, status, retrycount, nextretryat, updatedat
FROM icumssubmissionqueues
WHERE status NOT IN ('Pending', 'Failed')
  AND retrycount > 0
  AND completedat IS NULL
ORDER BY updatedat DESC NULLS LAST
LIMIT 200;

\echo '## Container scan queues with retry count beyond max'
SELECT id, containernumber, scannertype, inspectionid, status, retrycount, maxretries,
       processedat, completedat, updatedat
FROM containerscanqueues
WHERE retrycount > maxretries
ORDER BY updatedat DESC NULLS LAST
LIMIT 200;

\echo '## ICUMS download queues with retry count beyond max'
\connect nickscan_downloads
SELECT id, containernumber, status, retrycount, maxretries,
       firstattemptat, lastattemptat, completedat
FROM icumsdownloadqueue
WHERE retrycount > maxretries
ORDER BY lastattemptat DESC NULLS LAST
LIMIT 200;

\echo '## Processing queue rows that look stuck'
\connect nickscan_production
SELECT 'ContainerScanQueue' AS queue_name, id, containernumber, status, processedat AS last_attempt_at
FROM containerscanqueues
WHERE status = 'Processing'
  AND processedat < (NOW() AT TIME ZONE 'UTC') - INTERVAL '30 minutes'
UNION ALL
SELECT 'ICUMSDownloadQueue' AS queue_name, id, containernumber, status, lastattemptat AS last_attempt_at
FROM nickscan_downloads.public.icumsdownloadqueue
WHERE status = 'Processing'
  AND lastattemptat < (NOW() AT TIME ZONE 'UTC') - INTERVAL '30 minutes'
ORDER BY last_attempt_at;

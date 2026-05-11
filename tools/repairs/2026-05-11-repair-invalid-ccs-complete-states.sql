-- Repair historical ContainerCompletenessStatus rows that were marked Complete
-- before the repo-wide completeness policy was enforced.
--
-- Run against: nickscan_production
--
-- Categories repaired:
--   1. CMR rows without a declaration/group -> AwaitingDeclaration/Pending.
--   2. Export-hold rows without ICUMS data -> Export-Pending/Export-Hold.
--   3. Other Complete rows missing required evidence -> Missing/Pending.

BEGIN;

CREATE TABLE IF NOT EXISTS repair_20260511_invalid_ccs_complete AS
SELECT *
FROM containercompletenessstatuses
WHERE false;

INSERT INTO repair_20260511_invalid_ccs_complete
SELECT c.*
FROM containercompletenessstatuses c
WHERE c.status = 'Complete'
  AND (
      COALESCE(c.hasscannerdata, false) = false
      OR COALESCE(c.hasicumsdata, false) = false
      OR COALESCE(c.hasimagedata, false) = false
      OR (c.clearancetype = 'CMR' AND NULLIF(BTRIM(c.groupidentifier), '') IS NULL)
  )
  AND NOT EXISTS (
      SELECT 1
      FROM repair_20260511_invalid_ccs_complete r
      WHERE r.id = c.id
  );

UPDATE containercompletenessstatuses
SET status = 'AwaitingDeclaration',
    workflowstage = 'Pending',
    lastcheckedat = NULL,
    updatedat = NOW() AT TIME ZONE 'UTC',
    errormessage = 'Data repair 2026-05-11: CMR row without declaration reset from invalid Complete/ImageAnalysis state.'
WHERE status = 'Complete'
  AND clearancetype = 'CMR'
  AND NULLIF(BTRIM(groupidentifier), '') IS NULL;

UPDATE containercompletenessstatuses
SET status = 'Export-Pending',
    workflowstage = 'Export-Hold',
    lastcheckedat = NULL,
    updatedat = NOW() AT TIME ZONE 'UTC',
    errormessage = 'Data repair 2026-05-11: export-hold row reset from invalid Complete status.'
WHERE status = 'Complete'
  AND workflowstage = 'Export-Hold'
  AND COALESCE(hasicumsdata, false) = false;

UPDATE containercompletenessstatuses
SET status = 'Missing',
    workflowstage = 'Pending',
    lastcheckedat = NULL,
    updatedat = NOW() AT TIME ZONE 'UTC',
    errormessage = 'Data repair 2026-05-11: row reset from invalid Complete state because required evidence is missing.'
WHERE status = 'Complete'
  AND (
      COALESCE(hasscannerdata, false) = false
      OR COALESCE(hasicumsdata, false) = false
      OR COALESCE(hasimagedata, false) = false
  );

SELECT 'remaining_invalid_complete' AS check_name, COUNT(*) AS rows
FROM containercompletenessstatuses
WHERE status = 'Complete'
  AND (
      COALESCE(hasscannerdata, false) = false
      OR COALESCE(hasicumsdata, false) = false
      OR COALESCE(hasimagedata, false) = false
      OR (clearancetype = 'CMR' AND NULLIF(BTRIM(groupidentifier), '') IS NULL)
  );

SELECT 'remaining_cmr_imageanalysis' AS check_name, COUNT(*) AS rows
FROM containercompletenessstatuses
WHERE workflowstage = 'ImageAnalysis'
  AND clearancetype = 'CMR'
  AND NULLIF(BTRIM(groupidentifier), '') IS NULL;

COMMIT;

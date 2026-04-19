-- Split Integration Phase 5: Backfill split fields on existing AnalysisRecords
-- for containers that are part of 2-container scans.
-- Idempotent — re-runs are no-ops (only updates rows where ismulticontainerscan = false).

WITH multi_container_scans AS (
    SELECT
        osr.id AS scan_id,
        osr.originalcontainernumbers,
        osr.derivedrecordcount,
        trim(split_part(osr.originalcontainernumbers, ',', 1)) AS container_1,
        trim(split_part(osr.originalcontainernumbers, ',', 2)) AS container_2
    FROM originalscanrecords osr
    WHERE osr.derivedrecordcount = 2
      AND osr.originalcontainernumbers LIKE '%,%'
)
UPDATE analysisrecords ar
SET
    ismulticontainerscan = true,
    splitposition = CASE
        WHEN ar.containernumber = mcs.container_1 THEN 'left'
        WHEN ar.containernumber = mcs.container_2 THEN 'right'
        ELSE 'left'
    END
FROM multi_container_scans mcs
WHERE (ar.containernumber = mcs.container_1 OR ar.containernumber = mcs.container_2)
  AND ar.ismulticontainerscan = false;

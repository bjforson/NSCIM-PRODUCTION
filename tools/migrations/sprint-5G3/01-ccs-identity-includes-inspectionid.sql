-- Sprint 5G3: align ContainerCompletenessStatus database identity with service identity.
--
-- Context:
--   ContainerCompletenessService dedupes CCS rows by
--   (ContainerNumber, ScannerType, InspectionId). The earlier Sprint 5G1 unique
--   index used only (containernumber, scannertype), which prevents repeated scans
--   of the same container on the same scanner from being tracked separately.
--
-- PostgreSQL note:
--   A normal UNIQUE index permits multiple NULL inspectionid values. The service
--   treats NULL inspection IDs as the same legacy identity, so this uses
--   COALESCE(inspectionid, '') in the unique expression.

BEGIN;

\echo '## PRECHECK: duplicates under desired CCS identity'
SELECT containernumber, scannertype, COALESCE(inspectionid, '') AS inspection_identity, COUNT(*) AS rows
FROM containercompletenessstatuses
GROUP BY containernumber, scannertype, COALESCE(inspectionid, '')
HAVING COUNT(*) > 1
ORDER BY rows DESC, containernumber, scannertype, inspection_identity
LIMIT 50;

DO $$
DECLARE
    n integer;
BEGIN
    SELECT count(*) INTO n FROM (
        SELECT containernumber, scannertype, COALESCE(inspectionid, '') AS inspection_identity
        FROM containercompletenessstatuses
        GROUP BY containernumber, scannertype, COALESCE(inspectionid, '')
        HAVING count(*) > 1
    ) sub;

    IF n > 0 THEN
        RAISE EXCEPTION 'CCS duplicates remain under (containernumber, scannertype, inspectionid): % groups. Resolve duplicates before applying unique index.', n;
    END IF;
END $$;

DROP INDEX IF EXISTS ix_ccs_containernumber_scannertype_unique;

CREATE UNIQUE INDEX IF NOT EXISTS ix_ccs_container_scanner_inspection_unique
    ON containercompletenessstatuses(containernumber, scannertype, COALESCE(inspectionid, ''));

CREATE INDEX IF NOT EXISTS ix_ccs_container_scanner_inspection_lookup
    ON containercompletenessstatuses(containernumber, scannertype, inspectionid);

\echo '## POST-INDEX'
SELECT indexname, indexdef
FROM pg_indexes
WHERE tablename = 'containercompletenessstatuses'
  AND indexname IN (
      'ix_ccs_container_scanner_inspection_unique',
      'ix_ccs_container_scanner_inspection_lookup'
  )
ORDER BY indexname;

COMMIT;

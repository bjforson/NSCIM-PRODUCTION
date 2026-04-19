-- CMR Composite Index for CMR→BOE Lifecycle Upgrade
-- This partial index speeds up the lookup of CMR records by (ContainerNumber, RotationNumber, BLNumber)
-- Used by IcumJsonIngestionService to find existing CMR records to upgrade when IM/EX BOE arrives

CREATE INDEX CONCURRENTLY IF NOT EXISTS idx_boedocuments_cmr_composite
ON boedocuments (containernumber, rotationnumber, blnumber)
WHERE clearancetype = 'CMR';

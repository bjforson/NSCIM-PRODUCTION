-- Split Integration Phase 1: Add split columns to analysisrecords + imageanalysisdecisions
-- All nullable, deploy-safe, idempotent. Re-runs are no-ops.

-- ── analysisrecords: per-record split tracking ──

ALTER TABLE analysisrecords
    ADD COLUMN IF NOT EXISTS ismulticontainerscan boolean NOT NULL DEFAULT false;

ALTER TABLE analysisrecords
    ADD COLUMN IF NOT EXISTS splitjobid uuid;

ALTER TABLE analysisrecords
    ADD COLUMN IF NOT EXISTS splitposition varchar(10);

ALTER TABLE analysisrecords
    ADD COLUMN IF NOT EXISTS splitstatus varchar(20);

ALTER TABLE analysisrecords
    ADD COLUMN IF NOT EXISTS splitresultid uuid;

ALTER TABLE analysisrecords
    ADD COLUMN IF NOT EXISTS splitoptiona_resultid uuid;

ALTER TABLE analysisrecords
    ADD COLUMN IF NOT EXISTS splitoptionb_resultid uuid;

-- Partial indexes (only index rows that actually have split data)
CREATE INDEX IF NOT EXISTS ix_analysisrecords_splitjobid
    ON analysisrecords (splitjobid)
    WHERE splitjobid IS NOT NULL;

CREATE INDEX IF NOT EXISTS ix_analysisrecords_splitstatus
    ON analysisrecords (splitstatus)
    WHERE splitstatus IS NOT NULL;

-- ── imageanalysisdecisions: link decision back to which split was used ──

ALTER TABLE imageanalysisdecisions
    ADD COLUMN IF NOT EXISTS splitjobid uuid;

ALTER TABLE imageanalysisdecisions
    ADD COLUMN IF NOT EXISTS splitresultid uuid;

ALTER TABLE imageanalysisdecisions
    ADD COLUMN IF NOT EXISTS splitchoicestrategy varchar(50);

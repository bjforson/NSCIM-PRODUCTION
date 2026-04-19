-- Wave Processing Schema Migration
-- Adds AnalysisParentGroups, WavePendingContainers tables
-- and wave-related columns to AnalysisGroups and AnalysisSettings

-- 1. New table: AnalysisParentGroups
CREATE TABLE IF NOT EXISTS analysisparentgroups (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    groupidentifier VARCHAR(150) NOT NULL,
    scannertype VARCHAR(20),
    totalexpectedcontainers INT NOT NULL DEFAULT 0,
    status VARCHAR(20) NOT NULL DEFAULT 'Active',
    createdatutc TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    completedatutc TIMESTAMPTZ
);

CREATE INDEX IF NOT EXISTS ix_analysisparentgroups_status ON analysisparentgroups(status);

-- 2. New table: WavePendingContainers
CREATE TABLE IF NOT EXISTS wavependingcontainers (
    id SERIAL PRIMARY KEY,
    parentgroupid UUID NOT NULL REFERENCES analysisparentgroups(id),
    containernumber VARCHAR(50) NOT NULL,
    scannertype VARCHAR(20),
    firstseenutc TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    status VARCHAR(20) NOT NULL DEFAULT 'Pending',
    becamereadyutc TIMESTAMPTZ
);

CREATE INDEX IF NOT EXISTS ix_wavependingcontainers_parentgroupid ON wavependingcontainers(parentgroupid);

-- 3. Add wave columns to AnalysisGroups
ALTER TABLE analysisgroups ADD COLUMN IF NOT EXISTS parentgroupid UUID REFERENCES analysisparentgroups(id);
ALTER TABLE analysisgroups ADD COLUMN IF NOT EXISTS wavenumber INT;
ALTER TABLE analysisgroups ADD COLUMN IF NOT EXISTS wavecreatedreason VARCHAR(50);

CREATE INDEX IF NOT EXISTS ix_analysisgroups_parentgroupid ON analysisgroups(parentgroupid);

-- 4. Add wave settings to AnalysisSettings
ALTER TABLE analysissettings ADD COLUMN IF NOT EXISTS enablewaveprocessing BOOLEAN NOT NULL DEFAULT false;
ALTER TABLE analysissettings ADD COLUMN IF NOT EXISTS waveminbatchsize INT NOT NULL DEFAULT 3;
ALTER TABLE analysissettings ADD COLUMN IF NOT EXISTS wavetimeouthours INT NOT NULL DEFAULT 24;
ALTER TABLE analysissettings ADD COLUMN IF NOT EXISTS waveautoclosedays INT NOT NULL DEFAULT 30;

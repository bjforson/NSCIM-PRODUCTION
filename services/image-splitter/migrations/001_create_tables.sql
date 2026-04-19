-- NSCIM Image Splitting Service - Database Schema
-- Run against: nickscan_production (PostgreSQL)

CREATE TABLE IF NOT EXISTS image_split_jobs (
    id              UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    container_numbers TEXT NOT NULL,
    source_image_id  UUID,
    scanner_type     VARCHAR(50),
    image_data       BYTEA,
    image_width      INT,
    image_height     INT,
    status           VARCHAR(20) DEFAULT 'pending',
    best_strategy    VARCHAR(50),
    best_score       FLOAT,
    split_x          INT,
    created_at       TIMESTAMPTZ DEFAULT NOW(),
    completed_at     TIMESTAMPTZ,
    error_message    TEXT
);

CREATE TABLE IF NOT EXISTS image_split_results (
    id              UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    job_id          UUID NOT NULL REFERENCES image_split_jobs(id) ON DELETE CASCADE,
    strategy_name   VARCHAR(50) NOT NULL,
    split_x         INT NOT NULL,
    confidence      FLOAT,
    processing_ms   INT,
    metadata        JSONB,
    left_image      BYTEA,
    right_image     BYTEA,
    created_at      TIMESTAMPTZ DEFAULT NOW()
);

CREATE TABLE IF NOT EXISTS image_split_assignments (
    id              UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    job_id          UUID NOT NULL REFERENCES image_split_jobs(id) ON DELETE CASCADE,
    result_id       UUID REFERENCES image_split_results(id),
    container_number VARCHAR(20) NOT NULL,
    position        VARCHAR(10) NOT NULL,
    image_data      BYTEA,
    approved        BOOLEAN DEFAULT FALSE,
    approved_by     VARCHAR(100),
    approved_at     TIMESTAMPTZ
);

-- Indexes
CREATE INDEX IF NOT EXISTS idx_split_jobs_status ON image_split_jobs(status);
CREATE INDEX IF NOT EXISTS idx_split_jobs_created ON image_split_jobs(created_at DESC);
CREATE INDEX IF NOT EXISTS idx_split_results_job ON image_split_results(job_id);
CREATE INDEX IF NOT EXISTS idx_split_assignments_job ON image_split_assignments(job_id);
CREATE INDEX IF NOT EXISTS idx_split_assignments_container ON image_split_assignments(container_number);

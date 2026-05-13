-- Image splitter - append-only shadow-mode prediction persistence
--
-- Adds separate run tables for remote vision/provider outputs and local model
-- predictions. These tables intentionally have no uniqueness constraints so
-- repeated shadow/backfill/evaluation attempts are retained as independent rows.
--
-- Existing Claude Vision columns on image_split_jobs remain in place for
-- backward compatibility with current reporting and tools.

CREATE TABLE IF NOT EXISTS image_split_remote_vision_runs (
    id              UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    job_id          UUID NOT NULL REFERENCES image_split_jobs(id) ON DELETE CASCADE,
    provider        VARCHAR(64) NOT NULL,
    model_name      VARCHAR(128) NOT NULL,
    model_version   VARCHAR(128),
    run_purpose     VARCHAR(32) NOT NULL DEFAULT 'shadow',
    status          VARCHAR(20) NOT NULL DEFAULT 'completed',
    prompt_version  VARCHAR(64),
    request_id      VARCHAR(128),
    split_x         INT,
    confidence      FLOAT,
    reasoning       TEXT,
    input_tokens    INT,
    output_tokens   INT,
    total_tokens    INT,
    latency_ms      INT,
    error_message   TEXT,
    raw_request     JSONB,
    raw_response    JSONB,
    metadata        JSONB,
    completed_at    TIMESTAMPTZ,
    created_at      TIMESTAMPTZ DEFAULT NOW()
);

ALTER TABLE image_split_remote_vision_runs
    ADD COLUMN IF NOT EXISTS job_id          UUID REFERENCES image_split_jobs(id) ON DELETE CASCADE,
    ADD COLUMN IF NOT EXISTS provider        VARCHAR(64),
    ADD COLUMN IF NOT EXISTS model_name      VARCHAR(128),
    ADD COLUMN IF NOT EXISTS model_version   VARCHAR(128),
    ADD COLUMN IF NOT EXISTS run_purpose     VARCHAR(32) DEFAULT 'shadow',
    ADD COLUMN IF NOT EXISTS status          VARCHAR(20) DEFAULT 'completed',
    ADD COLUMN IF NOT EXISTS prompt_version  VARCHAR(64),
    ADD COLUMN IF NOT EXISTS request_id      VARCHAR(128),
    ADD COLUMN IF NOT EXISTS split_x         INT,
    ADD COLUMN IF NOT EXISTS confidence      FLOAT,
    ADD COLUMN IF NOT EXISTS reasoning       TEXT,
    ADD COLUMN IF NOT EXISTS input_tokens    INT,
    ADD COLUMN IF NOT EXISTS output_tokens   INT,
    ADD COLUMN IF NOT EXISTS total_tokens    INT,
    ADD COLUMN IF NOT EXISTS latency_ms      INT,
    ADD COLUMN IF NOT EXISTS error_message   TEXT,
    ADD COLUMN IF NOT EXISTS raw_request     JSONB,
    ADD COLUMN IF NOT EXISTS raw_response    JSONB,
    ADD COLUMN IF NOT EXISTS metadata        JSONB,
    ADD COLUMN IF NOT EXISTS completed_at    TIMESTAMPTZ,
    ADD COLUMN IF NOT EXISTS created_at      TIMESTAMPTZ DEFAULT NOW();

ALTER TABLE image_split_remote_vision_runs
    ALTER COLUMN run_purpose SET DEFAULT 'shadow',
    ALTER COLUMN status SET DEFAULT 'completed',
    ALTER COLUMN created_at SET DEFAULT NOW();

CREATE TABLE IF NOT EXISTS image_split_local_model_prediction_runs (
    id                    UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    job_id                UUID NOT NULL REFERENCES image_split_jobs(id) ON DELETE CASCADE,
    model_name            VARCHAR(128) NOT NULL,
    model_version         VARCHAR(128),
    model_artifact_uri    TEXT,
    model_artifact_sha256 VARCHAR(64),
    training_run_id       VARCHAR(128),
    dataset_version       VARCHAR(128),
    run_purpose           VARCHAR(32) NOT NULL DEFAULT 'shadow',
    status                VARCHAR(20) NOT NULL DEFAULT 'completed',
    split_x               INT,
    confidence            FLOAT,
    uncertainty           FLOAT,
    latency_ms            INT,
    error_message         TEXT,
    prediction            JSONB,
    metadata              JSONB,
    completed_at          TIMESTAMPTZ,
    created_at            TIMESTAMPTZ DEFAULT NOW()
);

ALTER TABLE image_split_local_model_prediction_runs
    ADD COLUMN IF NOT EXISTS job_id                UUID REFERENCES image_split_jobs(id) ON DELETE CASCADE,
    ADD COLUMN IF NOT EXISTS model_name            VARCHAR(128),
    ADD COLUMN IF NOT EXISTS model_version         VARCHAR(128),
    ADD COLUMN IF NOT EXISTS model_artifact_uri    TEXT,
    ADD COLUMN IF NOT EXISTS model_artifact_sha256 VARCHAR(64),
    ADD COLUMN IF NOT EXISTS training_run_id       VARCHAR(128),
    ADD COLUMN IF NOT EXISTS dataset_version       VARCHAR(128),
    ADD COLUMN IF NOT EXISTS run_purpose           VARCHAR(32) DEFAULT 'shadow',
    ADD COLUMN IF NOT EXISTS status                VARCHAR(20) DEFAULT 'completed',
    ADD COLUMN IF NOT EXISTS split_x               INT,
    ADD COLUMN IF NOT EXISTS confidence            FLOAT,
    ADD COLUMN IF NOT EXISTS uncertainty           FLOAT,
    ADD COLUMN IF NOT EXISTS latency_ms            INT,
    ADD COLUMN IF NOT EXISTS error_message         TEXT,
    ADD COLUMN IF NOT EXISTS prediction            JSONB,
    ADD COLUMN IF NOT EXISTS metadata              JSONB,
    ADD COLUMN IF NOT EXISTS completed_at          TIMESTAMPTZ,
    ADD COLUMN IF NOT EXISTS created_at            TIMESTAMPTZ DEFAULT NOW();

ALTER TABLE image_split_local_model_prediction_runs
    ALTER COLUMN run_purpose SET DEFAULT 'shadow',
    ALTER COLUMN status SET DEFAULT 'completed',
    ALTER COLUMN created_at SET DEFAULT NOW();

CREATE INDEX IF NOT EXISTS ix_remote_vision_runs_job_created
    ON image_split_remote_vision_runs (job_id, created_at DESC);

CREATE INDEX IF NOT EXISTS ix_remote_vision_runs_provider_model
    ON image_split_remote_vision_runs (provider, model_name);

CREATE INDEX IF NOT EXISTS ix_remote_vision_runs_purpose_created
    ON image_split_remote_vision_runs (run_purpose, created_at DESC);

CREATE INDEX IF NOT EXISTS ix_remote_vision_runs_status
    ON image_split_remote_vision_runs (status);

CREATE INDEX IF NOT EXISTS ix_local_prediction_runs_job_created
    ON image_split_local_model_prediction_runs (job_id, created_at DESC);

CREATE INDEX IF NOT EXISTS ix_local_prediction_runs_model_version
    ON image_split_local_model_prediction_runs (model_name, model_version);

CREATE INDEX IF NOT EXISTS ix_local_prediction_runs_purpose_created
    ON image_split_local_model_prediction_runs (run_purpose, created_at DESC);

CREATE INDEX IF NOT EXISTS ix_local_prediction_runs_artifact_sha
    ON image_split_local_model_prediction_runs (model_artifact_sha256);

CREATE INDEX IF NOT EXISTS ix_local_prediction_runs_status
    ON image_split_local_model_prediction_runs (status);

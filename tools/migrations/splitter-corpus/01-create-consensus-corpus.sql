-- 1.20.x — Splitter consensus corpus
--
-- Growing set of operator-verified / algorithm-consensus examples used as
-- few-shot exemplars for the Claude Vision verifier. A row is added to
-- this corpus whenever inner_casting_pair and steel_wall_midpoint agree
-- within 10 pixels, OR an operator has hand-annotated the ground truth.
--
-- The corpus is:
--   - appended automatically by the orchestrator whenever a consensus
--     match happens (unless the same job is already in the corpus)
--   - sampled by the verifier on each call: 3 random rows become
--     <example> blocks in the Claude prompt showing the correct split
--   - never trimmed except by explicit operator action
--
-- Storing the image bytes inline is the simplest implementation; at ~250KB
-- per image × 200 corpus rows expected at steady state = ~50MB which is
-- acceptable. If the corpus grows beyond a few thousand entries we can
-- switch to a reference-by-job_id scheme.

CREATE TABLE IF NOT EXISTS splitter_consensus_corpus (
    id                     SERIAL PRIMARY KEY,
    source_job_id          UUID NOT NULL,
    image_data             BYTEA NOT NULL,
    image_width            INTEGER NOT NULL,
    image_height           INTEGER NOT NULL,
    icp_split_x            INTEGER,
    steel_wall_split_x     INTEGER,
    c1_right_casting_x_end INTEGER,
    c2_left_casting_x_start INTEGER,
    verified_split_x       INTEGER NOT NULL,
    verification_source    TEXT NOT NULL CHECK (
        verification_source IN ('consensus', 'operator', 'analyst_approve')
    ),
    consensus_delta_px     INTEGER,
    added_at               TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    added_by               TEXT
);

CREATE INDEX IF NOT EXISTS ix_scc_source_job
    ON splitter_consensus_corpus (source_job_id);

CREATE INDEX IF NOT EXISTS ix_scc_verification_source
    ON splitter_consensus_corpus (verification_source);

-- Prevent accidental duplicate rows from the same job (idempotent populator)
CREATE UNIQUE INDEX IF NOT EXISTS ux_scc_one_per_job
    ON splitter_consensus_corpus (source_job_id);

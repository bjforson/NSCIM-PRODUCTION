-- ─────────────────────────────────────────────────────────────────────────────
-- Image splitter — 1.19.0 Claude Vision columns
--
-- Adds tracking columns to image_split_jobs so we can audit Claude Vision's
-- accuracy against analyst ground truth over time and measure the cost of
-- running it on every scan vs on a sampled subset.
--
-- All columns nullable so existing rows are unaffected. Idempotent.
-- ─────────────────────────────────────────────────────────────────────────────

ALTER TABLE image_split_jobs
    ADD COLUMN IF NOT EXISTS claude_vision_split_x      INT         NULL,
    ADD COLUMN IF NOT EXISTS claude_vision_confidence   REAL        NULL,
    ADD COLUMN IF NOT EXISTS claude_vision_reasoning    TEXT        NULL,
    ADD COLUMN IF NOT EXISTS claude_vision_input_tokens INT         NULL,
    ADD COLUMN IF NOT EXISTS claude_vision_output_tokens INT        NULL,
    ADD COLUMN IF NOT EXISTS claude_vision_latency_ms   INT         NULL,
    ADD COLUMN IF NOT EXISTS claude_vision_model        VARCHAR(64) NULL,
    ADD COLUMN IF NOT EXISTS claude_vision_ran_at       TIMESTAMPTZ NULL;

CREATE INDEX IF NOT EXISTS ix_image_split_jobs_claude_vision_ran_at
    ON image_split_jobs (claude_vision_ran_at)
    WHERE claude_vision_ran_at IS NOT NULL;

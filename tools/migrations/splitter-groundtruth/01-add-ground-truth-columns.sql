-- 1.20.x — Splitter ground-truth annotation columns
--
-- Adds four new columns to image_split_jobs so operators can click-set the
-- TRUE split position via the new /groundtruth web UI, to calibrate the
-- strategies (especially Claude Vision) on asymmetric trailers (20+40) where
-- steel_wall_midpoint is geometrically wrong.
--
-- Columns:
--   ground_truth_split_x  — pixel x of the true split, -1 = "unusable image"
--   ground_truth_set_by   — operator name (from browser localStorage prompt)
--   ground_truth_set_at   — when it was set
--   ground_truth_notes    — free-text reason, e.g. "unusable, cargo obscures wall"
--
-- Already applied to nickscan_production on 2026-04-08. This file is the
-- canonical source for fresh environments.

ALTER TABLE image_split_jobs
  ADD COLUMN IF NOT EXISTS ground_truth_split_x  integer,
  ADD COLUMN IF NOT EXISTS ground_truth_set_by   text,
  ADD COLUMN IF NOT EXISTS ground_truth_set_at   timestamptz,
  ADD COLUMN IF NOT EXISTS ground_truth_notes    text;

CREATE INDEX IF NOT EXISTS ix_image_split_jobs_ground_truth_set_at
    ON image_split_jobs (ground_truth_set_at)
    WHERE ground_truth_set_at IS NOT NULL;

-- 2026-04-19: prevent duplicate raw-channel rows per scan
--
-- The FS6000RawChannelIngester uses INSERT … ON CONFLICT DO NOTHING so repeat
-- ingestion calls (backfill re-runs, race conditions between a live hook and a
-- manual trigger) don't create duplicate rows. Requires a unique index on
-- (scanid, imagetype).
--
-- Historically there's no dupe — verified at apply time — so we can add the
-- constraint without cleanup.
--
-- Idempotent: CREATE UNIQUE INDEX IF NOT EXISTS.

CREATE UNIQUE INDEX IF NOT EXISTS ix_fs6000images_scanid_imagetype_unique
    ON public.fs6000images (scanid, imagetype);

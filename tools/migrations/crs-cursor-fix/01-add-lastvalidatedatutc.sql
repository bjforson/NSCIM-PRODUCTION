-- ─────────────────────────────────────────────────────────────────────────────
-- 1.20.0 — CRS validator cursor fix
--
-- Adds LastValidatedAtUtc column on originalscanrecords so that
-- RunPostICUMSValidationWorkflowAsync can advance past rows it's already
-- processed instead of looping on the same oldest 100 rows forever.
--
-- The 1.19.0 fix removed the `settledCount >= containerNumbers.Count`
-- short-circuit that was silently acting as the progress cursor. The worker
-- subsequently got stuck processing the same 100 oldest multi-container scans
-- on every tick, never reaching the other 218 in the backlog.
--
-- 1.20.0 code uses this column as a proper cursor: rows with NULL or stale
-- (> 1 hour old) LastValidatedAtUtc are eligible. Stale rows get re-visited
-- so late-arriving BOE data eventually lands a validation.
--
-- Idempotent. Non-blocking (nullable column, partial index).
-- ─────────────────────────────────────────────────────────────────────────────

ALTER TABLE originalscanrecords
    ADD COLUMN IF NOT EXISTS lastvalidatedatutc TIMESTAMPTZ;

CREATE INDEX IF NOT EXISTS ix_originalscanrecords_lastvalidated
    ON originalscanrecords (lastvalidatedatutc)
    WHERE derivedrecordcount >= 2;

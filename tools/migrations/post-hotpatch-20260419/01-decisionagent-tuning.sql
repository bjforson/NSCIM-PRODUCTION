-- Post-hotpatch DecisionAgent tuning (applied 2026-04-19 during ops recovery).
--
-- The AI decision agent was flagging ~100% of records as abnormal because:
--   (1) allownormaldecisions = false blocked any "normal" outcome, and
--   (2) abnormalthreshold = 0.35 was low enough that almost every weighted
--       score tripped the abnormal bar.
--
-- Operations needed the agent to auto-clear low-risk records and only flag
-- higher-risk ones for analyst review. This seed loosens both parameters.
--
-- Idempotent: the UPDATE only writes the target values, so re-running is a no-op.
-- Run against: nickscan_production (the main app DB).
-- Usage:
--   PGPASSWORD=$NICKSCAN_DB_PASSWORD \
--     psql -U postgres -h localhost -d nickscan_production \
--          -f tools/migrations/post-hotpatch-20260419/01-decisionagent-tuning.sql
-- Restart NSCIM_API afterwards so the cached settings are reloaded.

BEGIN;

-- Show state BEFORE
SELECT 'BEFORE' AS marker, id, allownormaldecisions, abnormalthreshold, updatedatutc
FROM public.decisionagentsettings
ORDER BY id;

-- Apply the target values
UPDATE public.decisionagentsettings
SET    allownormaldecisions = TRUE,
       abnormalthreshold    = 0.50,
       updatedatutc         = NOW() AT TIME ZONE 'UTC'
WHERE  allownormaldecisions <> TRUE
   OR  abnormalthreshold   <> 0.50;

-- Show state AFTER
SELECT 'AFTER' AS marker, id, allownormaldecisions, abnormalthreshold, updatedatutc
FROM public.decisionagentsettings
ORDER BY id;

COMMIT;

-- =============================================================================
-- NICKSCAN ERP SOLUTION - Sprint 5G2 / Bridge B1
-- Create analysis_group_status_transitions audit table
--
-- Phase 1 of the v1 hardening bridge to v2 (see plan
-- C:\Users\Administrator\.claude\plans\i-need-an-analysis-abundant-pnueli.md
-- §B1). The v1 image-analysis pipeline currently has 37 distinct write
-- sites of `analysisgroups.status`, of which 34 bypass
-- AnalysisStatusValidator entirely. Promoting the validator from
-- advisory to mandatory requires (a) a facade class
-- (AnalysisGroupStateMachine) that's the sole writer, and (b) this
-- table, where the facade records every transition for forensic
-- replay.
--
-- This migration is ADDITIVE only — creating the table does not affect
-- any existing code path. The 37 call sites are refactored to go
-- through AnalysisGroupStateMachine in a separate change set; until
-- that lands, the table is empty in production. Catching transitions
-- the moment the facade is wired up gives ops a clean baseline (every
-- new transition observed, no historical noise).
--
-- Schema:
--   id                BIGSERIAL PRIMARY KEY
--   tenant_id         BIGINT NOT NULL DEFAULT 1     -- matches every other phase-1 table
--   group_id          UUID NOT NULL                 -- FK to analysisgroups.id
--   from_status       VARCHAR(40) NOT NULL          -- empty string for first/initial transition
--   to_status         VARCHAR(40) NOT NULL
--   trigger_name      VARCHAR(64) NOT NULL          -- e.g. 'AnalystSubmittedFindings', 'JanitorReleasedExpiredLease'
--   actor             VARCHAR(128) NOT NULL         -- user id (Guid as string) or service name
--   reason            VARCHAR(512) NOT NULL         -- free-text justification, never empty
--   correlation_id    VARCHAR(128)                  -- optional propagated correlation id
--   occurred_at_utc   TIMESTAMPTZ NOT NULL DEFAULT now()
--
-- Indexes:
--   (tenant_id, group_id, occurred_at_utc DESC) — per-group timeline view
--   (tenant_id, occurred_at_utc DESC)           — global "what's happening" feed
--
-- RLS:
--   Standard tenant_isolation_<table> policy with the same fail-closed
--   COALESCE expression every other phase-1 table uses (default '0').
--   FORCE ROW LEVEL SECURITY so even the table owner is filtered when
--   querying via app connections (matches the 2026-04-25 hardening that
--   put 180 policies under FORCE).
--
-- Grants:
--   nscim_app gets SELECT + INSERT only — the table is append-only, no
--   UPDATE, no DELETE. Audit trail integrity comes from the runtime
--   role's lack of mutation grants, not from a trigger.
--
-- Idempotent: re-running just re-asserts the table (IF NOT EXISTS), the
-- policy (DROP IF EXISTS + CREATE), and FORCE RLS.
-- =============================================================================

\echo Creating analysis_group_status_transitions audit table

CREATE TABLE IF NOT EXISTS public.analysis_group_status_transitions (
    id              BIGSERIAL PRIMARY KEY,
    tenant_id       BIGINT NOT NULL DEFAULT 1,
    group_id        UUID NOT NULL,
    from_status     VARCHAR(40) NOT NULL,
    to_status       VARCHAR(40) NOT NULL,
    trigger_name    VARCHAR(64) NOT NULL,
    actor           VARCHAR(128) NOT NULL,
    reason          VARCHAR(512) NOT NULL,
    correlation_id  VARCHAR(128),
    occurred_at_utc TIMESTAMPTZ NOT NULL DEFAULT now()
);

-- Per-group timeline. The `(tenant_id, group_id, occurred_at_utc DESC)`
-- shape supports the "show me everything that happened to this AG"
-- admin view, with the tenant prefix matching the RLS predicate so
-- Postgres can use the index without an extra filter step.
CREATE INDEX IF NOT EXISTS ix_agst_tenant_group_time
    ON public.analysis_group_status_transitions (tenant_id, group_id, occurred_at_utc DESC);

-- Global "what's happening" feed — backs the future
-- /api/_module/queues observability endpoint's "recent transitions"
-- panel. Tenant-scoped so RLS still filters.
CREATE INDEX IF NOT EXISTS ix_agst_tenant_time
    ON public.analysis_group_status_transitions (tenant_id, occurred_at_utc DESC);

-- Index on (actor) for the "show me everything DECISION-AGENT did
-- today" forensic query — useful when triaging a Decision Agent
-- non-shadow incident. Partial index keeps it cheap (most transitions
-- are human actors).
CREATE INDEX IF NOT EXISTS ix_agst_system_actors_time
    ON public.analysis_group_status_transitions (actor, occurred_at_utc DESC)
    WHERE actor IN ('DECISION-AGENT', 'SYSTEM-HOUSEKEEPING', 'QueueJanitor', 'OutboxRelay');

-- Enable + force RLS. Same posture as the rest of phase-1.
ALTER TABLE public.analysis_group_status_transitions ENABLE ROW LEVEL SECURITY;
ALTER TABLE public.analysis_group_status_transitions FORCE ROW LEVEL SECURITY;

-- Recreate the tenant_isolation policy with the fail-closed default.
-- Matches the COALESCE expression used everywhere else in phase-1.
DROP POLICY IF EXISTS tenant_isolation_analysis_group_status_transitions
    ON public.analysis_group_status_transitions;

CREATE POLICY tenant_isolation_analysis_group_status_transitions
    ON public.analysis_group_status_transitions
    FOR ALL
    USING (tenant_id = (COALESCE(NULLIF(current_setting('app.tenant_id', true), ''), '0')::bigint))
    WITH CHECK (tenant_id = (COALESCE(NULLIF(current_setting('app.tenant_id', true), ''), '0')::bigint));

-- Grants: nscim_app gets SELECT + INSERT only. No UPDATE / DELETE —
-- the audit trail is append-only by role design, not by trigger.
DO $$
BEGIN
    IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'nscim_app') THEN
        GRANT SELECT, INSERT ON public.analysis_group_status_transitions TO nscim_app;
        -- The sequence behind BIGSERIAL needs USAGE for INSERT to allocate
        -- a primary key value.
        GRANT USAGE, SELECT ON SEQUENCE public.analysis_group_status_transitions_id_seq TO nscim_app;
    END IF;
END $$;

\echo Done — analysis_group_status_transitions ready for AnalysisGroupStateMachine

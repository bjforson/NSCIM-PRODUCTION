-- ─────────────────────────────────────────────────────────────────────────────
-- 1.14.0 — Record Completeness schema
--
-- Adds two new tables to nickscan_production for the record-anchored
-- reconciliation model:
--
--   recordcompletenessstatuses  — one row per ICUMS declaration
--   recordexpectedcontainers    — one row per container the declaration expects
--
-- Plus four new tunables on analysissettings for the reconciliation worker.
--
-- Strictly ADDITIVE. Nothing existing is modified. The existing
-- containercompletenessstatuses / wavependingcontainers / analysisparentgroups
-- pipeline continues to run untouched.
--
-- Idempotent. Safe to re-run.
--
-- Run with:
--   PGPASSWORD=... psql -h localhost -U postgres -d nickscan_production \
--     -v ON_ERROR_STOP=1 \
--     -f tools/migrations/record-completeness/01-add-record-completeness-tables.sql
-- ─────────────────────────────────────────────────────────────────────────────

\timing on

BEGIN;

-- ── Table 1: recordcompletenessstatuses ─────────────────────────────────────
CREATE TABLE IF NOT EXISTS recordcompletenessstatuses (
    id                          SERIAL PRIMARY KEY,
    declarationnumber           VARCHAR(100) NOT NULL,
    clearancetype               VARCHAR(20),
    regimecode                  VARCHAR(20),
    primaryboedocumentid        INT,
    rotationnumber              VARCHAR(100),
    blnumber                    VARCHAR(100),
    containergroupkey           VARCHAR(150),
    scannertype                 VARCHAR(20),

    totalexpectedcontainers     INT NOT NULL DEFAULT 0,
    containersawaitingscan      INT NOT NULL DEFAULT 0,
    containersscanned           INT NOT NULL DEFAULT 0,
    containersready             INT NOT NULL DEFAULT 0,
    containersdecided           INT NOT NULL DEFAULT 0,
    containerssubmitted         INT NOT NULL DEFAULT 0,
    containersnoimage           INT NOT NULL DEFAULT 0,
    containersnoscan            INT NOT NULL DEFAULT 0,

    status                      VARCHAR(20) NOT NULL DEFAULT 'Pending',
    workflowstage               VARCHAR(50) NOT NULL DEFAULT 'Pending',

    firstseenutc                TIMESTAMPTZ NOT NULL,
    lastnewcontaineratutc       TIMESTAMPTZ,
    firstreadyatutc             TIMESTAMPTZ,
    archivedatutc               TIMESTAMPTZ,
    archivalreason              VARCHAR(50),
    lastcheckedatutc            TIMESTAMPTZ,

    declarationsjson            JSONB,

    createdatutc                TIMESTAMPTZ NOT NULL DEFAULT now(),
    updatedatutc                TIMESTAMPTZ NOT NULL DEFAULT now(),
    tenant_id                   BIGINT NOT NULL DEFAULT 1
);

-- Unique key — functional index to handle the "scannertype may be NULL" case.
-- A declaration can exist with NULL scannertype (unknown at record creation)
-- OR with a specific scannertype; the (declarationnumber, COALESCE(scannertype,''))
-- combination must be unique.
CREATE UNIQUE INDEX IF NOT EXISTS ix_recordcompleteness_decl_scanner_unique
    ON recordcompletenessstatuses (declarationnumber, COALESCE(scannertype, ''));

CREATE INDEX IF NOT EXISTS ix_recordcompleteness_status
    ON recordcompletenessstatuses (status);
CREATE INDEX IF NOT EXISTS ix_recordcompleteness_workflowstage
    ON recordcompletenessstatuses (workflowstage);
CREATE INDEX IF NOT EXISTS ix_recordcompleteness_containergroupkey
    ON recordcompletenessstatuses (containergroupkey)
    WHERE containergroupkey IS NOT NULL;
CREATE INDEX IF NOT EXISTS ix_recordcompleteness_lastnewcontainer
    ON recordcompletenessstatuses (lastnewcontaineratutc);
CREATE INDEX IF NOT EXISTS ix_recordcompleteness_tenant
    ON recordcompletenessstatuses (tenant_id, id);

-- Row-level security (matches the Phase 1 tenancy pattern applied to every other
-- NSCIM table). Idempotent: DROP POLICY IF EXISTS first.
ALTER TABLE recordcompletenessstatuses ENABLE ROW LEVEL SECURITY;
DROP POLICY IF EXISTS tenant_isolation_recordcompletenessstatuses ON recordcompletenessstatuses;
CREATE POLICY tenant_isolation_recordcompletenessstatuses ON recordcompletenessstatuses
    USING      (tenant_id = (COALESCE(NULLIF(current_setting('app.tenant_id', true), ''), '1'))::bigint)
    WITH CHECK (tenant_id = (COALESCE(NULLIF(current_setting('app.tenant_id', true), ''), '1'))::bigint);


-- ── Table 2: recordexpectedcontainers ───────────────────────────────────────
CREATE TABLE IF NOT EXISTS recordexpectedcontainers (
    id                  SERIAL PRIMARY KEY,
    recordid            INT NOT NULL REFERENCES recordcompletenessstatuses(id) ON DELETE CASCADE,
    containernumber     VARCHAR(50) NOT NULL,
    status              VARCHAR(20) NOT NULL DEFAULT 'AwaitingScan',
    boedocumentid       INT,
    housebl             VARCHAR(100),
    consigneename       VARCHAR(500),
    inspectionid        VARCHAR(50),
    scannertype         VARCHAR(20),
    firstseenutc        TIMESTAMPTZ NOT NULL,
    scannedatutc        TIMESTAMPTZ,
    becamereadyutc      TIMESTAMPTZ,
    decidedatutc        TIMESTAMPTZ,
    tenant_id           BIGINT NOT NULL DEFAULT 1
);

CREATE UNIQUE INDEX IF NOT EXISTS ix_recordexpected_record_container
    ON recordexpectedcontainers (recordid, containernumber);
CREATE INDEX IF NOT EXISTS ix_recordexpected_container
    ON recordexpectedcontainers (containernumber);
CREATE INDEX IF NOT EXISTS ix_recordexpected_status
    ON recordexpectedcontainers (status);
CREATE INDEX IF NOT EXISTS ix_recordexpected_tenant
    ON recordexpectedcontainers (tenant_id, id);

ALTER TABLE recordexpectedcontainers ENABLE ROW LEVEL SECURITY;
DROP POLICY IF EXISTS tenant_isolation_recordexpectedcontainers ON recordexpectedcontainers;
CREATE POLICY tenant_isolation_recordexpectedcontainers ON recordexpectedcontainers
    USING      (tenant_id = (COALESCE(NULLIF(current_setting('app.tenant_id', true), ''), '1'))::bigint)
    WITH CHECK (tenant_id = (COALESCE(NULLIF(current_setting('app.tenant_id', true), ''), '1'))::bigint);


-- ── AnalysisSettings tunables for the reconciliation worker ─────────────────
ALTER TABLE analysissettings
    ADD COLUMN IF NOT EXISTS recordreconciliationenabled         BOOLEAN NOT NULL DEFAULT true,
    ADD COLUMN IF NOT EXISTS recordreconciliationintervalminutes INT     NOT NULL DEFAULT 30,
    ADD COLUMN IF NOT EXISTS recordarchiveafterdays              INT     NOT NULL DEFAULT 30,
    ADD COLUMN IF NOT EXISTS recordreconciliationbatchsize       INT     NOT NULL DEFAULT 100;


-- ── Tiny KV-style watermark table for the reconciliation worker ─────────────
-- (If a generic kv table already exists in the app db, the worker should use
-- that instead. Otherwise this is the lightweight home for the watermark.)
-- NOTE: column names use NSCIM's flat-lowercase EF convention (no underscores).
-- A hand-written snake_case variant was shipped in the initial 1.14.0 deploy and
-- required an ALTER TABLE RENAME hotfix — the version below is the corrected one
-- that fresh environments should use.
CREATE TABLE IF NOT EXISTS recordreconciliationstate (
    id                      INT PRIMARY KEY DEFAULT 1,
    lastwatermarkutc        TIMESTAMPTZ,
    lasttickatutc           TIMESTAMPTZ,
    lasttickdurationms      INT,
    recordscreatedtotal     BIGINT NOT NULL DEFAULT 0,
    recordsupdatedtotal     BIGINT NOT NULL DEFAULT 0,
    containerspromotedtotal BIGINT NOT NULL DEFAULT 0,
    recordsarchivedtotal    BIGINT NOT NULL DEFAULT 0,
    tenant_id               BIGINT NOT NULL DEFAULT 1,
    CONSTRAINT recordreconciliationstate_singleton CHECK (id = 1)
);

INSERT INTO recordreconciliationstate (id, tenant_id) VALUES (1, 1)
ON CONFLICT (id) DO NOTHING;

ALTER TABLE recordreconciliationstate ENABLE ROW LEVEL SECURITY;
DROP POLICY IF EXISTS tenant_isolation_recordreconciliationstate ON recordreconciliationstate;
CREATE POLICY tenant_isolation_recordreconciliationstate ON recordreconciliationstate
    USING      (tenant_id = (COALESCE(NULLIF(current_setting('app.tenant_id', true), ''), '1'))::bigint)
    WITH CHECK (tenant_id = (COALESCE(NULLIF(current_setting('app.tenant_id', true), ''), '1'))::bigint);

COMMIT;

-- Verify
\d recordcompletenessstatuses
\d recordexpectedcontainers

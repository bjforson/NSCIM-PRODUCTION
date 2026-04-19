-- ─────────────────────────────────────────────────────────────────────────────
-- ICUMS Staging DB schema — nickscan_icums_staging
--
-- Built for ad-hoc analysis of the Y:\BatchData + Y:\ContainerData JSON
-- backlog (~44k files, ~100k+ BOE documents). Mirrors the production
-- nickscan_downloads shape closely enough that the same queries work, but
-- keeps the analysis isolated from production data.
--
-- Keys:
--   staging_boe_documents  — one row per BOE document in a JSON file.
--                            Uniqueness is (containernumber, declarationnumber).
--   staging_manifest_items — one row per manifest item under a BOE.
--   staging_source_files   — audit trail: which files produced which rows.
-- ─────────────────────────────────────────────────────────────────────────────

CREATE TABLE IF NOT EXISTS staging_source_files (
    id              SERIAL PRIMARY KEY,
    file_path       TEXT NOT NULL UNIQUE,
    file_name       TEXT NOT NULL,
    file_kind       VARCHAR(20) NOT NULL,   -- 'BatchData' | 'ContainerData'
    file_size_bytes BIGINT,
    document_count  INT,
    item_count      INT,
    parse_status    VARCHAR(20) NOT NULL DEFAULT 'pending',  -- pending | ok | failed
    parse_error     TEXT,
    parsed_at       TIMESTAMPTZ,
    ingested_at     TIMESTAMPTZ NOT NULL DEFAULT now()
);

CREATE INDEX IF NOT EXISTS ix_staging_source_files_kind_status
    ON staging_source_files (file_kind, parse_status);

CREATE TABLE IF NOT EXISTS staging_boe_documents (
    id                      BIGSERIAL PRIMARY KEY,
    source_file_id          INT NOT NULL REFERENCES staging_source_files(id) ON DELETE CASCADE,
    document_index          INT NOT NULL,

    container_number        VARCHAR(100) NOT NULL,
    container_description   TEXT,
    container_iso           VARCHAR(20),
    container_quantity      INT,
    container_weight        NUMERIC,
    is_vehicle              BOOLEAN NOT NULL DEFAULT false,
    vehicle_identifier      VARCHAR(100),

    -- Header
    imp_name                TEXT,
    imp_address             TEXT,
    exp_name                TEXT,
    exp_address             TEXT,
    declarant_name          TEXT,
    declarant_address       TEXT,
    total_duty_paid         NUMERIC,
    crms_level              VARCHAR(20),
    declaration_number      VARCHAR(100),
    regime_code             VARCHAR(20),
    no_of_containers        INT,
    comp_off_remarks        TEXT,
    impexp_name             TEXT,
    impexp_address          TEXT,
    declaration_version     INT,
    declaration_date        VARCHAR(50),
    clearance_type          VARCHAR(20),
    ccvr_intel_remarks      TEXT,

    -- ManifestDetails (optional — often null)
    rotation_number         VARCHAR(100),
    bl_number               VARCHAR(100),
    house_bl                VARCHAR(100),
    master_bl_number        TEXT,
    delivery_place          VARCHAR(200),
    consignee_name          TEXT,
    consignee_address       TEXT,
    country_of_origin       VARCHAR(100),
    marks_numbers           TEXT,
    shipper_name            TEXT,
    shipper_address         TEXT,
    goods_description       TEXT,
    is_consolidated         BOOLEAN NOT NULL DEFAULT false,

    raw_json                JSONB,
    created_at              TIMESTAMPTZ NOT NULL DEFAULT now()
);

CREATE INDEX IF NOT EXISTS ix_staging_boe_container              ON staging_boe_documents (container_number);
CREATE INDEX IF NOT EXISTS ix_staging_boe_declaration            ON staging_boe_documents (declaration_number);
CREATE INDEX IF NOT EXISTS ix_staging_boe_clearance              ON staging_boe_documents (clearance_type);
CREATE INDEX IF NOT EXISTS ix_staging_boe_is_vehicle             ON staging_boe_documents (is_vehicle);
CREATE INDEX IF NOT EXISTS ix_staging_boe_declaration_container
    ON staging_boe_documents (declaration_number, container_number)
    WHERE declaration_number IS NOT NULL;

CREATE TABLE IF NOT EXISTS staging_manifest_items (
    id                  BIGSERIAL PRIMARY KEY,
    boe_document_id     BIGINT NOT NULL REFERENCES staging_boe_documents(id) ON DELETE CASCADE,
    item_index          INT NOT NULL,
    item_no             INT,
    hs_code             VARCHAR(20),
    description         TEXT,
    quantity            NUMERIC,
    unit                VARCHAR(50),
    weight              NUMERIC,
    item_fob            NUMERIC,
    item_duty_paid      NUMERIC,
    fob_currency        VARCHAR(10),
    country_of_origin   VARCHAR(100),
    cpc                 VARCHAR(50)
);

CREATE INDEX IF NOT EXISTS ix_staging_items_boe      ON staging_manifest_items (boe_document_id);
CREATE INDEX IF NOT EXISTS ix_staging_items_hscode   ON staging_manifest_items (hs_code);
CREATE INDEX IF NOT EXISTS ix_staging_items_fts      ON staging_manifest_items USING gin (to_tsvector('english', coalesce(description, '')));

-- ── Analysis views ───────────────────────────────────────────────────────────
-- View: one row per declaration (complete record) with aggregates
CREATE OR REPLACE VIEW staging_records AS
SELECT
    d.declaration_number,
    MAX(d.clearance_type)            AS clearance_type,
    MAX(d.regime_code)               AS regime_code,
    MAX(d.crms_level)                AS crms_level,
    MAX(d.impexp_name)               AS importer,
    MAX(d.declaration_date)          AS declaration_date,
    MAX(d.bl_number)                 AS bl_number,
    MAX(d.master_bl_number)          AS master_bl_number,
    MAX(d.rotation_number)           AS rotation_number,
    COUNT(DISTINCT d.container_number) AS container_count,
    BOOL_OR(d.is_vehicle)            AS has_vehicle,
    (SELECT COUNT(*) FROM staging_manifest_items i
       WHERE i.boe_document_id IN (SELECT id FROM staging_boe_documents d2 WHERE d2.declaration_number = d.declaration_number))
                                      AS total_item_rows,
    (SELECT COUNT(DISTINCT i.hs_code) FROM staging_manifest_items i
       WHERE i.boe_document_id IN (SELECT id FROM staging_boe_documents d2 WHERE d2.declaration_number = d.declaration_number))
                                      AS distinct_hs_codes,
    (SELECT COUNT(DISTINCT LEFT(COALESCE(i.description, ''), 40)) FROM staging_manifest_items i
       WHERE i.boe_document_id IN (SELECT id FROM staging_boe_documents d2 WHERE d2.declaration_number = d.declaration_number))
                                      AS distinct_description_prefixes
FROM staging_boe_documents d
WHERE d.declaration_number IS NOT NULL AND d.declaration_number <> ''
GROUP BY d.declaration_number;

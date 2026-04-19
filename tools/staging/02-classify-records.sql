-- ─────────────────────────────────────────────────────────────────────────────
-- Record classification — homogeneous vs heterogeneous, vehicle filter
--
-- Mirrors the production RecordCompletenessStatus model: "a record" =
-- a declaration (grouped on declaration_number). Per Jonathan's rules:
--
--   Homogeneous:   < 5 line items  OR  >= 5 items but all descriptions
--                  share the same first token (loose "same family" test).
--   Heterogeneous: >= 5 line items AND wildly different descriptions
--                  (distinct first-token count > 1).
--   Vehicle:       ANY BOE doc in the record has is_vehicle=true
--                  OR ANY item description mentions a vehicle keyword.
--                  Vehicle records are excluded from the analysis population.
--
-- "First token" = LOWER(SPLIT_PART(TRIM(description), ' ', 1)) — a cheap
-- proxy for "same commodity family" without needing HS hierarchy lookups.
-- ─────────────────────────────────────────────────────────────────────────────

DROP MATERIALIZED VIEW IF EXISTS staging_record_classification CASCADE;

CREATE MATERIALIZED VIEW staging_record_classification AS
WITH per_record AS (
    SELECT
        d.declaration_number,
        BOOL_OR(d.is_vehicle) AS has_vehicle_flag,
        COUNT(DISTINCT d.container_number) AS container_count,
        MAX(d.clearance_type) AS clearance_type,
        MAX(d.impexp_name)    AS importer,
        COUNT(i.id)           AS line_item_count,
        COUNT(DISTINCT LOWER(SPLIT_PART(TRIM(COALESCE(i.description, '')), ' ', 1))) AS distinct_first_tokens,
        BOOL_OR(
            COALESCE(i.description, '') ~* '\y(toyota|honda|nissan|mazda|hyundai|kia|ford|chevrolet|mitsubishi|volkswagen|bmw|mercedes|audi|lexus|peugeot|renault|suzuki|isuzu|jeep|chrysler|dodge|land rover|landrover|range rover|rangerover|volvo|subaru|porsche|ferrari|lamborghini|bentley|rolls royce|rollsroyce|maserati|mini cooper|minicooper|fiat|alfa romeo|alfaromeo|jaguar|tesla|acura|infiniti|lincoln|cadillac|buick|gmc|ram|bus|motorcycle|motor cycle|motorbike|scooter|tricycle|vehicle|chassis|automobile|used car|new car|pickup|pick up|truck|tractor)\y'
        ) AS items_mention_vehicle
    FROM staging_boe_documents d
    LEFT JOIN staging_manifest_items i ON i.boe_document_id = d.id
    WHERE d.declaration_number IS NOT NULL AND d.declaration_number <> ''
    GROUP BY d.declaration_number
)
SELECT
    declaration_number,
    clearance_type,
    importer,
    container_count,
    line_item_count,
    distinct_first_tokens,
    (has_vehicle_flag OR items_mention_vehicle) AS is_vehicle_record,
    CASE
        WHEN (has_vehicle_flag OR items_mention_vehicle) THEN 'vehicle_excluded'
        WHEN line_item_count < 5 THEN 'homogeneous_small'
        WHEN distinct_first_tokens <= 2 THEN 'homogeneous_uniform'
        ELSE 'heterogeneous'
    END AS classification
FROM per_record;

CREATE INDEX ix_src_classification ON staging_record_classification (classification);
CREATE INDEX ix_src_declaration    ON staging_record_classification (declaration_number);

-- ── Quick-look reports ──────────────────────────────────────────────────────
-- Classification counts
-- SELECT classification, COUNT(*) FROM staging_record_classification GROUP BY 1 ORDER BY 2 DESC;

-- Homogeneous sample
-- SELECT * FROM staging_record_classification
--  WHERE classification LIKE 'homogeneous%' ORDER BY random() LIMIT 20;

-- Heterogeneous sample (the interesting ones for AI training signal)
-- SELECT * FROM staging_record_classification
--  WHERE classification = 'heterogeneous' ORDER BY line_item_count DESC LIMIT 20;

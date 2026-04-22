-- =====================================================================
-- ICUMS outreach export: real declarations with no DeliveryPlace in source
-- =====================================================================
-- Purpose: Hand this list to the ICUMS integration contact and ask why
--          the ManifestDetails.DeliveryPlace field is absent from the
--          source feed for these BOEs.
--
-- As of audit (2026-04-22): 324 records, all IM clearance, regimes
-- 40 (258), 70 (41), 90 (19), 80 (6).
--
-- Usage (from psql):
--   \o icums_no_dp.csv
--   \copy (...see query below...) TO STDOUT WITH CSV HEADER
--   \o
--
-- Or one-liner:
-- psql -h localhost -U postgres -d nickscan_downloads \
--      -c "\copy (<SELECT below>) TO 'icums_no_dp.csv' WITH CSV HEADER"
-- =====================================================================

SELECT
    id                              AS boe_id,
    containernumber                 AS container_number,
    declarationnumber               AS declaration_number,
    clearancetype                   AS clearance_type,
    regimecode                      AS regime_code,
    declarationdate                 AS declaration_date,
    blnumber                        AS bl_number,
    rotationnumber                  AS rotation_number,
    consigneename                   AS consignee,
    crmslevel                       AS crms_level,
    createdat                       AS ingested_at
FROM boedocuments
WHERE regimecode IS NOT NULL
  AND (deliveryplace IS NULL OR deliveryplace = '')
ORDER BY regimecode, createdat DESC;

-- Sanity-check the list size before exporting:
-- SELECT COUNT(*) FROM boedocuments WHERE regimecode IS NOT NULL AND (deliveryplace IS NULL OR deliveryplace='');

-- ─────────────────────────────────────────────────────────────────────────────
-- 1.14.0 — Record Completeness backfill (Phase 1: export from ICUMS)
--
-- Run against nickscan_downloads. Writes a CSV that 02b-import-to-production.sql
-- then ingests into nickscan_production.
-- ─────────────────────────────────────────────────────────────────────────────

\timing on

\copy (SELECT DISTINCT ON (TRIM(b.declarationnumber), UPPER(TRIM(b.containernumber))) TRIM(b.declarationnumber) AS declarationnumber, UPPER(TRIM(b.containernumber)) AS containernumber, b.clearancetype, b.regimecode, b.id AS primary_boe_id, b.rotationnumber, b.blnumber, b.housebl, b.consigneename FROM boedocuments b WHERE b.clearancetype IN ('IM','EX') AND b.declarationnumber IS NOT NULL AND b.declarationnumber <> '' AND b.containernumber IS NOT NULL AND b.containernumber <> '' ORDER BY TRIM(b.declarationnumber), UPPER(TRIM(b.containernumber)), b.id DESC) TO 'C:/Users/Administrator/AppData/Local/Temp/record_backfill_icums.csv' CSV HEADER

\echo Export complete. Next: run 02b-import-to-production.sql against nickscan_production.

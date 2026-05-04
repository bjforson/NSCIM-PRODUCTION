-- ─────────────────────────────────────────────────────────────────────────────
-- documenttype tagging at ingest (audit option (b), 2026-05-03)
--
-- WHY
-- ---
-- ICUMS sends Bonded Transportation declarations (regimes 80/88/89) with the
-- same clearancetype = "IM" as regular import BOEs. The audit found 5,434
-- regime-80 rows in nickscan_downloads.boedocuments labelled IM natively that
-- are actually transit cargo. We can't fix the upstream conflation, but we CAN
-- tag at ingest: derive a documenttype value from regimecode so downstream
-- consumers (rule layer, BT-vs-BOE filters, dashboards) can scope on
-- documenttype = 'Transit' / 'BOE' / 'Free Zone' instead of hard-coding regime
-- set membership in every consumer.
--
-- This migration is purely additive:
--   1. ADD COLUMN documenttype varchar(20) NULL (default keeps it nullable —
--      pre-declaration CMR rows with no regime stay NULL).
--   2. CREATE INDEX on documenttype so rule queries can scope cheaply.
--   3. One-shot UPDATE backfill from regimecode using the canonical Ghana
--      Customs ICUMS regime map (verified 2026-05-03 against
--      external.unipassghana.com).
--
-- The application code stamps documenttype on every save going forward via
-- RegimeDirectionMap.ClassifyDocumentType. This backfill is one-shot for the
-- existing rows.
--
-- IDEMPOTENT: re-running the ALTER + CREATE INDEX is a no-op. Re-running the
-- UPDATE is also safe — it overwrites with the same canonical mapping (all
-- rules below are deterministic given regimecode).
--
-- DATABASE: nickscan_downloads (the IcumDownloadsDbContext target).
--
-- CANONICAL REGIME → DOCUMENTTYPE MAP (verified 2026-05-03)
-- --------------------------------------------------------
--   Transit    : 80, 88, 89
--   Free Zone  : 90, 94, 95, 97, 99
--   BOE        : 10, 19, 20, 24, 27,
--                30, 34, 35, 37, 39,
--                40, 45, 47, 48, 49,
--                50, 57, 59,
--                61, 62,
--                70, 72, 75, 77, 79
--   NULL regime → leave documenttype NULL (CMR pre-declarations).
-- ─────────────────────────────────────────────────────────────────────────────

\timing on

BEGIN;

-- 1. Column.
ALTER TABLE boedocuments
    ADD COLUMN IF NOT EXISTS documenttype varchar(20) NULL;

-- 2. Index. Keep small: most queries will scope ('Transit') or ('BOE'); a plain
--    btree is fine for cardinality 4 (Transit / BOE / Free Zone / NULL).
CREATE INDEX IF NOT EXISTS ix_boedocuments_documenttype
    ON boedocuments (documenttype);

-- 3. Backfill. Trim regimecode so '40 ' / '40' both classify the same way.
--    NULL/blank regime → NULL documenttype (pre-declaration CMR with no
--    regime yet).
UPDATE boedocuments
   SET documenttype = CASE
       WHEN btrim(regimecode) IN ('80', '88', '89')
            THEN 'Transit'
       WHEN btrim(regimecode) IN ('90', '94', '95', '97', '99')
            THEN 'Free Zone'
       WHEN btrim(regimecode) IN (
                '10', '19', '20', '24', '27',
                '30', '34', '35', '37', '39',
                '40', '45', '47', '48', '49',
                '50', '57', '59',
                '61', '62',
                '70', '72', '75', '77', '79')
            THEN 'BOE'
       ELSE NULL  -- unknown regime, or NULL regime; leave NULL
   END
 WHERE documenttype IS DISTINCT FROM CASE
       WHEN btrim(regimecode) IN ('80', '88', '89')
            THEN 'Transit'
       WHEN btrim(regimecode) IN ('90', '94', '95', '97', '99')
            THEN 'Free Zone'
       WHEN btrim(regimecode) IN (
                '10', '19', '20', '24', '27',
                '30', '34', '35', '37', '39',
                '40', '45', '47', '48', '49',
                '50', '57', '59',
                '61', '62',
                '70', '72', '75', '77', '79')
            THEN 'BOE'
       ELSE NULL
   END;

COMMIT;

-- ─────────────────────────────────────────────────────────────────────────────
-- Verification: row counts by documenttype + a peek at any unrecognised regime
-- codes that landed in the NULL bucket despite having a non-null regimecode.
-- ─────────────────────────────────────────────────────────────────────────────
SELECT documenttype,
       COUNT(*) AS row_count
  FROM boedocuments
 GROUP BY documenttype
 ORDER BY documenttype NULLS LAST;

-- Surface unmapped non-null regimes so the canonical map can be extended if
-- ICUMS ever introduces a new regime code.
SELECT btrim(regimecode) AS unmapped_regime,
       COUNT(*)          AS row_count
  FROM boedocuments
 WHERE documenttype IS NULL
   AND regimecode IS NOT NULL
   AND btrim(regimecode) <> ''
 GROUP BY btrim(regimecode)
 ORDER BY row_count DESC;

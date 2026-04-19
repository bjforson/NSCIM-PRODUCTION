-- ─────────────────────────────────────────────────────────────────────────────
-- Gap 2 backfill — historical SuspiciousAreas JSON → typed ContainerAnnotation
--
-- Walks every imageanalysisdecisions row that has a non-empty SuspiciousAreas
-- JSON blob and emits one ContainerAnnotation row per rectangle, linked to the
-- decision via imageanalysisdecisionid. Categories on the parent decision (if
-- any) propagate onto each annotation row.
--
-- Idempotent: skips any decision id that already has typed ContainerAnnotation
-- rows linked to it. Safe to run multiple times.
--
-- Case-insensitive: handles both PascalCase (X / Y / Width / Height) — the
-- shape of every existing prod row — and lowercase (x / y / width / height)
-- in case any newer write path emits them.
--
-- Run with:
--   PGPASSWORD=... psql -h localhost -U postgres -d nickscan_production \
--     -v ON_ERROR_STOP=1 -f tools/migrations/gap2-backfill/backfill_suspicious_areas.sql
-- ─────────────────────────────────────────────────────────────────────────────

\timing on

BEGIN;

WITH candidates AS (
    SELECT
        d.id              AS decision_id,
        d.containernumber,
        d.reviewedby,
        d.reviewedat,
        d.threatcategoryid,
        d.revenueanomalycategoryid,
        d.tenant_id,
        d.suspiciousareas::jsonb AS areas
    FROM imageanalysisdecisions d
    WHERE d.suspiciousareas IS NOT NULL
      AND length(d.suspiciousareas) > 2
      AND NOT EXISTS (
          SELECT 1
          FROM containerannotations ca
          WHERE ca.imageanalysisdecisionid = d.id
            AND ca.isdeleted = false
      )
      AND (
          -- Defensive: only treat as JSON when it parses cleanly to an array.
          jsonb_typeof(d.suspiciousareas::jsonb) = 'array'
      )
),
boxes AS (
    SELECT
        c.decision_id,
        c.containernumber,
        c.reviewedby,
        c.reviewedat,
        c.threatcategoryid,
        c.revenueanomalycategoryid,
        c.tenant_id,
        rect
    FROM candidates c,
         LATERAL jsonb_array_elements(c.areas) AS rect
    WHERE jsonb_typeof(rect) = 'object'
),
parsed AS (
    SELECT
        b.decision_id,
        b.containernumber,
        b.reviewedby,
        b.reviewedat,
        b.threatcategoryid,
        b.revenueanomalycategoryid,
        b.tenant_id,
        -- Coalesce PascalCase first (existing prod data), then lowercase fallback.
        COALESCE((b.rect->>'X')::double precision,      (b.rect->>'x')::double precision)      AS x,
        COALESCE((b.rect->>'Y')::double precision,      (b.rect->>'y')::double precision)      AS y,
        COALESCE((b.rect->>'Width')::double precision,  (b.rect->>'width')::double precision)  AS w,
        COALESCE((b.rect->>'Height')::double precision, (b.rect->>'height')::double precision) AS h,
        COALESCE(b.rect->>'CreatedBy', b.rect->>'createdBy') AS rect_createdby,
        COALESCE(b.rect->>'Tag',       b.rect->>'tag')       AS rect_tag
    FROM boxes b
)
INSERT INTO containerannotations (
    containernumber,
    type,
    x1, y1, x2, y2,
    color,
    width,
    text,
    createdat,
    createdby,
    isdeleted,
    imageanalysisdecisionid,
    threatcategoryid,
    revenueanomalycategoryid,
    tenant_id
)
SELECT
    p.containernumber,
    'Rectangle',
    p.x,
    p.y,
    p.x + p.w,
    p.y + p.h,
    '#ff0000',
    2,
    NULLIF(p.rect_tag, ''),
    COALESCE(p.reviewedat, now()),
    COALESCE(NULLIF(p.rect_createdby, ''), NULLIF(p.reviewedby, ''), 'backfill'),
    false,
    p.decision_id,
    p.threatcategoryid,
    p.revenueanomalycategoryid,
    COALESCE(p.tenant_id, 1)
FROM parsed p
WHERE p.x IS NOT NULL
  AND p.y IS NOT NULL
  AND p.w IS NOT NULL
  AND p.h IS NOT NULL;

-- Report what we did.
SELECT
    (SELECT COUNT(DISTINCT imageanalysisdecisionid)
       FROM containerannotations
      WHERE imageanalysisdecisionid IS NOT NULL
        AND isdeleted = false) AS decisions_with_typed_annotations,
    (SELECT COUNT(*)
       FROM containerannotations
      WHERE imageanalysisdecisionid IS NOT NULL
        AND isdeleted = false) AS total_typed_annotations,
    (SELECT COUNT(*)
       FROM imageanalysisdecisions
      WHERE suspiciousareas IS NOT NULL
        AND length(suspiciousareas) > 2) AS decisions_with_json_areas;

COMMIT;

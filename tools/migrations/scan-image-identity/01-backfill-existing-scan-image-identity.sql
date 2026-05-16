\set ON_ERROR_STOP on

BEGIN;

SET LOCAL app.tenant_id = '1';

CREATE TEMP TABLE tmp_scan_identity_sources ON COMMIT DROP AS
SELECT
    'ASE'::varchar(32) AS scannertype,
    a.inspectionid::text AS scannernativeid,
    a.originalscanrecordid AS originalscanrecordid,
    a.containernumber AS sourcecontainerlabel,
    a.imagedisplayname AS imagedisplayname,
    CASE
        WHEN a.scanimage IS NULL THEN NULL::bigint
        ELSE octet_length(a.scanimage)::bigint
    END AS filesizebytes,
    a.scantime AS scantimeutc,
    NULL::varchar(1024) AS sourcepath,
    NULL::varchar(1024) AS localpath
FROM asescans a
WHERE NULLIF(btrim(a.containernumber), '') IS NOT NULL
  AND upper(btrim(a.containernumber)) <> 'UNKNOWN'
UNION ALL
SELECT
    'FS6000'::varchar(32) AS scannertype,
    f.id::text AS scannernativeid,
    f.originalscanrecordid AS originalscanrecordid,
    f.containernumber AS sourcecontainerlabel,
    f.picnumber AS imagedisplayname,
    NULL::bigint AS filesizebytes,
    f.scantime AS scantimeutc,
    f.filepath AS sourcepath,
    f.filepath AS localpath
FROM fs6000scans f
WHERE NULLIF(btrim(f.containernumber), '') IS NOT NULL
  AND upper(btrim(f.containernumber)) <> 'UNKNOWN'
  AND (COALESCE(f.hasimage, false) = true OR NULLIF(btrim(f.filepath), '') IS NOT NULL);

CREATE INDEX ix_tmp_scan_identity_sources_native
    ON tmp_scan_identity_sources (scannertype, scannernativeid);

INSERT INTO scanimageassets (
    id,
    originalscanrecordid,
    scannertype,
    scannernativeid,
    sourcecontainerlabel,
    assetkind,
    storagekind,
    sourcepath,
    localpath,
    imagedisplayname,
    filesizebytes,
    scantimeutc,
    createdatutc,
    updatedatutc
)
SELECT
    gen_random_uuid(),
    s.originalscanrecordid,
    s.scannertype,
    s.scannernativeid,
    s.sourcecontainerlabel,
    'source',
    'database',
    s.sourcepath,
    s.localpath,
    s.imagedisplayname,
    s.filesizebytes,
    s.scantimeutc,
    now(),
    now()
FROM tmp_scan_identity_sources s
WHERE NOT EXISTS (
    SELECT 1
    FROM scanimageassets existing
    WHERE existing.scannertype = s.scannertype
      AND existing.assetkind = 'source'
      AND (
            (s.originalscanrecordid IS NOT NULL AND existing.originalscanrecordid = s.originalscanrecordid)
         OR (NULLIF(s.scannernativeid, '') IS NOT NULL AND existing.scannernativeid = s.scannernativeid)
      )
);

CREATE TEMP TABLE tmp_scan_identity_assets ON COMMIT DROP AS
SELECT DISTINCT ON (s.scannertype, s.scannernativeid)
    s.scannertype,
    s.scannernativeid,
    s.originalscanrecordid,
    s.sourcecontainerlabel,
    s.scantimeutc,
    a.id AS scanimageassetid
FROM tmp_scan_identity_sources s
JOIN scanimageassets a
  ON a.scannertype = s.scannertype
 AND a.assetkind = 'source'
 AND (
        (s.originalscanrecordid IS NOT NULL AND a.originalscanrecordid = s.originalscanrecordid)
     OR (NULLIF(s.scannernativeid, '') IS NOT NULL AND a.scannernativeid = s.scannernativeid)
 )
ORDER BY
    s.scannertype,
    s.scannernativeid,
    CASE
        WHEN s.originalscanrecordid IS NOT NULL AND a.originalscanrecordid = s.originalscanrecordid THEN 0
        ELSE 1
    END,
    a.updatedatutc DESC;

CREATE INDEX ix_tmp_scan_identity_assets_asset
    ON tmp_scan_identity_assets (scanimageassetid);

CREATE TEMP TABLE tmp_scan_identity_tokens_raw ON COMMIT DROP AS
SELECT
    a.scanimageassetid,
    a.originalscanrecordid,
    a.scannertype,
    a.scannernativeid,
    a.sourcecontainerlabel,
    a.scantimeutc,
    token.ordinality,
    btrim(token.value) AS containernumber,
    upper(regexp_replace(btrim(token.value), '[^A-Za-z0-9]', '', 'g')) AS normalizedcontainernumber
FROM tmp_scan_identity_assets a
CROSS JOIN LATERAL regexp_split_to_table(COALESCE(a.sourcecontainerlabel, ''), ',')
    WITH ORDINALITY AS token(value, ordinality)
WHERE NULLIF(btrim(token.value), '') IS NOT NULL
  AND upper(regexp_replace(btrim(token.value), '[^A-Za-z0-9]', '', 'g')) <> 'UNKNOWN';

CREATE TEMP TABLE tmp_scan_identity_tokens ON COMMIT DROP AS
WITH deduped AS (
    SELECT DISTINCT ON (scanimageassetid, normalizedcontainernumber)
        *
    FROM tmp_scan_identity_tokens_raw
    WHERE NULLIF(normalizedcontainernumber, '') IS NOT NULL
    ORDER BY scanimageassetid, normalizedcontainernumber, ordinality
)
SELECT
    d.*,
    count(*) OVER (PARTITION BY d.scanimageassetid) AS tokencount,
    row_number() OVER (PARTITION BY d.scanimageassetid ORDER BY d.ordinality) AS tokenindex
FROM deduped d;

CREATE INDEX ix_tmp_scan_identity_tokens_norm
    ON tmp_scan_identity_tokens (normalizedcontainernumber);

INSERT INTO sourcescancontainerlinks (
    scanimageassetid,
    originalscanrecordid,
    scannertype,
    scannernativeid,
    containernumber,
    normalizedcontainernumber,
    sourcecontainerlabel,
    position,
    confidence,
    createdatutc,
    updatedatutc
)
SELECT
    t.scanimageassetid,
    t.originalscanrecordid,
    t.scannertype,
    t.scannernativeid,
    t.containernumber,
    t.normalizedcontainernumber,
    t.sourcecontainerlabel,
    CASE
        WHEN t.tokencount <= 1 THEN 'single'
        WHEN t.tokenindex = 1 THEN 'left'
        WHEN t.tokenindex = 2 THEN 'right'
        ELSE 'unknown'
    END,
    'backfill',
    now(),
    now()
FROM tmp_scan_identity_tokens t
ON CONFLICT (scanimageassetid, normalizedcontainernumber) DO UPDATE
SET
    originalscanrecordid = COALESCE(sourcescancontainerlinks.originalscanrecordid, EXCLUDED.originalscanrecordid),
    scannertype = COALESCE(NULLIF(sourcescancontainerlinks.scannertype, ''), EXCLUDED.scannertype),
    scannernativeid = COALESCE(NULLIF(sourcescancontainerlinks.scannernativeid, ''), EXCLUDED.scannernativeid),
    containernumber = COALESCE(NULLIF(sourcescancontainerlinks.containernumber, ''), EXCLUDED.containernumber),
    sourcecontainerlabel = COALESCE(NULLIF(sourcescancontainerlinks.sourcecontainerlabel, ''), EXCLUDED.sourcecontainerlabel),
    position = CASE
        WHEN sourcescancontainerlinks.position IS NULL OR sourcescancontainerlinks.position = 'unknown'
            THEN EXCLUDED.position
        ELSE sourcescancontainerlinks.position
    END,
    confidence = COALESCE(NULLIF(sourcescancontainerlinks.confidence, ''), EXCLUDED.confidence),
    updatedatutc = now();

WITH candidates AS (
    SELECT
        l.id AS linkid,
        r.id AS recordexpectedcontainerid,
        r.boedocumentid,
        row_number() OVER (
            PARTITION BY l.id
            ORDER BY
                CASE
                    WHEN r.scannertype IS NOT NULL AND upper(r.scannertype) = upper(l.scannertype) THEN 0
                    ELSE 1
                END,
                r.id DESC
        ) AS rn
    FROM sourcescancontainerlinks l
    JOIN recordexpectedcontainers r
      ON upper(regexp_replace(COALESCE(r.containernumber, ''), '[^A-Za-z0-9]', '', 'g')) = l.normalizedcontainernumber
    WHERE l.recordexpectedcontainerid IS NULL
      AND (r.scannertype IS NULL OR l.scannertype IS NULL OR upper(r.scannertype) = upper(l.scannertype))
)
UPDATE sourcescancontainerlinks l
SET
    recordexpectedcontainerid = c.recordexpectedcontainerid,
    boedocumentid = COALESCE(l.boedocumentid, c.boedocumentid),
    updatedatutc = now()
FROM candidates c
WHERE c.rn = 1
  AND l.id = c.linkid;

WITH candidates AS (
    SELECT
        r.id AS rowid,
        l.scanimageassetid,
        l.originalscanrecordid,
        l.sourcecontainerlabel,
        row_number() OVER (
            PARTITION BY r.id
            ORDER BY
                CASE
                    WHEN r.scannertype IS NOT NULL AND upper(r.scannertype) = upper(l.scannertype) THEN 0
                    ELSE 1
                END,
                a.scantimeutc DESC NULLS LAST,
                a.updatedatutc DESC
        ) AS rn
    FROM recordexpectedcontainers r
    JOIN sourcescancontainerlinks l
      ON upper(regexp_replace(COALESCE(r.containernumber, ''), '[^A-Za-z0-9]', '', 'g')) = l.normalizedcontainernumber
    JOIN scanimageassets a
      ON a.id = l.scanimageassetid
    WHERE r.scanimageassetid IS NULL
      AND (r.scannertype IS NULL OR l.scannertype IS NULL OR upper(r.scannertype) = upper(l.scannertype))
)
UPDATE recordexpectedcontainers r
SET
    scanimageassetid = c.scanimageassetid,
    originalscanrecordid = COALESCE(r.originalscanrecordid, c.originalscanrecordid),
    sourcecontainerlabel = COALESCE(NULLIF(r.sourcecontainerlabel, ''), c.sourcecontainerlabel)
FROM candidates c
WHERE c.rn = 1
  AND r.id = c.rowid;

WITH candidates AS (
    SELECT
        ar.id AS rowid,
        l.scanimageassetid,
        l.originalscanrecordid,
        l.sourcecontainerlabel,
        row_number() OVER (
            PARTITION BY ar.id
            ORDER BY
                CASE
                    WHEN ar.scannertype IS NOT NULL AND upper(ar.scannertype) = upper(l.scannertype) THEN 0
                    ELSE 1
                END,
                a.scantimeutc DESC NULLS LAST,
                a.updatedatutc DESC
        ) AS rn
    FROM analysisrecords ar
    JOIN sourcescancontainerlinks l
      ON upper(regexp_replace(COALESCE(ar.containernumber, ''), '[^A-Za-z0-9]', '', 'g')) = l.normalizedcontainernumber
    JOIN scanimageassets a
      ON a.id = l.scanimageassetid
    WHERE ar.scanimageassetid IS NULL
      AND (ar.scannertype IS NULL OR l.scannertype IS NULL OR upper(ar.scannertype) = upper(l.scannertype))
)
UPDATE analysisrecords ar
SET
    scanimageassetid = c.scanimageassetid,
    originalscanrecordid = COALESCE(ar.originalscanrecordid, c.originalscanrecordid),
    sourcecontainerlabel = COALESCE(NULLIF(ar.sourcecontainerlabel, ''), c.sourcecontainerlabel)
FROM candidates c
WHERE c.rn = 1
  AND ar.id = c.rowid;

WITH candidates AS (
    SELECT
        ccs.id AS rowid,
        l.scanimageassetid,
        l.originalscanrecordid,
        l.sourcecontainerlabel,
        row_number() OVER (
            PARTITION BY ccs.id
            ORDER BY
                CASE
                    WHEN ccs.scannertype IS NOT NULL AND upper(ccs.scannertype) = upper(l.scannertype) THEN 0
                    ELSE 1
                END,
                a.scantimeutc DESC NULLS LAST,
                a.updatedatutc DESC
        ) AS rn
    FROM containercompletenessstatuses ccs
    JOIN sourcescancontainerlinks l
      ON upper(regexp_replace(COALESCE(ccs.containernumber, ''), '[^A-Za-z0-9]', '', 'g')) = l.normalizedcontainernumber
    JOIN scanimageassets a
      ON a.id = l.scanimageassetid
    WHERE ccs.scanimageassetid IS NULL
      AND (ccs.scannertype IS NULL OR l.scannertype IS NULL OR upper(ccs.scannertype) = upper(l.scannertype))
)
UPDATE containercompletenessstatuses ccs
SET
    scanimageassetid = c.scanimageassetid,
    originalscanrecordid = COALESCE(ccs.originalscanrecordid, c.originalscanrecordid),
    sourcecontainerlabel = COALESCE(NULLIF(ccs.sourcecontainerlabel, ''), c.sourcecontainerlabel)
FROM candidates c
WHERE c.rn = 1
  AND ccs.id = c.rowid;

WITH candidates AS (
    SELECT
        q.id AS rowid,
        l.scanimageassetid,
        l.originalscanrecordid,
        l.sourcecontainerlabel,
        l.position,
        row_number() OVER (
            PARTITION BY q.id
            ORDER BY
                CASE
                    WHEN q.scannertype IS NOT NULL AND upper(q.scannertype) = upper(l.scannertype) THEN 0
                    ELSE 1
                END,
                a.scantimeutc DESC NULLS LAST,
                a.updatedatutc DESC
        ) AS rn
    FROM containerscanqueues q
    JOIN sourcescancontainerlinks l
      ON upper(regexp_replace(COALESCE(q.containernumber, ''), '[^A-Za-z0-9]', '', 'g')) = l.normalizedcontainernumber
    JOIN scanimageassets a
      ON a.id = l.scanimageassetid
    WHERE q.scanimageassetid IS NULL
      AND (q.scannertype IS NULL OR l.scannertype IS NULL OR upper(q.scannertype) = upper(l.scannertype))
)
UPDATE containerscanqueues q
SET
    scanimageassetid = c.scanimageassetid,
    originalscanrecordid = COALESCE(q.originalscanrecordid, c.originalscanrecordid),
    sourcecontainerlabel = COALESCE(NULLIF(q.sourcecontainerlabel, ''), c.sourcecontainerlabel),
    scancontainerposition = COALESCE(NULLIF(q.scancontainerposition, ''), c.position)
FROM candidates c
WHERE c.rn = 1
  AND q.id = c.rowid;

WITH candidates AS (
    SELECT
        iq.id AS rowid,
        l.scanimageassetid,
        l.originalscanrecordid,
        l.sourcecontainerlabel,
        row_number() OVER (
            PARTITION BY iq.id
            ORDER BY
                CASE
                    WHEN iq.scannertype IS NOT NULL AND upper(iq.scannertype) = upper(l.scannertype) THEN 0
                    ELSE 1
                END,
                a.scantimeutc DESC NULLS LAST,
                a.updatedatutc DESC
        ) AS rn
    FROM icumssubmissionqueues iq
    JOIN sourcescancontainerlinks l
      ON upper(regexp_replace(COALESCE(iq.containernumber, ''), '[^A-Za-z0-9]', '', 'g')) = l.normalizedcontainernumber
    JOIN scanimageassets a
      ON a.id = l.scanimageassetid
    WHERE iq.scanimageassetid IS NULL
      AND (iq.scannertype IS NULL OR l.scannertype IS NULL OR upper(iq.scannertype) = upper(l.scannertype))
)
UPDATE icumssubmissionqueues iq
SET
    scanimageassetid = c.scanimageassetid,
    originalscanrecordid = COALESCE(iq.originalscanrecordid, c.originalscanrecordid),
    sourcecontainerlabel = COALESCE(NULLIF(iq.sourcecontainerlabel, ''), c.sourcecontainerlabel)
FROM candidates c
WHERE c.rn = 1
  AND iq.id = c.rowid;

SELECT 'scanimageassets' AS table_name, count(*) AS row_count FROM scanimageassets
UNION ALL
SELECT 'sourcescancontainerlinks', count(*) FROM sourcescancontainerlinks
UNION ALL
SELECT 'linked_recordexpectedcontainers', count(*) FROM recordexpectedcontainers WHERE scanimageassetid IS NOT NULL
UNION ALL
SELECT 'linked_analysisrecords', count(*) FROM analysisrecords WHERE scanimageassetid IS NOT NULL
UNION ALL
SELECT 'linked_containercompletenessstatuses', count(*) FROM containercompletenessstatuses WHERE scanimageassetid IS NOT NULL
UNION ALL
SELECT 'linked_containerscanqueues', count(*) FROM containerscanqueues WHERE scanimageassetid IS NOT NULL
UNION ALL
SELECT 'linked_icumssubmissionqueues', count(*) FROM icumssubmissionqueues WHERE scanimageassetid IS NOT NULL
ORDER BY table_name;

COMMIT;

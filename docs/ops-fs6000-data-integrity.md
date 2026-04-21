# FS6000 Data Integrity — Ops Runbook

**Scope:** three data-integrity items surfaced during the v2.14.0 viewer
sign-off, plus the SQL monitoring queries ops can run to spot recurrence.

Last reviewed: 2026-04-21 (post-arc).

---

## Quick status dashboard

Paste into psql against `nickscan_production` for a one-shot health check:

```sql
-- FS6000 raw-channel coverage by state
with raw_state as (
  select s.id, s.scantime,
         bool_or(i.imagetype = 'HighEnergy') as has_high,
         bool_or(i.imagetype = 'LowEnergy')  as has_low,
         bool_or(i.imagetype = 'Material')   as has_mat
  from fs6000scans s
  left join fs6000images i on i.scanid = s.id
  group by s.id, s.scantime
)
select
  case
    when has_high and has_low and has_mat     then 'full (9 modes)'
    when has_high and has_low and not has_mat then 'partial HE+LE (6 modes via v2.14.0)'
    when has_high or has_low or has_mat       then 'degraded (1-2 channels)'
    else 'empty (vendor-jpeg-only)'
  end as state,
  count(*) as scans,
  min(scantime)::date as oldest,
  max(scantime)::date as newest
from raw_state
group by 1 order by 1;
```

Expected healthy mix as of 2026-04-21:
- `full (9 modes)` — growing daily, all recent full-channel scans
- `partial HE+LE (6 modes via v2.14.0)` — small residual, see ticket 2 below
- `degraded (1-2 channels)` — should trend to zero after v2.14.1 ingest validation
- `empty (vendor-jpeg-only)` — flat at 1,036 (historical data loss, permanent)

---

## Ticket 1 — Pre-April 4 Archive retention gap

**Status:** historical, already fixed in current code. No action possible
for the lost data.

**What happened:** scans between 2026-03-18 and 2026-04-03 (1,036 total)
have only `.jpg` + `.xml` in `Data\FS6000\Archive\<date>\<n>\` — the raw
`.img` files were never copied to Archive. Investigation found that
`Services.FS6000/FileSyncService.cs` was replaced on 2026-04-19 with a
version that copies all `.img` files in addition to XML + JPG. The prior
version (preserved as `FileSyncService.cs.backup`, 360 lines, Sep 2025)
only copied XML + JPG. Between the two versions being installed and the
scans ingested pre-April 4, the `.img` files were archived to Archive
without being copied anywhere, or were deleted during a retention sweep.

**Current state:** `FileSyncService.cs` copies `*.img` at line 418. The
downstream `IngestionService` moves the whole Staging folder into
Archive (not just select files), so `.img` files end up in Archive. The
fix is already running. New scans from 2026-04-04 onwards have complete
archives.

**Action:** delete `FileSyncService.cs.backup` + `IngestionService.cs.backup` +
`IngestionService.cs.backup2` during the next housekeeping pass — they
give false signal that an older shape is current. Otherwise nothing to do.

---

## Ticket 2 — Scanner not always emitting `material.img`

**Status:** ongoing. Scanner-side. Partial relief shipped in v2.14.0.

**What happened:** ~15% of recent FS6000 scans have `high.img` + `low.img`
in Archive but no `material.img`. The scanner itself isn't producing the
material-classification file for those scans. Ingest doesn't invent it,
so `fs6000images` stores only 2 of the 3 channels.

**Relief (v2.14.0):** affected scans now decode as `fs6000-v1-no-material`
and get the 5-mode subset (`bw / inverse / high-pen / low-pen / diff`).
They are no longer view-only.

**Possible root causes** (need ops/hardware investigation):
- Scanner firmware bug — material classification step fails silently
- Disk / network write issue on the scanner's local drive
- Specific truck speeds or container types that confuse the Z-eff stage

**Monitoring query — share with scanner vendor / ops:**

```sql
-- Per-day material.img miss rate, last 14 days
with daily as (
  select s.scantime::date as day,
         count(*) as total,
         count(*) filter (
           where exists (select 1 from fs6000images i where i.scanid=s.id and i.imagetype='HighEnergy')
             and exists (select 1 from fs6000images i where i.scanid=s.id and i.imagetype='LowEnergy')
             and not exists (select 1 from fs6000images i where i.scanid=s.id and i.imagetype='Material')
         ) as missing_material,
         count(*) filter (
           where exists (select 1 from fs6000images i where i.scanid=s.id and i.imagetype='HighEnergy')
             and exists (select 1 from fs6000images i where i.scanid=s.id and i.imagetype='LowEnergy')
             and exists (select 1 from fs6000images i where i.scanid=s.id and i.imagetype='Material')
         ) as full_triad
  from fs6000scans s
  where s.scantime > now() - interval '14 days'
  group by s.scantime::date
)
select day,
       total,
       full_triad,
       missing_material,
       round(100.0 * missing_material / nullif(total, 0), 1) as miss_pct
from daily
order by day desc;
```

**Trend interpretation:**
- Stable 10-15% → scanner has a persistent intermittent bug; escalate to vendor
- Rising trend → recent change on the scanner side; check for firmware/config drift
- Falling toward 0 → fix deployed upstream; watch until stable then archive this ticket

---

## Ticket 3 — Truncated `.img` blobs (one-off + ingest-validation fix)

**Status:** prevention shipped in v2.14.1. Past data can't be recovered.

**What happened:** 22 scans in the 2026-04-13 → 2026-04-20 window had
`LowEnergy` blobs truncated in both Archive and DB. HE blobs were full
(~10 MB), LE blobs were round-number partial sizes (2 MB, 1 MB, 640 KB,
128 KB, …) — classic interrupted-write signature. All 22 are also
missing Material, suggesting the scanner's write pipeline crashed partway
through the 3-channel write sequence.

Investigation confirmed the Archive files themselves are truncated — the
FileSyncService + archiver faithfully copied the partial files. Decode
fails downstream with "channel truncated" and the scan shows as
`vendor-jpeg-only (missing: Material)` to operators. Technically usable
but with no mode toolbar.

**Prevention (v2.14.1):** `FS6000RawChannelIngester.IsHeaderConsistent`
parses the 36-byte FS6000 header, computes expected `Width × Height ×
(BitDepth/8) + header` bytes, and rejects files where the actual byte
count is short. The rejected channel is NOT stored to DB; the ingester
logs a warning and the backfill worker retries on the next cycle (so if
the scanner finishes writing later, we'll pick up the complete file).

After v2.14.1 deployment, truncated `.img` files:
- Don't pollute `fs6000images` (no partial LE blobs in DB)
- Log a clear `[FS6000-RAW] Rejecting truncated/inconsistent {ImageType} ...` warning
- Get re-attempted by the backfill worker every 5 min for up to 7 days

**Monitoring query — truncated blobs in the DB:**

```sql
-- Scans where HE and LE blob sizes differ by more than 1 MB.
-- Pre-v2.14.1 data will still appear; post-v2.14.1 should show zero new rows.
with blob_sizes as (
  select s.id, s.containernumber, s.scantime::date as scan_date,
         max(case when i.imagetype='HighEnergy' then length(i.imagedata) end) as he_bytes,
         max(case when i.imagetype='LowEnergy'  then length(i.imagedata) end) as le_bytes
  from fs6000scans s
  join fs6000images i on i.scanid = s.id
  group by s.id, s.containernumber, s.scantime
)
select scan_date, count(*) as truncated_scans
from blob_sizes
where he_bytes is not null
  and le_bytes is not null
  and abs(he_bytes - le_bytes) > 1000000
group by scan_date
order by scan_date desc;
```

**Cleanup note:** the 22 pre-v2.14.1 scans with truncated LE blobs in
the DB remain as-is. The viewer handles them gracefully (falls to
vendor-jpeg-only). Removing the bad rows is optional — they don't
affect anything that works today. If a future job wants to re-attempt
ingest, deleting the LE rows and clearing the partial state would let
the backfill worker retry from Archive — but since Archive also has
the truncated file, the v2.14.1 validator would reject it on re-attempt.

---

## Related code

| Layer | File | Role |
|---|---|---|
| File sync (source → Staging) | `Services.FS6000/FileSyncService.cs` | Watches scanner network share, copies XML + JPG + all `.img` files |
| Ingest (Staging → DB) | `Services.FS6000/IngestionService.cs` + `Services.ImageProcessing/FS6000/FS6000RawChannelIngester.cs` | Writes to `fs6000scans` + `fs6000images`, validates header consistency (v2.14.1) |
| Archival (Staging → Archive) | `IngestionService` (same file) | `Directory.Move` whole folder — preserves all contents |
| Backfill worker | `Services.ImageProcessing/FS6000/FS6000RawChannelBackfillWorker.cs` | 5-min cycle, 7-day lookback, retries scans with missing channels |
| Manual backfill endpoint | `API/Controllers/ImageProcessingController.cs` — `POST /api/imageprocessing/backfill/fs6000-raw-channels` | Admin-only; runs for an arbitrary date range, ignores the 7-day limit |

## Retention policy reminder

- `Data/FS6000/Staging/*` — 7-day TTL (aging from this cleans up live files)
- `Data/FS6000/Archive/*` — permanent, ops responsibility
- `Data/FS6000/IngestWorkspace/*` — transient (seconds), auto-cleaned in finally-blocks
- `fs6000images.imagedata` — permanent in DB (no age-out); binary blobs grow ~25 MB per scan when full triad

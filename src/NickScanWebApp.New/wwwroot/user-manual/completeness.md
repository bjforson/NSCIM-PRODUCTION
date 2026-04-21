---
title: "Completeness — what 'ready' means"
category: "Workflow"
order: 35
requires: [Pages.ContainerCompleteness, Pages.ImageAnalysisManagement]
updated: 2026-04-21
version: v2.15.0
---

# Container completeness

A container can't enter Workbench until it's **complete**. Three independent data signals have to be present:

1. **Scanner data** — the raw scan + images are in the system.
2. **ICUMS declaration** — the Bill of Entry (BoE) has been pulled from ICUMS.
3. **Vehicle match** — the scanner-read plate lines up with the declaration.

When all three are green, the container becomes eligible for analyst review. Any one missing → the container sits in "awaiting" state until it catches up.

## Why these three?

- **Scanner data** without declaration: you'd be looking at an image with no context. Scanners see many containers that never actually enter the country (trans-shipment, empty moves, returns). Without the declaration there's nothing to compare against.
- **Declaration without scanner data**: customs wants to clear it, but we can't screen what we can't see.
- **Without a vehicle match**: we don't know which scan belongs to which declaration. Ambiguous → can't route.

## The Completeness Records page

Open: **Operations → Completeness Records** (URL: `/completeness`).

The page shows containers that are near-ready but stuck because one signal is missing. Columns:

| Column | Meaning |
|---|---|
| Container # | The container number (from plate OCR). |
| BL | Bill of Lading (if declaration received). |
| Scanner ✓ | Green if scan exists, red if missing. |
| ICUMS ✓ | Green if BoE exists, red if missing. |
| Match ✓ | Green if plate ↔ BoE linkage resolved. |
| Age | How long it's been waiting. |
| Action | "Investigate" button for rows with a red column. |

Filter bar at top lets you focus on one missing signal (e.g., "show me everything waiting on ICUMS").

## Common stuck reasons

### Scanner ✓ missing
- Scanner service not reporting (check Scanners page).
- Scanner scanned but the file never got ingested (check IngestionService logs).
- Scan is `vendor-jpeg-only` because raw files were truncated at ingest — the container *is* complete for a degraded review. See [Variant Labels](/help/variant-labels).

### ICUMS ✓ missing
- BoE not yet submitted to ICUMS by the clearing agent — not our problem; clock is on them.
- ICUMS sync is behind — check the ICUMS Dashboard. If connectivity is down, a whole batch can stall.
- The BoE was submitted but references a container number we haven't scanned yet.

### Match ✓ missing
- Plate OCR misread the container number → scanner record has wrong container #.
- ICUMS declaration has a typo in the container number.
- Two candidate BoEs both match this scan → routed to [Cross-Record Scans](/help/cross-record-scans) instead.

## How long containers wait

Typical timing at steady state:

- **Scanner → Complete**: median 12 min, 95th percentile 45 min.
- **Declaration → Complete**: median 6 h, 95th percentile 24 h (clearing agents often submit in batches).
- **Stuck containers**: anything >48h typically has an ops issue. The Alerts panel at top of the page highlights these.

## Resolving stuck containers

### "Match" problems (the most common operator-resolvable case)

Click **Investigate** on the row → a dialog opens showing candidate BoEs for that scan. Usually one is obviously right. Pick it → match written → container completes.

If no candidate looks right, escalate to ops — the BoE might not be in our system yet, or the plate read was wrong.

### "Scanner" problems
Not typically operator-resolvable. Log to ops.

### "ICUMS" problems
Wait. Or if urgent, use [ICUMS BoE Request](/help/icums-boe-request) to manually pull a specific BoE.

## The Records tab vs the Pending-Export tab

The page has multiple tabs:

- **Records** — containers ready or recently complete.
- **Queue / Pending** — containers still stuck (the stuck-reasons surface above).
- **Cross-Record** — 2+ candidate matches; see [Cross-Record Scans](/help/cross-record-scans).
- **Completed** — historical complete records (for lookup).
- **Cargo Groups** — higher-level grouping (BL aggregation).

Each tab has its own filter bar + action context.

## Metrics at the top

Four cards:
- **Total Records** — everything the page is tracking right now.
- **Fully Complete** — all 3 signals green, ready for Workbench.
- **Missing ICUMS** — count waiting on BoE.
- **Missing Scanner** — count waiting on scan (rare — usually you have the scan first).

<!-- requires: Pages.ImageAnalysisManagement -->
## Manager's signal

If "Missing ICUMS" grows over 200 and isn't dropping, something's wrong at the ICUMS boundary — check the [ICUMS Dashboard](/help/icums-overview). If "Fully Complete" grows over 50, analysts are backed up — add capacity in Workbench.
<!-- /requires -->

---

## What to read next

- [ICUMS Overview](/help/icums-overview) — to diagnose missing-BoE stalls
- [Variant Labels](/help/variant-labels) — to understand vendor-JPEG-only completeness
- [Cross-Record Scans](/help/cross-record-scans) — for ambiguous matches

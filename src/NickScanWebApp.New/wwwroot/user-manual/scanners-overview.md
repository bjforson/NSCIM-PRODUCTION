---
title: "Scanners page — the three scanner tabs"
category: "Monitoring"
order: 20
requires: [Pages.ScannersView, Pages.ImageAnalysisView, Pages.ImageAnalysisManagement]
updated: 2026-04-21
version: v2.15.0
---

# Scanners page

The Scanners page is the **live window into every scanner** feeding NSCIS. You use it to:

- Confirm scanners are online and producing scans.
- Find a specific recent scan by time + scanner.
- Launch the image viewer directly from a scanner's feed (without going through Workbench).

Open: **Operations → Scanners** (URL: `/operations/scanners` or `/scanners`).

## Tab structure

One tab per scanner family:

| Tab | Scanner | What makes it different |
|---|---|---|
| **FS6000** | Nuctech FS6000 | Full raw channels (HE + LE + Material) |
| **ASE** | Nuctech ASE | Tri-panel or single-view variants |
| **Heimann Smith** | Smith Heimann | Vendor-JPEG-only (no raw channels at this site) |

Inside each tab: left sidebar listing the scanners in that family (one row per physical scanner), right pane showing recent scans for the selected scanner.

## Per-scanner row

Shows:

- Scanner name + site/gate identifier.
- Online chip (green/red).
- Scans today count.
- Last scan timestamp.
- Variant (see [Variant Labels](/help/variant-labels)) — useful confirmation the scanner is producing the expected file set.

Click a row → the right pane populates with a grid of recent scans.

## Recent scans grid

Thumbnails of the latest ~50 scans. Each tile:

- Thumbnail (vendor JPEG).
- Container number (from plate OCR).
- Timestamp.
- Variant label.
- Status chip (Ready / InReview / DecisionMade / Audited / Submitted).

Click any tile → a small preview dialog opens with the vendor JPEG. Click **View Fullscreen** in that dialog → the full viewer ([Viewer Basics](/help/viewer-basics)).

## When a scanner looks offline

Red chip + zero scans today + old last-scan timestamp = scanner is down or disconnected.

Steps:
1. Check **Services Monitoring** → is the ingestion service healthy?
2. Check network to the scanner (ping the scanner's IP from the ops console).
3. Check the scanner itself — physical machine may need attention.
4. Check the file-share mount — some scanners write to a shared folder; if the share is down, new files can't be seen.

## What you can and can't do from this page

- ✅ View scans
- ✅ Launch the image viewer fullscreen
- ✅ See per-scanner status
- ❌ Start/stop a scanner (that's on the scanner console itself)
- ❌ Delete / re-ingest scans (ops task via DB/scripts)
- ❌ Change scanner settings (those live in **Admin → System Settings → Scanners**)

## Why scanners may show partial-channel

A scanner is expected to emit a full file bundle (main JPEG + HE.img + LE.img + Mat.img for FS6000/ASE). If any is missing:

- Variant label becomes `fs6000-v1-no-material`, `ase-single-view`, or `vendor-jpeg-only`.
- The scan is still usable (fewer render modes available).
- Tends to indicate a scanner configuration issue — ops should investigate. Historically this represented ~1% of scans; after v2.14.1 ingest validation rejects truncated files, so it should be rare going forward.

See [Variant Labels](/help/variant-labels) for what each variant means for the viewer.

## Filters

Top of each tab:

- **Date range** — today / this week / custom.
- **Status** — Ready / InReview / Complete / etc.
- **Search** — container number.
- **Variant** — filter to only partial-channel scans (useful for ops triage).

## Performance

- Thumbnail loads: lazy, 100–300 ms each.
- Full viewer open: 1–3 s (see [Viewer Basics](/help/viewer-basics) performance section).
- Polling for new scans: 10 s auto-refresh.

---

## What to read next

- [Viewer Basics](/help/viewer-basics) — what opens when you click a scan
- [Variant Labels](/help/variant-labels) — decoding per-scanner variant chips
- [Services Monitoring](/help/services-monitoring) — if a scanner is red, check ingestion here

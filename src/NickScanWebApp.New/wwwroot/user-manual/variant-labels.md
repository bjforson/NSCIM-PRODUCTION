---
title: "Variant labels explained"
category: "Viewer"
order: 70
requires: [Pages.ImageAnalysisView, Pages.ImageAnalysisAudit, Pages.ImageAnalysisManagement]
updated: 2026-04-21
version: v2.11.0
---

# Variant labels

At the right edge of the RENDER MODE row you'll see a small italicised label like `fs6000-v1` or `ase-single-view`. That's the **variant** of the scan — a technical tag telling you what kind of raw data the viewer is working with. The number of mode chips changes with the variant.

## The live variants

| Label | What it means | Mode chips you'll see |
|---|---|---|
| `fs6000-v1` | Full FS6000 scan — high energy + low energy + material classification all present | All 9 |
| `fs6000-v1-no-material` | FS6000 scan missing the material classification file (scanner didn't emit it) | 6 modes (no Composite / Organic / Metal) |
| `ase-tri-panel` | Nuctech ASE scan with three energy planes (dual energy + material) | All 9 |
| `ase-single-view` | Nuctech ASE scan with a single energy channel | 3 modes (B/W, Inverse, Edge) |
| `vendor-jpeg-only (missing: X)` | Scan has incomplete raw channels — cannot run mode catalogue | None (toolbar hidden; falls back to vendor JPEG) |

## Why the variant matters to you

The modes each need certain channels to exist:

- **Composite / Organic-strip / Metal-strip** need HE + LE + Material. They won't appear for `fs6000-v1-no-material` or `ase-single-view`.
- **High Pen / Low Pen / Diff** need HE + LE. They work on everything except single-view ASE.
- **B/W / Inverse / Edge** need any single channel. They work on everything with at least one energy.

If you're looking at a scan and expecting a particular mode but it's missing from the toolbar, check the variant label — it'll tell you why.

## If the variant says `vendor-jpeg-only`

The scan exists but the raw `.img` files needed for mode rendering aren't in the system. This happens for:
- **Historical scans** from before 2026-04-04 (the archiver wasn't copying raw files back then; those ~1,000 scans are permanent vendor-JPEG-only).
- **Future scans** where the scanner's write sequence failed partway (interrupted write; the data integrity validator added in v2.14.1 now rejects truncated files at ingest so these should become rare).

In both cases you still have the vendor-rendered Main JPEG, and all the canvas tools (zoom, pan, rotate, draw, ruler, magnifier, enhance sliders) work. Just no mode catalogue / window-level / pixel probe / raw 16-bit / ROI panel for those scans.

## Admin / ops note

The variant label is the same information the `/api/ImageProcessing/container/{id}/mode-capabilities` endpoint returns. If something's misbehaving, that endpoint is the first thing to curl during an incident.

For ops-side investigation of why a scan is `vendor-jpeg-only`, see `docs/ops-fs6000-data-integrity.md` in the repo (outside the WebApp — check with your admin).

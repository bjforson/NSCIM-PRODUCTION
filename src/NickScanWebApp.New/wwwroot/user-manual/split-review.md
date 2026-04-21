---
title: "Split Review — two containers in one scan"
category: "Workflow"
order: 50
requires: [Pages.CrossRecordScans, Pages.ImageAnalysisManagement]
updated: 2026-04-21
version: v2.15.0
---

# Image Split Review

Sometimes a single scan image contains **two containers travelling together** — typically two 20-foot containers loaded on a single trailer, captured in one scanner pass. These are called **cross-record scans**, and they need to be split in software before they can be reviewed as individual containers.

Open it from the sidebar: **Image Analysis → Split Review** (URL: `/operations/image-split-review`).

## Why splits happen

- **Stacked 20ft containers on a 40ft chassis.** Most common case — two 20s side-by-side or stacked, one ICUMS declaration per container, but one scanner image.
- **Weighbridge co-transit.** A tractor-trailer carries two independently-declared containers through the scanner in one pass.
- **Scanner error.** Occasionally the scanner fires once across two separate trucks caught mid-frame. Rare but happens — always needs split review.

## How the auto-splitter works

A background service (the **Image Splitter**, port 5320) examines every scan and looks for:

- Two independent container numbers visible in the vehicle plate region.
- A visible gap / boundary between cargo masses in the X-ray data.
- Two separate BoE declarations that could plausibly match the two halves.

When those three conditions align, the scan is flagged as a **split job** and comes to this queue. The service's best-guess split point (a vertical line x-coordinate) is pre-calculated. You review + approve it.

## The page layout

**Top**: Service health chip. If it says "Splitter Offline" the review list won't populate — check ops.

**Stats cards**: Total jobs / Completed / Pending / Processing.

**Main list**: one card per split job with:
- The full scanner image.
- A draggable red dividing line at the auto-detected split X.
- The two container numbers (as read from the plate area).
- Container-A preview (left half) and Container-B preview (right half).
- **Approve Split** and **Manually Adjust** buttons.

## Your workflow

For each job:

1. **Look at the whole image.** Do the two halves look like independent cargoes? Is there a clear boundary?
2. **Check the proposed split line.** Dense-metal cargo or cluttered shipments can confuse the auto-detector. The line should fall in the visible gap between the two containers, not across either one.
3. **Drag to adjust** if needed. The previews update live.
4. **Approve** when the split looks correct. The two halves become two independent scan records, and they enter Completeness / Workbench as normal.

## When to reject

The **Reject** option (if your role allows) marks the job as "not actually a split" — useful when:
- The splitter mis-flagged a single-container scan as split.
- The image is too corrupt to split reliably (send it for re-scan instead).

Rejected jobs go to an ops queue for manual investigation — they don't automatically become single-container records.

## Performance

- Loading the job list: 200–500 ms (typically <50 jobs at a time).
- Rendering a split preview with live-drag: <30 ms per re-render (client-side canvas).
- Approving a split: 1–3 s (backend re-slices the raw channels and writes the two new scan records).

## Service dependencies

- Splitter service running on port 5320 (check the health chip at the top).
- Image Processing service reachable (it's what performs the actual slice).
- Enough disk space in the scan archive for two derived scans per split (roughly 2× the source scan size).

## When the queue is empty

This is normal — most days only see a handful of splits. If you've been shown empty for more than a day, that could mean:
- No qualifying scans (low traffic day).
- The splitter service is down but not flagging it in the health chip — ops should check the service log.

<!-- requires: Pages.ImageAnalysisManagement -->
## Manager notes

The **Cross-Record Scans** permission (`pages.crossrecordscans.view`) is usually assigned to senior analysts only. Splitting affects how two containers appear for downstream review — it's not a routine operator task. Keep the assignment tight.

For ops-side diagnostics on splitter false-positives, see the Splitter service logs — they log every split decision with the confidence score and the detected container-number OCR reads.
<!-- /requires -->

---

## What to read next

- [Cross-Record Scans](/help/cross-record-scans) — the broader category this surfaces from
- [Viewer Basics](/help/viewer-basics) — once a split is approved, each half behaves like any other scan

---
title: "Viewer Basics — opening, panning, zooming"
category: "Viewer"
order: 10
requires: [Pages.ImageAnalysisView, Pages.ImageAnalysisAudit, Pages.ImageAnalysisManagement]
updated: 2026-04-21
version: v2.10.3
---

# Image Analysis Viewer — the basics

The viewer opens full-screen when you click a scanner image. Everything you can do with the image — zoom, rotate, mark, inspect — happens here.

## Opening a scan

There are three ways in:

1. **Scanners page** (sidebar → Operations & Monitoring → Scanners) → pick a scanner tab (FS6000 / ASE / Heimann Smith) → click the eye icon next to a container → click the image preview → **View Fullscreen**.
2. **Workbench** (sidebar → Image Analysis → Workbench) → your assigned containers → click to open.
3. **Container Details** (global Containers page → click any container → Images tab) → click for fullscreen.

The viewer that opens is the **same component** in all three paths, so the capabilities below work identically regardless of how you got there.

## The top toolbar

A horizontal strip across the top of the image. Left to right, grouped:

| Group | Icons | What they do |
|---|---|---|
| **Zoom** | magnifier +/−, 100%, center, fit | Standard zoom controls. Mouse-wheel over the image also zooms. |
| **Rotate** | ↺ 0° ↻ | Rotate ±90°. 0° button resets. |
| **Enhance** | tune slider icon | Client-side CSS brightness/contrast/saturation/hue/blur. Affects display only. |
| **Filter chips** | G / Inv / Edge / Sepia | Quick client-side CSS filters (grayscale, invert, edge, sepia). |
| **Canvas tools** | layers icon | Convolution filters, pseudocolor maps, histogram equalisation, W/L drag, loupe. |
| **Magnifier** | 🔍 | Always-on hover magnifier. |
| **Pixel Probe** | eyedropper | See [Pixel Probe](/help/pixel-probe). |
| **Ruler / Draw / Vis** | misc | Measure distances, mark rectangles, toggle annotation visibility. |

## The RENDER MODE row

Below the top toolbar. Scanner-aware — only shows modes your scan supports:

- **FS6000** with full raw channels → Default + 9 mode chips + **Raw 16-bit** toggle.
- **ASE tri-panel** → same 9-mode set.
- **ASE single-view** → Default + 3 modes (B/W, Inverse, Edge).
- **FS6000 partial-channel** (no material) → Default + 6 modes (B/W, Inverse, High Pen, Low Pen, Edge, Diff).
- **Scan with missing HE or LE** → Row hidden entirely; viewer falls back to the vendor JPEG.

To the right of the chips: **LEVEL / WINDOW** sliders (see [Window & Level](/help/window-level)), **Raw 16-bit** toggle (see [Raw 16-bit viewer](/help/raw-16bit)), and the **variant label** (e.g. `fs6000-v1`, `ase-single-view`) — that's the scan's decoded variant, useful when troubleshooting.

## Panning & zooming

- Drag with mouse to pan (when zoomed in).
- Mouse-wheel to zoom in/out around the cursor.
- `+` / `-` on keyboard also zoom.
- `R` rotates. `G` toggles grayscale. `I` toggles invert. `D` toggles draw mode.

## Closing without losing work

Top-right **×** closes the viewer. Any decision or rectangle changes auto-save as you make them — you don't need to click a separate save button.

---

## What to read next

- [Render Mode toolbar](/help/mode-toolbar) — what each of the 9 modes shows
- [Window & Level](/help/window-level) — adjusting contrast for dense cargo
- [Pixel Probe](/help/pixel-probe) — reading per-pixel values

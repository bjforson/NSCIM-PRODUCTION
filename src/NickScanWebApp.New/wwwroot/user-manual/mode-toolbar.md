---
title: "Render Mode toolbar — the 9 modes explained"
category: "Viewer"
order: 20
requires: [Pages.ImageAnalysisView, Pages.ImageAnalysisAudit, Pages.ImageAnalysisManagement]
updated: 2026-04-21
version: v2.10.3
---

# The RENDER MODE row

The chips beneath the top toolbar let you view the same scan through nine different vendor-standard "lenses." Each one emphasises something different.

## One-line summaries

| Mode | What it emphasises | Typical use |
|---|---|---|
| **Default** | Whatever the scanner shipped as the main image | Quick first look |
| **Composite** | Vendor-faithful colour composite (blue metal, orange organic) | Day-to-day inspection, matches the standalone scanner console |
| **B/W** | Grayscale of the high-energy channel, inverted (dense = dark) | Fine-grain contrast, X-ray standard look |
| **Inverse** | Same as B/W but not inverted (dense = bright) | Occasionally easier for inspecting metal structures |
| **High Pen** | High-energy channel with aggressive gamma to brighten dense areas | Seeing through steel sheets / dense cargo |
| **Low Pen** | High-energy channel with the opposite gamma, accents shallow attenuation | Making low-Z material (plastics, foodstuffs) pop |
| **Organic** | Composite with the organic LUT band neutralised — organic fades, metal stays | Metal inside organic cargo |
| **Metal** | Composite with the metal LUT band neutralised — metal fades, organic stays | Organic inside metal containers |
| **Edge** | Composite + an unsharp-mask pass | Accenting material boundaries |
| **Diff** | Dual-energy difference (HE − LE), remapped 0–255 | Isolating material-class boundaries; liquids show mid-grey |

## What each chip needs

Not every mode works on every scan — it depends on what raw channels are available:

| Mode | Needs |
|---|---|
| Composite, Organic, Metal | HE + LE + **Material** |
| Edge (colour) | HE + LE + **Material** (falls back to greyscale + sharpen when no Material) |
| High Pen, Low Pen, Diff | HE + LE |
| B/W, Inverse | Any single channel |

The toolbar hides chips the scan can't render, so if your scan shows only B/W / Inverse / Edge — the scan doesn't have a second energy channel. If it shows the 5-mode subset (B/W / Inverse / High Pen / Low Pen / Diff / Edge), the scanner didn't emit the `material.img` file (tracked as an ops issue; doesn't affect your analysis for those modes).

## How it works under the hood

Clicking a chip adds `?mode=X` to the image URL and re-fetches. The backend decodes the raw 16-bit channels (cached 30s per scan) and renders the appropriate composite. Typical render time: 30–50 ms; serving out: another 30–100 ms. Instant for operators.

Window/Level sliders also flow through the same URL (as `?loPct=` and `?hiPct=`) — see [Window & Level](/help/window-level).

## Mode tips

- **Start with Composite.** It's the vendor-faithful look most operators are trained on.
- **Switch to B/W or High Pen** when something in the composite is ambiguous (e.g. is this a shadow or real density?).
- **Organic-strip / Metal-strip** are powerful for mixed cargo — use them back-to-back to see what each layer contains.
- **Edge mode** is a quick way to check whether two shapes are separate items or one item touching the wall.
- **Diff** is unusual but very effective for liquids and mixed-density items.

---

## What to read next

- [Window & Level](/help/window-level) — fine-tune the contrast on any mode
- [Raw 16-bit viewer](/help/raw-16bit) — see the underlying dynamic range without JPEG compression

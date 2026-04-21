---
title: "ROI Inspector — histograms + material stats"
category: "Viewer"
order: 60
requires: [Pages.ImageAnalysisView, Pages.ImageAnalysisAudit, Pages.ImageAnalysisManagement]
updated: 2026-04-21
version: v2.13.0
---

# ROI Inspector side panel

When you draw a rectangle on the image (to mark a suspicious area), the **ROI INSPECTOR** panel on the right side of the viewer automatically populates with everything the scanner knows about that region. Click an existing rectangle to re-populate the panel for that area.

## How to draw a rectangle

1. Click the **Mark Area** button in the top toolbar (CropSquare icon, or press `D`).
2. Drag a rectangle on the image over the area you want to inspect.
3. Release — rectangle appears with red corner handles. It's also added to the **MARKED AREAS** chips in the right panel.
4. The **ROI INSPECTOR** section just below MARKED AREAS auto-populates.

## What the panel shows

### Geometry + timing
```
(1726, 360) 579×385 px · 540 ms
```
The rectangle's image-native coordinates, its size in pixels, and how long the backend took to compute the stats (typically 200–800 ms).

### Dominant category
A coloured dot + category label + percentage:
```
● metal    (7.5% of ROI)
```
Tells you the *interesting* material category (the scanner prefers metal / organic over background / noise when both are present above 1% of the area).

### Material distribution
Four horizontal bars:
- **background** — no signal / outside cargo
- **noise** — low-signal, usually empty space
- **organic** — low-Z materials (plastic, textile, wood, liquid, food)
- **metal** — high-Z materials (steel, aluminium, denser compounds)

Each bar is sized by its % of the rectangle. Good for seeing what's actually in a mixed region — "90% organic + 8% metal" means you've got metal items inside a box of organic cargo.

### HE and LE channel stats
```
HE channel
min=842 med=1020 max=47519 · p1=948 p99=33907
```
Plus a 32-bucket histogram visualising the distribution:
- Tall narrow histogram = uniform material (one density throughout)
- Wide spread = varied densities (cluttered cargo)
- Big spike at low + small spike at high = mostly empty space with a few dense items

LE has the same stats in a purple histogram. If HE ≈ LE for this region, the material is low-Z (organic). If HE is noticeably brighter than LE in the histograms, the material is higher-Z (metal).

### Preview thumbnails
Three small (100×80 px max) crops of the rectangle rendered in:
- **HE** — high-energy greyscale
- **LE** — low-energy greyscale
- **MAT** — material class colorised

Useful for a quick visual confirmation of what the numeric stats are describing.

## Single-view and partial-channel scans

- **ASE single-view**: only HE histogram + stats (LE mirrors HE, not shown separately). Material block shows "n/a (single-view)".
- **FS6000 partial-channel (no material)**: HE + LE histograms appear; material block shows "n/a (no material)".

## Tips

- **Draw a small rectangle over a suspicious pixel first.** The stats + previews tell you if your eye was reading the shape right.
- **Compare two rectangles** by drawing them in sequence — click each chip in MARKED AREAS to switch the panel between them. Handy for "is item A the same material as item B?"
- **The histograms are not just pretty pictures.** A bimodal histogram (two peaks) in an ROI often means two different materials adjacent — interesting for concealment analysis.
- **Collapse the panel** with the chevron icon on the `ROI INSPECTOR` header if you need screen real estate for other side-panel items.

## Performance

ROI computation is backed by the same 30-second scan decode cache used by the rest of the viewer — so drawing multiple rectangles on the same scan is fast (50–200 ms each after the first). The preview thumbnails are capped at 240 px on the long side so the wire payload is small (2–20 KB per thumbnail).

---

## What to read next

- [Pixel Probe](/help/pixel-probe) — per-pixel inspection, the finest-grained tool
- [Viewer Basics](/help/viewer-basics) — drawing and selecting rectangles

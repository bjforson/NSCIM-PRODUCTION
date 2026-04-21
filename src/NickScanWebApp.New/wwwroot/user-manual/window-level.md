---
title: "Window & Level — adjusting contrast"
category: "Viewer"
order: 30
requires: [Pages.ImageAnalysisView, Pages.ImageAnalysisAudit, Pages.ImageAnalysisManagement]
updated: 2026-04-21
version: v2.10.4
---

# Window & Level sliders

Located on the right side of the RENDER MODE row. Two sliders:

- **LEVEL** (0–100%, default 50%) — the **centre** of the displayed brightness range
- **WINDOW** (2–100%, default 100%) — the **width** of the displayed brightness range

Together they define a slice of the raw signal that gets mapped to the visible 0–255 grayscale range. Outside that slice, the image clips to black (below) or white (above).

## Radiology operators will recognise this

If you've used a DICOM viewer, this is the same concept. The scanner's signal covers a much wider dynamic range (14 or 16 bits) than a monitor can show (8 bits). Windowing picks which part of that range gets the full contrast budget.

## Practical recipes

| Problem | Level / Window setting | Why |
|---|---|---|
| **Dense cargo looks like a black blob** | Level ~30 / Window ~30 | Shifts the display centre down into the dark values, narrower window means more contrast on what was crushed into black |
| **Bright details washed out** | Level ~70 / Window ~40 | Shifts centre up, narrower window pulls out detail in the bright end |
| **Subtle texture in mid-range cargo** | Level 50 / Window 20 | Narrow window centred on mid-range — maximum contrast for the middle slice |
| **Full overview** | Level 50 / Window 100 (default) | Uses the server's natural 1%–99.5% percentile clip |

Hit the **↻ Refresh icon** between the sliders to reset both to default in one click.

## Which modes support it

- ✅ B/W, Inverse, High Pen, Low Pen, Organic, Metal, Edge, Diff
- ❌ Default and Composite

Composite uses a pre-computed colour LUT fitted from 240 million pixels of real scanner output — the tonal mapping is baked in. When you're on Composite or Default, the sliders **dim** and an info icon appears explaining windowing doesn't apply. Switch to B/W (or any greyscale mode) to get windowing back.

## Performance

- Slider drags are **debounced 180 ms** so the server sees at most one render per pause. Drag freely.
- Each render is 40–80 ms; you should see the new image within ~200–300 ms of releasing.
- If you want **instant** window/level (zero server round-trips), toggle **Raw 16-bit** — the whole slider becomes client-side, re-rendering at 20–60 ms per tick on the cached buffer. See [Raw 16-bit viewer](/help/raw-16bit).

## What the sliders actually do

Slider values map to server `loPct` and `hiPct` percentiles:

```
half        = window / 2
loPct       = max(0, level - half)
hiPct       = min(100, level + half)
```

So `Level 50, Window 100` → `loPct=0, hiPct=100` → full range (no clipping). In practice when the sliders are at defaults, the URL omits these params entirely and the server uses its own default clip (1% / 99.5%) which trims noise without sacrificing usable dynamic range. Nudging either slider switches to the computed percentiles explicitly.

---

## What to read next

- [Raw 16-bit viewer](/help/raw-16bit) — client-side contrast with zero latency on the real 16-bit data
- [Render Mode toolbar](/help/mode-toolbar) — which modes accept Window/Level

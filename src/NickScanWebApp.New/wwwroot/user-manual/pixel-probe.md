---
title: "Pixel Probe — hover for raw values"
category: "Viewer"
order: 40
requires: [Pages.ImageAnalysisView, Pages.ImageAnalysisAudit, Pages.ImageAnalysisManagement]
updated: 2026-04-21
version: v2.11.2
---

# Pixel Probe hover chip

Click the **eyedropper icon** in the top toolbar to turn the probe on. The icon goes green when it's active. Then hover anywhere on the image — a small chip appears at your cursor showing what the scanner actually measured at that pixel.

## What the chip shows

For a full FS6000 or ASE tri-panel scan:

```
(1234, 567)                    ← native pixel coords
HE = 41,203                    ← high-energy raw value (16-bit)
LE = 38,910                    ← low-energy raw value (16-bit)
Mat = 145  metal               ← material class (0–255) + category
RGB  (180, 140, 80)            ← vendor-LUT composite colour at this pixel
```

For a single-channel ASE scan:

```
(1234, 567)
HE = 661
```

(LE / Mat / RGB hidden — single-view scans don't carry those layers.)

For a partial-channel FS6000 (no material):

```
(1234, 567)
HE = 41,203
LE = 38,910
```

(Mat / RGB hidden.)

## How to read the numbers

- **HE / LE** are 16-bit raw attenuation values. `0` means the X-ray went through unobstructed (nothing between source and detector). `65535` means maximum attenuation (very dense). Practical range you'll see: roughly 500–50,000.
- **HE − LE gap** tells you the material kind. Larger gap = more differential attenuation = higher Z (metal). Smaller gap = lower Z (organic).
- **Mat** is the scanner's own Z-effective classification, 0–255. Categories map as:

| Class range | Category | Hint |
|---|---|---|
| 0 | background | outside the container |
| 1–40 | noise | low-confidence; usually empty space with detector noise |
| 41–120 | organic | wood, plastic, textiles, liquids, food |
| 121–255 | metal | steel, aluminium, denser compounds |

- **RGB** is what the Composite mode would paint that pixel — useful when comparing what the operator console would show.

## Performance

Cursor movement fires an API call per mouse-move event, but **throttled to ~80 ms** — so during a drag you get ~12 probes/sec, not hundreds. Each probe is <5 ms on the server (same decode cache the modes use).

If the backend is under heavy load and a probe fails, the chip keeps showing the last successful reading until a new one arrives. It doesn't flicker.

## Turning it off

Click the eyedropper again. The chip stops appearing immediately. Probe mode coexists with other tools — you can have probe on while drawing rectangles or using the magnifier.

---

## What to read next

- [ROI Inspector](/help/roi-inspector) — same underlying data but for a whole rectangle, with stats + histograms
- [Render Mode toolbar](/help/mode-toolbar) — if the probe values look off, switching modes may clarify what you're looking at

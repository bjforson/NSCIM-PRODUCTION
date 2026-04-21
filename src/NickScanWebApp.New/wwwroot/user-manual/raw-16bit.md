---
title: "Raw 16-bit viewer — real dynamic range"
category: "Viewer"
order: 50
requires: [Pages.ImageAnalysisView, Pages.ImageAnalysisAudit, Pages.ImageAnalysisManagement]
updated: 2026-04-21
version: v2.12.0
---

# Raw 16-bit viewer

The standard modes all go through a JPEG conversion pipeline that downsamples the scanner's 16-bit signal to 8-bit before you see it. For most day-to-day work that's fine. But for **subtle density variations** (organic hidden inside dense cargo, contraband shielded by metal, etc.) the JPEG path loses information.

The **Raw 16-bit** toggle bypasses the JPEG path entirely.

## Turning it on

Click the **Raw 16-bit** chip at the right end of the RENDER MODE row. It turns green, and below it three new chips appear:

- **HE** — the high-energy channel (default on enable)
- **LE** — the low-energy channel
- **MAT** — the material classification (only on scans with material)

A small status label shows the geometry: `2091×1378 @ 16-bit · 5.5 MB`.

## What changes

1. The image you see is drawn on a **canvas** by JavaScript running in your browser, not a JPEG from the server.
2. The **LEVEL / WINDOW sliders** re-render the canvas **locally** — zero server round-trips. Dragging a slider produces a new rendered frame in 20–60 ms.
3. The full 16-bit dynamic range is available — you're not limited to the 255 possible values in a JPEG.

## When to use it

- **Dense cargo inspection.** Moving the WINDOW slider down to ~20% and LEVEL up towards the bright end reveals subtle texture that's invisible in the JPEG path.
- **Contraband analysis.** If you suspect something hidden in a metal container, the 16-bit view lets you pull information out of the near-dark region without the JPEG compression artefacts.
- **A/B with the standard modes.** Enable Raw, compare the tonal information, then click Raw off to see if the standard render agrees. Discrepancies tell you something needs further investigation.

## Behaviour notes

- First enable of a plane (e.g. first click on HE) does a one-time 1–8 MB download from the server, cached in your browser tab.
- Switching between HE / LE / MAT: fetches the new plane **once**, then cached forever until you close the dialog.
- Switching back to a previously-loaded plane: instant (cache hit).
- Closing the dialog or navigating to a different container clears the cache.

## Plane availability

| Scan variant | Planes offered |
|---|---|
| FS6000 full / ASE tri-panel | HE, LE, MAT |
| ASE single-view | HE only (LE / MAT hidden — they don't exist on single-view scans) |
| FS6000 partial-channel | HE, LE (MAT hidden — scanner didn't emit it) |

## Performance

- Each plane fetch: 100–800 ms depending on scan resolution and network.
- Each client-side window/level re-render: 20–60 ms (negligible).
- Zero server round-trips once the buffer is loaded, **even for continuous slider drags**. Network tab: you'll see exactly N fetches for N planes viewed, nothing else.

## When the standard JPEG path is enough

For most day-to-day analysis (composite, mode switching, general pan/zoom) the standard modes are faster to use and plenty accurate. Reach for Raw 16-bit when you **need** the full dynamic range — usually because something is visually ambiguous at the JPEG level.

---

## What to read next

- [Window & Level](/help/window-level) — the sliders behave differently in Raw mode
- [Pixel Probe](/help/pixel-probe) — per-pixel inspection works in both the JPEG and Raw paths

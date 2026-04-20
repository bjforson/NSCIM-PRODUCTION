# Operator X-Ray Viewer — Phase Plan

**Status doc. Updated as work lands. Lives in git so sessions can pick up cleanly.**

> **Picking this up in a fresh session?** Jump to [Next Session Entry Point](#next-session-entry-point) first.
> It tells you the exact file to open and what state the work is in.

---

## Goal

Get the NSCIM operator viewer to vendor-grade parity for both scanner families:

- **FS6000** (Heimann Smiths) — 4-blob: Main JPEG + HE 16-bit + LE 16-bit + Material 8-bit
- **ASE** (Nuctech) — 1 blob, two variants:
  - tri-panel (`lineDataType=3`) — low|high|material planes, 16-bit, ~8% of production (~221/2770 scans)
  - single-view (`lineDataType=2`) — one plane only, ~92% of production

Parity = operator can reproduce every image-manipulation the vendor's own software does
(composite, pen modes, organic/metal strip, edge, diff, bw/inverse), at full blob
resolution, with access to the underlying 16-bit data for windowing / measurement.

## Current version

`2.10.2` — vendor-faithful FS6000 composite now shipped as default image.
See `src/Directory.Build.props`.

---

## Phase roadmap

| # | Phase | Status | Backend | Frontend | Notes |
|---|-------|--------|---------|----------|-------|
| 0 | Mode catalog + ROI inspector (v2.10.0) | Done | Shipped | — | 9 modes, capability gating, /roi |
| 0b | Empirical vendor LUT (v2.10.1–2) | Done | Shipped | — | 240M-pixel 3D LUT, ~4 RGB/ch error |
| 1 | Single-canvas viewer chassis + mode toolbar | In progress | Done (v2.10.0) | **Not started** | backbone for 2–5 |
| 2 | Windowing slider (server tone-curve) | Not started | Needs `?window=`, `?level=` params | Debounced slider | ~1 day |
| 3 | Pixel-value probe | Not started | `/pixel?x=&y=` endpoint | Hover chip | ~½ day |
| 4 | Client-side 16-bit viewer | Not started | Raw-binary endpoint (HE/LE/Material) | JS canvas W/L | ~1.5 days; real 16-bit parity |
| 5 | ROI inspector side panel | Not started | Done (v2.10.0) | UI wiring only | ~½ day |

**Strict order:** Phase 1 must ship before 2–5 because it's the chassis they all mount on.
Phases 2, 3, 5 can then go in any order. Phase 4 (raw 16-bit) is the most ambitious and
should go last so Phases 1–3 stabilise the UX first.

---

## Next session entry point

**Currently:** Phase 1 frontend not started.
**Start here:** `src/NickScanWebApp.New/Components/Operations/ImageAnalysisViewer.razor`
(the main operator viewer — called from `ContainerDetails.razor` and
`Pages/ImageAnalysis/*`). Around line 1280–1320 is the image URL builder. Phase 1 adds:

1. On open, GET `/api/ImageProcessing/container/{id}/mode-capabilities` → cache the
   `{variant, supportedModes[]}` response.
2. Render a mode toolbar above the canvas showing only the supported modes for this scan.
3. Clicking a mode flips a `_activeMode` field and re-issues the image URL with
   `?mode={name}` — the backend already handles this.
4. Default mode = `composite` (what the viewer shows today).

**Do not** start Phase 2–5 until Phase 1 is visually working against a real FS6000 scan
and a real ASE tri-panel scan. The toolbar gating matters: single-view ASE scans
(92% of ASE production) must only show `bw`, `inverse`, `edge` — the rest 422.

---

## Phase detail

### Phase 0 — Mode catalog (shipped in v2.10.0)

- **What:** 9-mode vendor-vocabulary catalog. Composite, BW, Inverse, High-Pen, Low-Pen,
  Organic-Strip, Metal-Strip, Edge, Diff.
- **Why these 9:** cross-checked against Smiths Heimann, Rapiscan, and Nuctech operator
  UI terminology. Names match what scanner operators already know.
- **Capability gating:** FS6000 = all 9; ASE tri-panel = all 9; ASE single-view = bw/inverse/edge only.
- **Files:**
  - `src/NickScanCentralImagingPortal.Services.ImageProcessing/FS6000/FS6000ModeRenderer.cs`
  - `src/NickScanCentralImagingPortal.Services.ImageProcessing/ASE/AseTriPanelDecoder.cs`
  - `src/NickScanCentralImagingPortal.Services.ImageProcessing/RoiInspectorShared.cs`
  - `src/NickScanCentralImagingPortal.Services.ImageProcessing/ASEImagePipeline.cs`
  - `src/NickScanCentralImagingPortal.Core/Interfaces/IImageProcessingService.cs`
  - `src/NickScanCentralImagingPortal.API/Controllers/ImageProcessingController.cs`
- **API surface:**
  - `GET .../container/{id}/complete/image?mode={name}&size=full|thumbnail`
  - `GET .../container/{id}/mode-capabilities` → `{variant, supportedModes[]}`
  - `GET .../container/{id}/roi?xPct=&yPct=&wPct=&hPct=` → ROI inspector blob
  - 422 on unsupported mode, with pointer to `/mode-capabilities`.
- **Side effect fixed:** ASE tri-panel had a latent renderer bug pre-v2.10.0 that showed
  only the high plane. Now routes via `AseTriPanelDecoder` → `FS6000ModeRenderer`.

### Phase 0b — Empirical vendor LUT (shipped in v2.10.1 + v2.10.2)

- **Problem:** our Python-ported `FS6000Compositor` produced washed-out colour that
  didn't match vendor output. User correctly pushed back that the Python itself couldn't
  be the reference — we wrote it.
- **Fix:** reverse-engineered the vendor by fitting a 3D LUT from production data.
  64 FS6000 scans × ~3.7M pixels = **240M training pairs** of
  `(Material class, HE bucket, LE bucket) → (R, G, B)` extracted from raw channels
  paired with each scan's own vendor-rendered Main JPEG.
- **Dimensions:** 256 classes × 32 HE buckets × 32 LE buckets × 3 RGB = 768 KB.
- **Why 3D not 1D:** phase-1 diagnostic found 47% of `(class, HE)` cells produce
  materially different RGB when LE varies (max |dRGB| = 207). Pre-computed class alone
  is not sufficient — vendor appears to derive Z-effective from LE/HE ratio internally.
- **Bucketing:** `he_bucket = he_u16 >> 11` (top 5 bits). Matches training script bit-for-bit.
- **Sparse cells:** ~95% of cells had zero samples; filled by nearest-neighbour in HE×LE
  within the same class. Classes with <100 samples default to grey.
- **Validation:** held-out 16 scans, mean per-pixel error **3.91 RGB units/channel**
  (max per-scan 5.56). Visually indistinguishable from vendor output.
- **Artifact:** `src/NickScanCentralImagingPortal.Services.ImageProcessing/FS6000/vendor_lut_v1.bin`
  embedded in the assembly as `EmbeddedResource`, loaded lazily by `FS6000VendorLutCompositor`.
- **v2.10.2:** made the vendor LUT the *default* composite path
  (`TryRenderCompositeNative` in `FS6000ImagePipeline.cs`). `CompositeLegacy` mode
  preserved for A/B if needed.
- **Research tools:** `tools/vendor-lut-research/` — `01_le_diagnostic.py`,
  `02_build_3d_lut.py`, `03_validate.py`, `holdout_scans.txt`.

### Phase 1 — Single-canvas viewer chassis + mode toolbar

- **Goal:** one canvas + a toolbar of mode buttons. Click a button → canvas swaps to that mode.
- **Why first:** Phases 2–5 all need a place to mount their UI. Without a chassis they'd
  each reinvent their own.
- **Backend:** already shipped in v2.10.0 (`?mode=X` on the image endpoint).
- **Frontend work (all in `ImageAnalysisViewer.razor`):**
  - Call `/mode-capabilities` in `OnInitializedAsync`; cache the response.
  - Add a `MudToggleGroup` (or button row) above the canvas binding `_activeMode`.
  - Disable/hide buttons not in `supportedModes`.
  - In `GetCurrentImageUrl()`, append `&mode={_activeMode}` when `_activeMode != "composite"`
    (composite is the default — no query param needed).
  - Bump `_imageCacheBuster` on mode change so the browser refetches.
  - Show a toast if the endpoint returns 422 (shouldn't happen if gating is correct,
    but belt-and-braces).
- **Acceptance:** open a container with a FS6000 scan, see all 9 buttons, click each,
  watch canvas swap. Open a single-view ASE, see only bw/inverse/edge. Open a tri-panel
  ASE, see all 9.
- **Estimate:** 1 day.

### Phase 2 — Windowing slider (server tone-curve)

- **Goal:** operator drags a slider, image brightness/contrast re-renders server-side
  using the pre-encoded JPEG path.
- **Backend:**
  - Add `?window=X&level=Y` to `/complete/image`. Route through mode renderer with an
    extra tone-curve step on the 16-bit buffer before 8-bit conversion.
  - Cache key includes window/level so repeat renders are cheap.
- **Frontend:**
  - Two MudSliders (window, level) with 150ms debounce.
  - Bind to `_window`, `_level`; append to image URL when non-default.
- **Where it lives:** same `ImageAnalysisViewer.razor` — slot into Phase 1's chassis.
- **Estimate:** 1 day.

### Phase 3 — Pixel-value probe

- **Goal:** operator hovers → small chip shows `HE=12345, LE=9876, Material=42` at that pixel.
- **Backend:**
  - New `GET .../container/{id}/pixel?x=&y=` → `{he, le, material, rgb}`.
  - Decode cached (already using `IMemoryCache`, 30s TTL).
- **Frontend:**
  - Mouse-move handler on the canvas, throttled to ~60ms.
  - Absolute-positioned MudChip tracks mouse position.
- **Estimate:** ½ day.

### Phase 4 — Client-side 16-bit viewer

- **Goal:** real 16-bit parity — user sees the full dynamic range, not the JPEG-8-bit.
- **Why it matters:** user was right to push on this. Current wire format is 8-bit JPEG
  (<500KB). A 16-bit HE plane is ~8MB uncompressed. Operator manipulation (window/level)
  on 8-bit loses information; on 16-bit doesn't.
- **Backend:**
  - New `GET .../container/{id}/raw?plane=he|le|material` → `application/octet-stream`
    with the 16-bit (or 8-bit for material) buffer and a tiny header (width, height, bitdepth).
  - Gated behind a feature flag / user perm — it's large.
- **Frontend:**
  - Fetch raw buffer → draw to canvas via `ImageData` with a JS window/level function.
  - All W/L happens client-side, zero server roundtrips per slider tick.
- **Sizing:** FS6000 is ~1920×2048 × 2 bytes = ~8MB per plane. ASE tri-panel similar.
  Acceptable on LAN; add a size warning on slow connections.
- **Estimate:** 1.5 days.
- **Do last** — most risk, biggest surface.

### Phase 5 — ROI inspector side panel

- **Goal:** operator draws a rectangle → side panel shows per-plane stats, material class
  distribution, and small preview JPEGs of the ROI in each mode.
- **Backend:** done in v2.10.0. `GET .../container/{id}/roi?xPct=&yPct=&wPct=&hPct=`.
- **Frontend:**
  - Reuse the existing rectangle-drawing code in `ImageAnalysisViewer.razor` (search for
    `_rectangles`).
  - On draw-complete, call `/roi` with the rect-normalised percentages.
  - Render returned JSON as a MudExpansionPanel side panel (stats table + thumbnail strip).
- **Estimate:** ½ day. Quick win because backend is already in production.

---

## Key decisions log

- **Vendor parity via empirical LUT, not physics reasoning** — attempted Python
  physics-port first, it diverged from vendor output. User flagged: our Python can't be
  the reference because we wrote it. Switched to fitting from real paired data.
- **3D (class, HE, LE) not 1D (class)** — diagnostic proved LE matters. See phase 0b.
- **Bucket = top 5 bits** — 32³ × 256 gave 768KB. Smaller buckets didn't improve error
  meaningfully at this sample count; larger inflated the LUT without payoff.
- **LUT embedded in DLL** — deploy pipeline doesn't ship sidecar files. 768KB is trivial
  vs existing DLL size.
- **9 modes, vendor-standard names** — so operators trained on vendor software find
  them immediately. No made-up labels.
- **Capability-gate ASE single-view to 3 modes** — the other 6 need two energy channels,
  single-view doesn't have them. 422 with a pointer is strictly better than a degraded render.
- **Default composite path uses the LUT (v2.10.2)** — no opt-in flag. Legacy Python
  compositor still reachable via `?mode=composite-legacy` for A/B only.
- **Phase 1 ordering is strict** — chassis must exist before 2–5 mount on it.
- **Phase 4 goes last** — ambitious, largest user-visible surface, best to stabilise
  cheaper phases first.

---

## Files touched (cumulative)

### Services.ImageProcessing
- `FS6000/FS6000ModeRenderer.cs` — 9-mode dispatcher
- `FS6000/FS6000VendorLutCompositor.cs` — LUT composite
- `FS6000/FS6000Compositor.cs` — legacy compositor (kept for A/B)
- `FS6000/vendor_lut_v1.bin` — 768KB embedded LUT
- `FS6000ImagePipeline.cs` — default composite path now uses LUT (v2.10.2)
- `FS6000FormatDecoder.cs` — 4-blob decode (unchanged in this phase)
- `ASE/AseTriPanelDecoder.cs` — splits tri-panel → DecodedFs6000 shape
- `ASEImagePipeline.cs` — mode dispatch + capability gating + ROI
- `RoiInspectorShared.cs` — shared ROI stats/preview helpers

### Core
- `Interfaces/IImageProcessingService.cs` — `ScanModeCapabilities` DTO + 3 new methods

### API
- `Controllers/ImageProcessingController.cs` — `?mode=`, `/mode-capabilities`, `/roi`

### Project files
- `Services.ImageProcessing.csproj` — `<EmbeddedResource Include="FS6000\vendor_lut_v1.bin" />`
- `Directory.Build.props` — version bumps

### Tools (not shipped)
- `tools/vendor-lut-research/01_le_diagnostic.py`
- `tools/vendor-lut-research/02_build_3d_lut.py`
- `tools/vendor-lut-research/03_validate.py`
- `tools/vendor-lut-research/holdout_scans.txt`
- `tools/vendor-lut-research/vendor_lut_v1.bin` / `.npz`

---

## Open questions / parked items

- **LUT refresh cadence.** The LUT was fitted from scans through ~2026-04-19. If the
  vendor firmware or phantom calibration changes materially, error will creep. No
  drift monitor in place yet — worth adding a nightly job that samples N fresh scans
  and flags if reconstruction error crosses a threshold (say >8 RGB/ch). Parked.
- **Phase 4 feature flag.** An 8MB-per-view fetch on a slow shared mobile link would be
  painful. Consider a per-user `allow_raw_16bit` permission + a size warning. Parked
  until Phase 4 starts.
- **`CompositeLegacy` retention.** Once Phase 1 ships and operators confirm the new
  composite visually, we can drop `FS6000Compositor` and remove `composite-legacy` from
  the mode enum. Don't do it pre-Phase-1.
- **ASE LUT.** Phase 0b built an FS6000 LUT. ASE tri-panel colour is currently rendered
  through the *same* FS6000 LUT via the `AseTriPanelDecoder` reshape. This works because
  the underlying physics (dual-energy) is the same, but has not been separately validated
  against ASE vendor composite output. Flag for a dedicated ASE validation pass once
  Phase 1 is live — can then use the Chrome extension to side-by-side.

---

## Ops notes

- **Deploy target:** `C:\Shared\NSCIM_PRODUCTION\publish\` (five NSCIM_* services).
- **Dev source:** `Y:\` → robocopy into `C:\Shared\NSCIM_PRODUCTION\`.
- **Git:** authoritative source, remote `github.com/bjforson/NSCIM-PRODUCTION`, branch `main`.
- **Version bump:** `src/Directory.Build.props` before each deploy.
- **Do not edit** `C:\NICK ERP\` — it's a stale ghost from the 2026-04-19 hot-patch window.
- **API auth is strict** — 401s mean expired sessions, not anonymous-caller degrade.

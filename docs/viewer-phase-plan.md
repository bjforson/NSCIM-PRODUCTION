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

**`v2.14.0`** — the five-phase viewer arc is complete plus partial-channel
rendering. See [Version history](#version-history) for the full trail.
See `src/Directory.Build.props` for the live version stamp.

---

## Phase roadmap

| # | Phase | Status | Version |
|---|-------|--------|---------|
| 0 | Mode catalog + ROI inspector (backend) | Shipped | v2.10.0 |
| 0b | Empirical vendor LUT (240M-pixel fit, ~4 RGB/ch error) | Shipped | v2.10.1–2 |
| 1 | Single-canvas viewer chassis + mode toolbar | Shipped | v2.10.3 |
| 2 | Server-side Window/Level sliders | Shipped | v2.10.4 |
| — | Capability hotfix (check raw-channel presence) | Shipped | v2.10.5 |
| — | **Structural refactor: unified scan pipeline** (5-layer Adapter/Retriever/Router/Kernel/Pipeline, variadic-channel IR, declared material taxonomy) | Shipped | **v2.11.0** |
| — | ASE adapter 90° CCW rotation fix | Shipped | v2.11.1 |
| 3 | Pixel-value probe hover chip | Shipped | v2.11.2 |
| 4 | Client-side 16-bit viewer (raw-binary endpoint + JS canvas window/level) | Shipped | v2.12.0 |
| 5 | ROI inspector side panel UI | Shipped | v2.13.0 |
| — | Partial-channel mode rendering (HE+LE without Material) | Shipped | v2.14.0 |

**Arc complete.** The operator viewer now reaches vendor-grade parity for all
three scan variants (FS6000 full, FS6000 partial-channel, ASE tri-panel,
ASE single-view), with real 16-bit dynamic range, region analysis, and per-
pixel probing — all running through one scanner-agnostic pipeline.

---

## Next session entry point

**Viewer arc is done.** All five phases shipped, verified against real FS6000
and ASE scans. Next work streams sit outside the viewer itself:

1. **Ops ticket** — see [Open questions / parked items](#open-questions--parked-items)
   below for three data-integrity items flagged during sign-off (archive
   retention, scanner material.img misses, one truncated LE blob).
2. **AI hooks** — the `DecodedScan` IR is ready for ML pipelines (variadic
   channels + declared material taxonomy + pixel pitch field). Any future
   inference layer consumes the IR directly; no scanner-specific code on
   the ML side.
3. **New scanner onboarding** — see [architecture-image-pipeline.md](architecture-image-pipeline.md)
   for the "add a new scanner" runbook. ~250 lines of new code (adapter +
   retriever + DI + enum + detector branch). No changes to the kernel,
   the pipeline, or the controllers.

---

<details>
<summary>Historical notes from mid-arc (kept for session continuity)</summary>

**Used to say:** "Phases 1 + 2 shipped (v2.10.3 + v2.10.4). Awaiting visual sign-off."

**To verify Phase 1 (mode toolbar):**

1. Open a container with a FS6000 scan. "Render Mode" row should show `Default` +
   all 9 mode chips. Click each — canvas should swap.
2. Open a single-view ASE (should be ~92% of ASE scans in production). "Render Mode"
   row should show `Default` + only `B/W`, `Inverse`, `Edge`.
3. Open a tri-panel ASE (8% of ASE). Should show all 9.
4. Check the variant label at the right edge of the toolbar.

**To verify Phase 2 (windowing):**

5. Pick B/W or High Pen mode. LEVEL and WINDOW sliders should brighten. Drag
   WINDOW down to ~30% — image should gain contrast (narrower band). Drag LEVEL
   to 20% — image should darken (band shifts to low end of signal). Drag LEVEL
   to 80% — image should brighten.
6. Hit the Refresh icon — image reverts to default render.
7. Pick Composite or Default. Sliders should dim + a small info icon should
   appear explaining windowing is ignored by the vendor LUT.
8. Network tab: only ~one refetch per ~180ms during a slider drag (debounce).

**If sign-off passes, start Phase 3** (pixel-value probe). Add
`GET /api/ImageProcessing/container/{id}/pixel?x=&y=` endpoint returning
`{he, le, material, rgb}` from the cached decoded scan; wire a throttled
mouse-move handler + floating MudChip in `ImageAnalysisViewer.razor`.

**If Phase 1 sign-off fails**, the toolbar is hidden entirely when
`/mode-capabilities` returns empty — viewer still works on the default
path. Check browser console for `[LoadModeCapabilitiesAsync]` errors.

**If Phase 2 sign-off fails**, sliders at defaults don't forward any query
params, so the no-touch case is identical to v2.10.3. Look for XHR with
`loPct=` / `hiPct=` and check backend logs for the `[FS6000-MODE]` /
`[ASE-MODE]` lines that include the forwarded values.

</details>

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

### Phase 1 — Single-canvas viewer chassis + mode toolbar (v2.10.3)

- **Goal:** one canvas + a toolbar of mode buttons. Click a button → canvas swaps to that mode.
- **Why first:** Phases 2–5 all need a place to mount their UI.
- **Backend:** shipped in v2.10.0 (`?mode=X` on the image endpoint).
- **Frontend:** shipped in v2.10.3 in `ImageAnalysisViewer.razor`:
  - New fields: `_activeMode`, `_scanVariant`, `_supportedModes`,
    `_modeCapabilitiesLoaded`, `ModeLabels` dictionary.
  - New methods: `LoadModeCapabilitiesAsync()` (soft-fails — toolbar hidden if API down),
    `SelectMode(string)` (bumps cache buster so `<img>` @key flips).
  - Toolbar row rendered between Row 1 (existing tool chrome) and Row 2 (70/30 content),
    only when capabilities loaded and `_supportedModes.Count > 0`.
  - `GetCurrentImageUrl()` appends `&mode={url-escaped}` when `_activeMode != ""`.
  - `OnInitializedAsync` + `OnParametersSetAsync` both reset state + reload.
  - Mode applies only on the default render path. `_useEnhancedImage` path
    (`/api/image-analysis/{id}/enhanced`) is a separate pipeline and intentionally
    ignores mode; documented inline.
- **Acceptance (pending):** manual visual sign-off against real FS6000, real ASE tri-panel,
  real ASE single-view. See [Next Session Entry Point](#next-session-entry-point).

### Phase 2 — Windowing slider (server tone-curve) (v2.10.4)

- **Goal:** operator drags sliders, image brightness/contrast re-renders server-side
  from the raw 16-bit buffers (not post-processed on the client 8-bit JPEG).
- **Backend:** already shipped in v2.10.0 — `?loPct=&hiPct=` query params flow
  end-to-end: controller → `IImageProcessingService.GetRenderedImageBytesAsync` →
  `FS6000ModeRenderer.RenderJpeg` → `FS6000Compositor.NormalizeEnergyChannel`.
  Phase 2 didn't require any backend changes; it just started driving the knobs.
- **Frontend (v2.10.4) in `ImageAnalysisViewer.razor`:**
  - New fields: `_serverWindowPct` (default 100), `_serverLevelPct` (default 50),
    `_windowLevelDebounceTimer` (System.Threading.Timer).
  - New methods: `OnWindowLevelSliderChanged()` (180ms debounce — coalesces drag
    bursts into one refetch), `ResetWindowLevel()`, `ComputeLoHiPct()`
    (slider → loPct/hiPct mapping), `IsWindowLevelEffective()` (dims + tooltip
    when mode = Composite / Default, which bake tone via vendor LUT).
  - Sliders inline in the RENDER MODE row next to the mode chips, with
    numeric % readouts and a Refresh-icon reset button.
  - `GetCurrentImageUrl()` appends `&loPct={lo}&hiPct={hi}` only when sliders
    have been moved from defaults — so the no-touch case preserves the
    pre-v2.10.4 visual baseline.
  - Container change resets sliders to defaults; `DisposeAsync` kills the timer.
- **Mapping:** `half = window/2`; `lo = max(0, level - half)`; `hi = min(100, level + half)`.
  At defaults (window=100, level=50) → (0, 100) but those params are never sent;
  server clips at its own (1, 99.5).
- **Known limitation:** Composite mode (vendor LUT) bakes tonal mapping into the
  LUT itself, so windowing is a visual no-op there. UI dims the sliders + shows
  an info icon so operators know to pick a greyscale / colourised mode.

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

### WebApp (Blazor)
- `src/NickScanWebApp.New/Components/Operations/ImageAnalysisViewer.razor` — Phase 1 mode toolbar (v2.10.3)

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

### Ops tickets (data-integrity, flagged during v2.14.0 sign-off)

- **Pre-April 4 Archive retention gap.** 1,036 FS6000 scans between
  2026-03-18 and 2026-04-03 have no raw `.img` files in the Archive
  folder — only the vendor JPEG + XML. Either the archiver was
  configured to copy just the JPEG during that window, or the raw files
  were purged for space. Raw data for those scans is **permanently lost**.
  Action: none possible for those scans; review archiver config going
  forward so current retention actually keeps the raw channels.
- **Scanner not emitting `material.img` for some scans.** 43 recent
  (2026-04-20) FS6000 scans have `high.img` + `low.img` on disk but no
  `material.img`. Implies the scanner's material-classification stage
  isn't running (or not writing output) for a fraction of scans. v2.14.0
  makes these scans usable (5-mode subset via partial-channel rendering)
  but it's still worth investigating the scanner config. Frequency:
  ~43 / 284 recent scans with any raw data = ~15%.
- **One scan with truncated LE blob.** `TCKU1817911`: `HighEnergy` is
  11 MB in DB but `LowEnergy` is exactly 2 MB (suspiciously round —
  likely a partial write during ingest). Decode correctly throws
  "channel truncated" and the endpoint returns
  `vendor-jpeg-only (missing: Material)`. One-off data integrity issue;
  worth a spot-check for similar truncations elsewhere via a query on
  `length(he_imagedata) != length(le_imagedata)`.

### Parked architectural items

- **Ingest pipeline unification.** Evaluated during the arc and rejected.
  Ingest is pure byte-shuttling (`Services.FS6000/IngestionService.cs`
  1,147 lines + `Services/ImageProcessingOrchestrator.cs` 206 lines,
  **zero decoder calls**). The "scanners declare format once" goal is
  already met — format decoders are declared once per scanner and used
  once per scanner (on the render side). Forcing ingest to use the
  adapter layer would add a rejection path for edge-case blobs with no
  offsetting benefit.
- **LUT refresh cadence.** The LUT was fitted from scans through ~2026-04-19.
  If the vendor firmware or phantom calibration changes materially, error
  will creep. No drift monitor in place yet — worth adding a nightly job
  that samples N fresh scans and flags if reconstruction error crosses a
  threshold (say >8 RGB/ch). Parked.
- **Phase 4 feature flag.** An 8 MB-per-view fetch on a slow shared mobile
  link would be painful. Consider a per-user `allow_raw_16bit` permission
  + a size warning. Current impl doesn't gate.
- **`CompositeLegacy` retention.** The Python-ported composite is kept as
  a mode-name alias (`composite-legacy` → `RenderMode.CompositeLegacy`)
  for A/B debugging. Can be dropped once the vendor-LUT composite has
  operator confidence — not urgent.
- **ASE LUT validation.** Phase 0b built an FS6000 LUT. ASE tri-panel
  colour is currently rendered through the *same* FS6000 LUT via the
  `AseTriPanelDecoder` reshape. This works because the underlying physics
  (dual-energy) is the same, but has not been separately validated
  against ASE vendor composite output. Flag for a dedicated ASE
  validation pass; can use the Chrome extension to side-by-side.
- **Capability-aware frontend.** Today the frontend re-fetches mode
  capabilities on every viewer open. Could short-circuit with a lookup
  cache keyed on containerNumber (TTL 5 min) if we start seeing the
  capability endpoint trending load.

---

## Ops notes

- **Deploy target:** `C:\Shared\NSCIM_PRODUCTION\publish\` (five NSCIM_* services).
- **Dev source:** `Y:\` → robocopy into `C:\Shared\NSCIM_PRODUCTION\`.
- **Git:** authoritative source, remote `github.com/bjforson/NSCIM-PRODUCTION`, branch `main`.
- **Version bump:** `src/Directory.Build.props` before each deploy.
- **Do not edit** `C:\NICK ERP\` — it's a stale ghost from the 2026-04-19 hot-patch window.
- **API auth is strict** — 401s mean expired sessions, not anonymous-caller degrade.

---

## Version history

| Version | What shipped |
|---|---|
| v2.10.0 | Mode-catalog backend (9 modes, capability endpoint, ROI endpoint) |
| v2.10.1 | Empirical vendor LUT (240M-pixel fit, validated against held-out scans) |
| v2.10.2 | Default composite path uses the new LUT |
| v2.10.3 | **Phase 1** — mode toolbar in `ImageAnalysisViewer.razor` |
| v2.10.4 | **Phase 2** — debounced server-side Window/Level sliders |
| v2.10.5 | Hotfix: capability endpoint checks actual raw-channel presence |
| **v2.11.0** | **Structural refactor** — unified scan pipeline (Adapter / Retriever / Router / Kernel / Pipeline), variadic-channel IR, declared material taxonomy. Byte-for-byte output parity vs v2.10.5 verified across 12 test cases |
| v2.11.1 | ASE adapter 90° CCW rotation (mode renders now landscape, matching vendor convention) |
| v2.11.2 | **Phase 3** — pixel-probe hover chip + `/pixel` endpoint |
| v2.12.0 | **Phase 4** — client-side 16-bit viewer (raw-binary endpoint + JS canvas window/level) |
| v2.13.0 | **Phase 5** — ROI inspector side panel UI (histograms + material bars + preview thumbs) |
| v2.14.0 | Partial-channel mode rendering (FS6000 scans with HE+LE but no Material now render 6 modes instead of hiding the toolbar) |

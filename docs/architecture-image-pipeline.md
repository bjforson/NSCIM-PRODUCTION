# NSCIM Image Pipeline Architecture

**Authoritative reference for how scan data flows through the system from ingestion through rendering. Read this before modifying anything under `Services.ImageProcessing/` — it answers the "why is it structured this way" question.**

---

## One-line summary

Multiple scanner-specific **format adapters** produce a single universal **`DecodedScan`** IR; everything downstream (rendering, pixel probe, ROI, capabilities, future AI) runs on that IR and is scanner-agnostic.

## Why this shape

The app is an **image-central portal** — our business is processing scanner images from different vendors. New scanners will arrive (Heimann, Nuctech MX-series, multi-energy systems). The architecture is designed so that adding a scanner requires only per-scanner format-parsing code; everything else is automatic.

The alternative (one pipeline class per scanner with parallel implementations of every operation) would mean every new capability we add — rendering modes, windowing, pixel probe, ROI, AI — pays rent in duplication across every scanner we support. That's a compounding tax that grows with both feature count and scanner count.

## The 5 layers

```
┌──────────────────────────────────────────────────────────────┐
│ Layer 5: Pipeline (one)                                       │
│   ScanProcessingPipeline                                      │
│   Every controller/service calls this; no other entry point.  │
├──────────────────────────────────────────────────────────────┤
│ Layer 4: Kernel (scanner-agnostic, pure functions)            │
│   ScanRenderer / ScanPixelProbe / ScanRoiBuilder /            │
│   ScanCapabilities / RenderModeRequirements                   │
│   Operate on DecodedScan; dispatch on scan *structure*, not   │
│   scanner *identity*.                                         │
├──────────────────────────────────────────────────────────────┤
│ Layer 3: Router (one)                                         │
│   ScanRouter: container → DecodedScan (with 30s cache)        │
│   Resolves scanner type, picks retriever + adapter, decodes.  │
├──────────────────────────────────────────────────────────────┤
│ Layer 2: Retrievers (one per data source)                     │
│   IScanSourceRetriever                                        │
│   FS6000SourceRetriever reads fs6000images                    │
│   ASESourceRetriever reads asescans                           │
│   Knows NOTHING about decoding — just loads raw bytes.        │
├──────────────────────────────────────────────────────────────┤
│ Layer 1: Format Adapters (one per wire format)                │
│   IScanFormatAdapter                                          │
│   FS6000FormatAdapter parses HE/LE/Material .img blobs        │
│   ASEFormatAdapter parses single ASE blob (both tri-panel     │
│     and single-view based on lineDataType header)             │
│   Knows NOTHING about the database — pure bytes→IR.           │
└──────────────────────────────────────────────────────────────┘
```

## The universal IR — `DecodedScan`

Defined in `Services.ImageProcessing/Kernel/DecodedScan.cs`. Key properties:

- `SourceFormatTag` — short tag for the wire format (`"fs6000-v1"`, `"ase-tri-panel"`, `"ase-single-view"`, future `"heimann-mx3000"`).
- `Channels` — variadic `IReadOnlyList<EnergyChannel>`. Single-view = 1 channel; dual-energy = 2; multi-energy = 3+. Each has a `Kind` (High / Low / Mid / Single) and `BitDepth`.
- `Material` — optional `MaterialClassification` with a **declared `MaterialTaxonomy`** — FS6000 declares `{bg 0-0, noise 1-40, organic 41-120, metal 121-255}`; a new scanner declares its own scheme. The kernel reads the taxonomy, never hardcodes band boundaries.
- `PixelPitchMm`, `Orientation` — geometry for calibrated measurements (ruler tool, Phase 4+).
- `VendorReferenceJpeg` — optional pre-rendered scanner JPEG for A/B comparison or UI fallback.
- `SourceMetadata` — opaque dict (serial, firmware, calibration date, etc.).

## Capabilities are derived, not declared

`RenderModeRequirements.IsAvailable(mode, scan)` declares each mode's **structural** requirements. `ScanCapabilities.Derive(scan)` enumerates modes and returns the ones the scan satisfies. Example:

```csharp
RenderMode.Composite      => scan.IsDualEnergy && scan.Material != null,
RenderMode.BlackWhite     => scan.Channels.Count >= 1,
RenderMode.OrganicStrip   => scan.IsDualEnergy && scan.Material != null,
RenderMode.Diff           => scan.IsDualEnergy,
```

A new scanner whose structure has 3 energies + material automatically gets every dual-energy-capable mode. A new mode that requires some future channel kind just declares its requirement and picks up every scanner that satisfies it. **No per-scanner or per-mode tables anywhere.**

## Adding a new scanner

Concrete steps. Example: vendor X with a 3-energy scanner, proprietary 0..63 material taxonomy:

1. Write `VendorXFormatAdapter : IScanFormatAdapter`
   - `SourceFormatTag = "vendor-x-v1"`
   - `DecodeAsync(ScanSourceBytes bytes, ct)` parses the bytes, emits a `DecodedScan` with 4 `EnergyChannel`s + `MaterialClassification` using your declared `MaterialTaxonomy`.
2. Write `VendorXSourceRetriever : IScanSourceRetriever`
   - `ScannerType = ScannerType.VendorX`
   - `LoadAsync(container, ct)` reads from vendor's DB table, populates `ScanSourceBytes.Blobs` with named entries (adapter's choice of key names), sets `SourceFormatTag = "vendor-x-v1"`.
   - `InventoryAsync(container, ct)` reports which blobs are present without loading them.
3. Add `ScannerType.VendorX = 4` to `Core.Interfaces.ScannerType`.
4. Register both in DI (`ServiceConfiguration.cs`):
   ```csharp
   services.AddScoped<IScanFormatAdapter, VendorXFormatAdapter>();
   services.AddScoped<IScanSourceRetriever, VendorXSourceRetriever>();
   ```
5. Extend `ScannerTypeDetector` to check the vendor's scan table.

Done. All 9 modes (or more, as their structural requirements allow), windowing, pixel probe, ROI inspector, capabilities, Phase 3/4/5 UI — all work on the new scanner without any further code.

## Adding a new render mode

1. Add enum value to `RenderMode` (in `Kernel/ScanModes.cs`).
2. Add structural requirement to `RenderModeRequirements.IsAvailable`.
3. Add a wire name + parser synonym in `RenderModeRequirements.Name` / `TryParse`.
4. Implement the recipe in `ScanRenderer.Render` (or a helper it calls).

Every existing scanner that satisfies the requirement automatically gets the new mode in its capabilities response.

## What's explicitly out of scope

### Ingest pipeline

Scan **ingestion** (scanner produces a new scan → write blobs to DB) is a separate lifecycle with separate code paths:
- `NickScanCentralImagingPortal.Services/ImageProcessingOrchestrator.cs` + `IScannerServiceFactory` for Nuctech / HeimannSmith ingest
- `NickScanCentralImagingPortal.Services.FS6000/IngestionService.cs` for FS6000 file-watcher ingest
- ASE has its own ingest via a sync worker

The v2.11.0 refactor does **NOT** unify ingest. It's a separate, bigger refactor. The plan is to fold ingest onto the same adapter model as a follow-up (v2.12.0 candidate) — the `IScanFormatAdapter` from this layer is reusable for parsing on ingest, which would mean scanners declare their format once and both ingest and render use it.

### IImagePipeline (thin, surviving class)

`FS6000ImagePipeline` and `ASEImagePipeline` still exist with the tiny `IImagePipeline` contract: `ProcessImageAsync`, `GetImageMetadataAsync`, `GetImageAsBase64Async`. These handle scanner-specific ingest-adjacent operations (caching, thumbnail generation, scanner-specific metadata synthesis) that don't fit the decoded-IR model. They were intentionally left untouched to keep the v2.11.0 refactor scoped.

### ImageProcessingService

The facade service that controllers inject. It now has two roles: (a) thin delegation to `ScanProcessingPipeline` for the 4 v2.10.x render/probe/ROI/capabilities methods, and (b) the remaining legacy methods (`ProcessImageAsync`, `GetImageMetadataAsync`, `GetCompleteContainerDataAsync`, etc.). Scope for a future pass: migrate the legacy methods onto the pipeline too.

## File map

```
Services.ImageProcessing/Kernel/
├── DecodedScan.cs                       ← The IR (this is the whole model)
├── ScanModes.cs                         ← RenderMode enum + requirements + parser
├── ScanCapabilities.cs                  ← Derive supported modes from scan structure
├── ScanRenderer.cs                      ← 9 mode recipes, pure functions
├── ScanPixelProbe.cs                    ← Per-pixel hover probe
├── ScanRoiBuilder.cs                    ← Rectangle ROI analysis
├── ScanProcessingPipeline.cs            ← Single orchestrator (controllers call this)
├── ScanRouter.cs                        ← container → DecodedScan (with cache)
├── ScannerTypeDetector.cs               ← container → ScannerType (EF lookup)
├── Abstractions/
│   ├── IScanFormatAdapter.cs            ← Parse bytes → IR (per wire format)
│   └── IScanSourceRetriever.cs          ← Load bytes from storage (per data source)
├── Adapters/
│   ├── FS6000FormatAdapter.cs           ← FS6000 blobs + taxonomy
│   └── ASEFormatAdapter.cs              ← ASE blob (tri-panel + single-view)
└── Retrievers/
    ├── FS6000SourceRetriever.cs         ← Reads fs6000images
    └── ASESourceRetriever.cs            ← Reads asescans

Services.ImageProcessing/FS6000/         ← scientific work (untouched by refactor)
├── FS6000FormatDecoder.cs               ← Binary parser (called by adapter)
├── FS6000Compositor.cs                  ← Percentile-clip + legacy composite
├── FS6000VendorLutCompositor.cs         ← 240M-pixel LUT + vendor RGB
└── vendor_lut_v1.bin                    ← 786KB embedded LUT (validated 3.91 RGB/channel vs vendor)

Services.ImageProcessing/ASE/
├── AseFormatDecoder.cs                  ← Binary parser (called by adapter)
└── AseTriPanelDecoder.cs                ← Panel split for lineDataType=3

Services.ImageProcessing/
├── RoiInspectorShared.cs                ← Stat + preview helpers (called by kernel)
├── FS6000ImagePipeline.cs               ← ONLY IImagePipeline methods remain (~489 lines)
├── ASEImagePipeline.cs                  ← ONLY IImagePipeline methods remain (~487 lines)
└── ImageProcessingService.cs            ← IImageProcessingService facade
```

## Version history

- `v2.10.0` — mode catalog + ROI inspector shipped with per-scanner duplicated pipelines
- `v2.10.1` — empirical vendor LUT fitted from 240M training pairs
- `v2.10.2` — default composite uses the LUT
- `v2.10.3` — frontend mode toolbar (Phase 1)
- `v2.10.4` — frontend window/level sliders (Phase 2)
- `v2.10.5` — hotfix: capabilities checks actual raw-channel presence (fixed 82% of FS6000 scans)
- **`v2.11.0`** — full structural refactor. Single unified pipeline. Variadic channels. Declared taxonomies. Scanner-agnostic kernel. Zero scanner-identity branching past the adapter layer.
- `v2.11.1` — ASE adapter rotates 90° CCW during decode so the IR is landscape, matching the legacy `AsePercentileRenderer` + the whole frontend's assumption that ASE images are horizontal. Fixes mode-rendered ASE coming out portrait.
- `v2.11.2` — Phase 3 pixel-probe hover chip backed by a new `/pixel` endpoint that reads one pixel from the cached `DecodedScan` via `ScanPixelProbe`. Works variant-aware: FS6000/ASE tri-panel show HE/LE/Material/RGB; ASE single-view shows only HE.
- `v2.12.0` — Phase 4 client-side 16-bit viewer. New `/raw?plane=he|le|material` endpoint returns the raw buffer as `application/octet-stream`; `Raw16BitViewer.js` fetches once per plane, caches in an in-memory Map, and window/levels client-side. Zero server round-trips per slider tick once the buffer is loaded.
- `v2.13.0` — Phase 5 ROI inspector side panel UI. Hooks into the existing rectangle-draw tool; on draw-complete fires `/roi?x=&y=&w=&h=`; renders per-channel histograms + material-class distribution + 3 preview thumbnails.
- `v2.14.0` — partial-channel mode rendering. FS6000 scans with HE + LE but no Material now decode through a new `FS6000FormatDecoder.DecodeEnergyOnly` path; the adapter emits `SourceFormatTag = "fs6000-v1-no-material"` and the 5-mode greyscale subset (bw / inverse / high-pen / low-pen / diff) lights up automatically because `RenderModeRequirements.IsAvailable` already gates Composite / OrganicStrip / MetalStrip / (the colour Edge path) on `scan.Material != null`.

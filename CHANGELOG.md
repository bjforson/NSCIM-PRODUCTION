# Changelog

All notable changes to NSCIM (NickScan Central Imaging Portal) are recorded here.
The format loosely follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/)
and the project uses [Semantic Versioning](https://semver.org/spec/v2.0.0.html):

- **MAJOR** — breaking schema / contract changes that require manual operator action beyond running migrations
- **MINOR** — new features, new migrations that apply cleanly, backwards-compatible behaviour changes
- **PATCH** — bug fixes, build / publish fixes, documentation, test coverage

The authoritative version number lives in `src/Directory.Build.props`. Bump it
before every `dotnet publish` so the new build's assemblies carry the right
`FileVersion` and the WebApp version endpoint (`/api/server/version`) reports
the live version accurately.

For each release, this file records:

- What landed (grouped by area)
- Any behaviour change the operations team should know about **before** the deploy
- New migrations introduced in the release
- Commits that make up the release (short SHAs)

---

## [2.9.0] — 2026-04-19 — Pipeline ops fixes (audit SLA, FS6000 retry, submission scheduler, dashboard UX)

Hot-patched on the deploy target (`C:\NICK ERP\src`) during the 2026-04-19
operations recovery session and ported back here so source stays authoritative.
The changes unblock: stalled ICUMS submission when `LiveSubmitEnabled` was
briefly toggled off, false "WebApp Unhealthy" flags from 307 redirects, FS6000
UNC copies failing on transient network hiccups, orphaned image-splitter jobs
stuck in `processing` after force-stops, and several dashboard UX wins
(clickable health cards, operator readiness banner, monitoring status filter,
auto-focus on login).

Backup of the exact files as they stood on the target:
`C:\NICK ERP\_CLAUDE_CHANGES_BACKUP_20260419\` (see `CHANGES.md` in that folder
for per-file notes and the out-of-source config mutations).

### What's new

- **Operator readiness banner (Dashboard)** — `Pages/Index.razor` now calls
  `/api/image-analysis/user/readiness-snapshot` + the new
  `/api/recordcompleteness/oldest-in-audit` and renders a warning / error
  alert when 0 analysts or 0 auditors are Ready, or when the oldest InAudit
  record is older than 24 h. Makes SLA breaches visible at a glance.
- **Drill-down from dashboard health cards** — `Overall Health`, `Healthy`,
  `Degraded`, `Unhealthy` cards are now clickable and navigate to
  `/monitoring/health?filter=<status>`. `StatCard.razor` gained `Href` /
  `OnClick` parameters; `components.css` gained a `.stat-pill-clickable`
  hover/active variant.
- **Monitoring health filter** — `Pages/Monitoring/Health.razor` accepts a
  `?filter=healthy|degraded|unhealthy` query param via
  `[SupplyParameterFromQuery]`, shows a dismissible filter chip, and exposes
  `GetFilteredServices()` + `ClearStatusFilter()`.
- **New API endpoint** — `GET /api/recordcompleteness/oldest-in-audit` returns
  `{ OldestCreatedAtUtc, TotalInAudit }` for the dashboard banner.
- **Login auto-focus** — `Pages/Authentication/Login.razor` focuses the
  username field on first render via `MudTextField.FocusAsync()`.

### What's fixed

- **Submission workflow stalled when no new AuditCompleted work existed**
  (`ImageAnalysisOrchestratorService.cs`). The submission branch now fires at
  least every 2 minutes even with `submissionWorkCount == 0`, so
  `RetryPendingIcumsSubmissionsAsync` drains any Outbox payloads written while
  `LiveSubmitEnabled` was briefly off.
- **WebApp health check flapped "Unhealthy" on 307 redirects**
  (`ComprehensiveHealthCheckService.CheckWebAppHealth`). `HttpClient` now uses
  `AllowAutoRedirect = false` and accepts any response in `[200, 600)` as
  proof the WebApp is listening — not just 2xx.
- **FS6000 copies failed hard on transient UNC errors**
  (`FileSyncService.CopyFileWithRetryAsync`). New `IsTransientNetworkError`
  helper detects well-known Win32 HRESULTs (`0x80070035` path not found,
  `0x80070040` network name deleted, `0x8007003B`, `0x80070043`) and retries
  with 2s/4s/8s exponential backoff instead of surfacing the error.
- **Image splitter jobs stuck in `processing` after a force-stop were never
  retried** (`services/image-splitter/main.py`). `resume_pending_jobs` now
  also resets `processing` jobs older than 1 hour back to `pending` on
  startup. *(Already present in source; the target copy was stale. Recorded
  here for completeness.)*
- **Record Pipeline tile hit 404** — `Pages/Index.razor` called
  `/api/record-completeness/summary` (route-name mismatch). Fixed to
  `/api/recordcompleteness/summary`.

### Operator notes

- **Version is still 2.8.0** — `src/Directory.Build.props` has not been
  bumped. Bump to `2.9.0` before the next `dotnet publish` so assemblies and
  `/api/server/version` reflect the new build.
- **Rebuild required.** These are source edits only. Run `dotnet publish` and
  redeploy via `Deploy-ERP-Target.ps1`.
- **Config / DB changes applied during the hot-patch window are NOT in this
  source tree** (they're machine state — DB rows, environment variables,
  appsettings on the deploy target):
    - `decisionagentsettings.allownormaldecisions = true` (was `false`)
    - `decisionagentsettings.abnormalthreshold = 0.50` (was `0.35`)
    - NSSM env on `NSCIM_ImageSplitter`:
      `NICKSCAN_FS6000_SHARE=\\172.16.1.1\Image\23301FS01`
    - Machine env: `FS6000__NetworkSharePath=\\172.16.1.1\Image\23301FS01`
    - WebApp `appsettings.json`: `ApiSettings.BaseUrl` = `https://localhost:5206`;
      mobile URL = `http://localhost:5280`
    - WebApp HTTPS cert: `Subject=10.0.1.254 Store=My Location=LocalMachine`
      (replacing a missing PFX path)
    - Mobile `appsettings.json`: Kestrel bound to `0.0.0.0:5280` (HTTP) /
      `0.0.0.0:5281` (HTTPS) instead of dev port `5000`
    - All `runtimeconfig.json` files carry `"rollForward": "latestMajor"`
      (re-applied by `Deploy-ERP-Target.ps1` step 8.5)

  A future source-driven deploy should re-apply these; ideally fold them into
  `Deploy.ps1` so the next cold deploy does not regress.
- **No new migrations.**

### Files touched

- `src/NickScanCentralImagingPortal.API/Controllers/RecordCompletenessController.cs`
- `src/NickScanCentralImagingPortal.Services/Monitoring/ComprehensiveHealthCheckService.cs`
- `src/NickScanCentralImagingPortal.Services/ImageAnalysis/ImageAnalysisOrchestratorService.cs`
- `src/NickScanCentralImagingPortal.Services.FS6000/FileSyncService.cs`
- `src/NickScanWebApp.New/Pages/Index.razor`
- `src/NickScanWebApp.New/Pages/Authentication/Login.razor`
- `src/NickScanWebApp.New/Pages/Monitoring/Health.razor`
- `src/NickScanWebApp.New/Components/Dashboard/StatCard.razor`
- `src/NickScanWebApp.New/wwwroot/css/components.css`
- `services/image-splitter/main.py` *(already in source; target was stale)*

---

## [1.23.0 – 2.8.0] — 2026-04-09 to 2026-04-19 — release notes not maintained

Change-log entries were not written for the releases that shipped between
1.22.0 (2026-04-09) and 2.9.0 (2026-04-19). The code for those releases is
present in the repo history (`git log --oneline main` covers it) — only the
human-readable release summaries are missing.

Recorded here so the version jump in this file is not a mystery to future
readers. If anyone needs to reconstruct the window properly, the input
material is:

- `git log --since=2026-04-09 --until=2026-04-19 --oneline`
- Deploy history on 10.0.1.254 (`Get-EventLog -LogName Application -Source NSCIM_API` / service install logs)
- `_CLAUDE_CHANGES_BACKUP_20260419/CHANGES.md` under `C:\NICK ERP\` documents the final-day hot-patches that became `2.9.0`.

---

## [1.22.0] — 2026-04-09 — Pure-C# ASE decoder fallback (mode switch, default DllOnly)

Eliminates the single point of failure in NSCIM's ASE decode path. The vendor
`Ase.Image.dll` remains the default; a new config-gated pure-C# fallback
replaces the DLL when it's missing, misbehaving, or explicitly disabled.

Format spec + Python reference came from 1.21.0's X-Ray Inspector work
(`services/image-splitter/inspector/decoders/ase.py` +
`services/image-splitter/tools/ase_format_research/FINDINGS.md`). This
release ports it to C# so the legacy pipelines (ContainerDetails,
BLDecisionReview, ScanReview, ICUMS payload generation, etc.) can benefit.

### What's new

- **`AseFormatDecoder`** (`src/NickScanCentralImagingPortal.Services.ImageProcessing/ASE/AseFormatDecoder.cs`) — pure static parser. `byte[]` → `(ushort width, ushort height, ushort lineDataType, ushort[] pixels)`. No DI, no System.Drawing, no vendor DLL. Full format spec documented inline.
- **`AsePercentileRenderer`** (same folder) — histogram-based 1st/99.5th percentile linear stretch → 8-bit indexed grayscale `Bitmap`. O(n + 65536). Mirrors the Python `_normalize_percentile` math line-for-line.
- **`AseDecoderOptions`** (same folder) — options class bound to `ImageProcessing:AseDecoder` with four modes:
  - `DllOnly` (default) — current behavior, byte-identical to 1.21.x
  - `Shadow` — DLL is authoritative; fallback runs fire-and-forget on `Task.Run` and logs pixel-stat comparison for validation
  - `FallbackOnFailure` — try DLL first; on exception, activate pure-C# fallback; return DLL's error if both fail
  - `FallbackOnly` — skip DLL entirely, for DLL-less environments
- **`ASEImageConverterService`** gains `IOptions<AseDecoderOptions>` injection, the four-way mode switch, `RunFallback` / `RunShadowCompare` / `EncodeBitmapToJpeg` helpers, and a new `DecoderUsed` field on `AseImageConversionResult` that rides with the result up to the pipeline.
- **`ASEImagePipeline`** propagates `DecoderUsed` into `ImageCache.ProcessingPipeline`, so ops can run `SELECT ProcessingPipeline, COUNT(*) FROM imagecaches GROUP BY 1` to track adoption. New values: `ASE-Proprietary-to-JPEG-DLL` and `ASE-Proprietary-to-JPEG-Fallback`. Old cached rows keep their legacy `ASE-Proprietary-to-JPEG` value (no migration).
- **`ServiceConfiguration`** registers `AseDecoderOptions` inside `AddEnhancedServices` where `IConfiguration` is already in scope.
- **`appsettings.json`** (both `src/NickScanCentralImagingPortal.API/` and `deploy/api/`) gains a new `ImageProcessing:AseDecoder` section defaulting to `DllOnly`. A deploy without any config change is byte-identical to 1.21.x.

### Unit tests

New xUnit test fixture in `src/NickScanCentralImagingPortal.Tests/ASE/AseFormatDecoderTests.cs` with 9 tests:
- `Decode_KnownSample_ReturnsExpectedDimensions` — loads `sample_single_view.ase` (544×1554 single-view from production), asserts w/h/lineDataType
- `Decode_KnownSample_PixelStatsMatchPythonReference` — **byte-for-byte parity with Python reference** on a real production sample (min=0, max=65535, mean=30089.91, std=22888.29 — matches `.report.json` sidecar exactly)
- `Decode_InvalidMagic_ThrowsInvalidDataException`
- `Decode_TooSmallBlob_ThrowsInvalidDataException`
- `Decode_TruncatedPayload_ThrowsWithCompressionHint` (mentions `CompEncryptHeader`)
- `Decode_ZeroDimensions_ThrowsInvalidDataException`
- `Decode_FakeTriPanelHeader_ReturnsLineDataType3AndIsMultiPanelTrue` (fabricated tiny blob for line_data_type=3 path)
- `Render_KnownSamplePercentileJpeg_ProducesValidJpegWithExpectedDimensions` (round-trip through JPEG encoder)
- `Render_AllZeroPixels_DoesNotDivideByZero`

Test fixture `TestData/Ase/sample_single_view.ase` is the smallest of the 10 samples from `services/image-splitter/tools/ase_format_research/samples/` (~1.7 MB). Bigger `line_data_type=3` samples are NOT committed to the test project.

All 9 tests pass in 191 ms.

### Rollout order (each step is one config change, full rollback in seconds)

1. **1.22.0 — land code, default `DllOnly`.** Production is byte-identical to 1.21.x. Cache rows start showing `ASE-Proprietary-to-JPEG-DLL` instead of the legacy `ASE-Proprietary-to-JPEG`; everything else unchanged.
2. **Step B — flip to `Shadow`.** Config-only. Watch `AseShadowCompare` log events for a day. Expect consistent LUT-shaped mean offsets; investigate any WARNING-level divergences (`meanDeltaFrac > 0.10`).
3. **Step C — flip to `FallbackOnFailure`.** Fallback only activates when the DLL throws. Previously-500ing scan views render successfully.
4. **Step D (future PR)** — flip to `FallbackOnly`, remove DLL files, strip `Assembly.LoadFrom` scaffolding.

### Explicit non-goals

- **Visual parity with vendor LUT.** Linear 1/99.5 percentile stretch only. The vendor's proprietary gamma / dual-energy colorization is out of scope.
- **`CompEncryptHeader` support.** Decoder throws with clear message pointing at the DLL for affected files. Zero such scans in current production.
- **FS6000 pipeline.** Completely separate; untouched.
- **Schema migration.** `ImageCache.ProcessingPipeline` is free-form varchar; new and old values coexist.
- **Removing `Ase.Image.dll`.** Step D, future PR.

### Verification

- `dotnet build src/NickScanCentralImagingPortal.Services.ImageProcessing/*.csproj` — 0 errors, 39 pre-existing System.Drawing.Common platform warnings
- `dotnet build src/NickScanCentralImagingPortal.Tests/*.csproj` — 0 errors
- `dotnet test --filter "FullyQualifiedName~AseFormat"` — **9/9 passing, 191 ms**
- API `/api/server/version` returns `1.22.0` after deploy
- Production renders ASE scans exactly as before (`Mode=DllOnly`, byte-identical)

### Commits

- `0e1c35eb` — Release 1.22.0 — C# ASE fallback + inspector polish + Excel/PDF export (24 files, +2041/-228)
- `ef017e17` — Merge 1.21.0 + 1.22.0 into main (joins parallel splitter session's work in `547e626b`..`f3dca996`)

---

## [1.21.0] — 2026-04-09 — X-Ray Inspector (unified ASE + FS6000 analysis workbench)

Ships a full-suite X-ray analysis workbench at `/validation/xray-inspector`
that handles both scanner types (ASE and FS6000) through pure-Python decoders —
no vendor DLL involved in the decode path.

### New page: `/validation/xray-inspector`

- Permission: `pages.validation.xrayinspector` (view), `xrayinspector.analyze`
  (ROI stats, edge detection, thresholding, object detection, dual-energy diff),
  `xrayinspector.export` (raw 16-bit PNG, CSV, PDF report)
- Unified scan picker searches both `asescans` and `fs6000scans` by container number
- Canvas-based viewer (`wwwroot/js/xrayInspector.js`) with pan/zoom/rotate,
  interactive window/level drag, live pixel readout, and ROI drawing
- Tool palette: pan, window/level, ruler, rectangle ROI, ellipse ROI, polygon ROI
  (double-click to close), line profile
- Display transforms: **raw 16-bit (default)**, CLAHE, percentile, log, gamma,
  window/level. Pseudocolor maps: hot, bone, jet, viridis, inferno, plasma
- Analysis tools: Canny/Sobel edge overlay, threshold mask, connected-component
  object detection, dual-energy difference, FS6000 vendor-style color composite
- Metadata panel: scan dimensions, bit depth, XML preview, linked
  `ContainerCompletenessStatus` records, one-click switch to the other scanner
  when the same container has both ASE and FS6000 scans
- Exports: raw 16-bit PNG, cooked 8-bit PNG, ROI statistics CSV, PDF report
  with annotations, vendor JPEG pass-through (FS6000)

### New Python backend: `services/image-splitter/inspector/`

Pure-Python X-ray decoding and analysis, mounted as a FastAPI blueprint at
`/inspector` in the existing image-splitter service. No vendor DLL required.

- `inspector/decoders/ase.py` — ASE `asescans.scanimage` decoder. 16-bit LE
  grayscale, tri-panel support (line_data_type=3), XML metadata + ContainerInfo
  binary block extraction
- `inspector/decoders/fs6000.py` — FS6000 `.img` decoder. 36-byte big-endian
  header (magic, dims, bit depth, timestamp), 16-bit BE high/low channels,
  native 8-bit material classification channel. Auto-flips the Y axis to
  match vendor display orientation
- `inspector/composite.py` — FS6000 dual-energy color compositing with a
  tuned customs-scanner palette (vibrant blue for dense/metal, warm orange
  for organics, near-neutral for air-gap noise). Also supports ASE tri-panel
  and dual-energy difference
- `inspector/analysis.py` — ROI masking (rect/ellipse/polygon/whole), ROI
  statistics with histograms, line profile with bilinear interpolation,
  Canny/Sobel edge detection, threshold masking, connected-component object
  detection
- `inspector/rendering.py` — PNG encoders. Default `render_raw_png` preserves
  native bit depth (16-bit for energy channels, 8-bit for material).
  `render_cooked_png` produces opt-in 8-bit transforms
- `inspector/data_access.py` — Postgres lookups (`asescans`, `fs6000scans`)
  and FS6000 network-share path resolution with PIC-number decoding
- `inspector/report.py` — PDF report generation via `fpdf2` with embedded
  preview, dual-energy composite, ROI stats table, and free-text notes
- `inspector/routes.py` — 14 FastAPI endpoints covering every workbench
  operation

### New C# controller: `XrayInspectorController`

Thin HTTP proxy that sits between the Blazor page and the Python service.
Owns NSCIM auth (`[Authorize(Policy = "Permission:...")]`), joins scan
records with `ContainerCompletenessStatus`, and forwards pixel/analysis
requests to the splitter via the existing `ImageSplitter` named HttpClient.

### Feature rule reinforced

The existing "16-bit lossless preferred" rule (previously ASE-only) is
generalized to cover both scanner types. The default pixel output across
the whole inspector is 16-bit PNG for 16-bit channels (ASE main, FS6000
high/low) and native 8-bit for FS6000 material. Cooked 8-bit transforms
are opt-in via `transform=clahe|percentile|log|gamma|window`.

### New dependencies

- Python: `fpdf2==2.8.1` (PDF report generation)

### No migrations

This release reads only from existing tables (`asescans`, `fs6000scans`,
`fs6000images`, `imagecaches`, `containercompletenessstatuses`,
`originalscanrecords`). No schema changes.

### Explicit non-goals

- Does NOT replace the existing DLL-based path in `ASEImageConverterService`
  (legacy pipeline continues to run unchanged)
- Does NOT modify the FS6000 ingestion pipeline (`FileSyncService`,
  `IngestionService`)
- Does NOT integrate with the AI splitter pipeline yet (the splitter stays
  a separate consumer; it can call the same Python decoders later)

### Verification

- Build: `dotnet build` clean on both `NickScanCentralImagingPortal.API`
  and `NickScanWebApp.New`, 0 errors
- End-to-end smoke test of all 22 endpoint permutations against real
  production data (ASE container `OOCU9515538` and FS6000 container
  `MRKU8707837`): **22/22 passing**
- Raw 16-bit ASE PNG verified as `mode='I;16'` via `PIL.Image.open()`
- FS6000 composite visually matches the vendor's blue/orange render at
  correct orientation (NCC ≈ 0.82 on aligned samples)

### Post-deploy hotfixes applied same day (2026-04-09)

During the live browser smoke test we caught and fixed four defects without
bumping the version:

1. **snake_case JSON deserialization** — the Blazor page's `ScanSearchDto` /
   `ScanDetailsDto` / `RoiStatsDto` were PascalCase but the Python splitter
   returns snake_case. `ApiService` has `PropertyNameCaseInsensitive=true`
   but no `SnakeCaseLower` naming policy. Fix: annotated every DTO property
   with `[JsonPropertyName("snake_case")]`. Verified live — the result card
   now shows `OOCU9515538` + scan time instead of em-dashes.

2. **Deep-link auto-search (new feature)** — the page now parses
   `?q=CONTAINER&scanner=ase&id=SCAN_UUID` on first render, kicks off
   `DoSearchAsync`, and auto-selects the matching result (or the only
   result if there's exactly one). Primary motivation: share a direct link
   to a specific scan from other NSCIM pages; secondary benefit: gives
   automated test harnesses a reliable way to drive the page without
   fighting MudTextField's non-deterministic synthetic event handling.

3. **Defensive try/catch around deep-link async work** — the auto-search
   + auto-select chain now runs via `InvokeAsync(...)` fire-and-forget on
   the next dispatcher tick (not synchronously inside `OnAfterRenderAsync`),
   and every step has its own try/catch that writes to a new `_deepLinkError`
   field displayed as a `MudAlert` at the top of the page. Any exception
   along the deep-link path is now visible to the operator instead of
   killing the Blazor circuit with a generic "An error has occurred" banner.

4. **Splitter-unavailable detection** — during the deploy I noticed the
   `NSCIM_ImageSplitter` Windows service had been stopped (probably by the
   initial 4-service stop sweep, then not restarted in the correct order).
   The WebApp surfaced a `503 Service Unavailable` from its controller's
   `ForwardGetJsonAsync` catch block. Restarting the splitter resolved it.
   No code change needed — the existing error message is correct and
   actionable.

### Known limitations

- **MudTextField + programmatic input events** — Chrome MCP's `type` and
  `form_input` actions don't reliably populate the container-number search
  field because MudBlazor's `@bind-Value` with `Immediate="true"` listens on
  an interop-routed channel, not plain DOM `input` events. Real user typing
  works; synthetic test events do not. The deep-link `?q=...` query param
  is the official workaround.

- **FS6000 first-access latency** — fetching a fresh FS6000 scan reads
  ~12 MB of `.img` files from the network share. Cold-cache latency is
  ~30–45 seconds per scan on the current dev mount. An LRU cache of
  decoded `Fs6000Image` instances in the Python process (keyed on scan
  UUID) would collapse this to milliseconds. Tracked as a follow-up; not
  a blocker.

- **Chrome MCP freeze on multi-MB base64 image load** — when the Blazor
  page loads a 16-bit PNG (~1.5 MB for ASE, ~8 MB for FS6000 high) into
  the canvas via `JS.InvokeVoidAsync("XrayInspector.loadImage", dataUrl)`,
  the Chrome extension's screenshot channel sometimes times out. The page
  itself continues to work; only the automation harness is affected. The
  fix is either (a) server-side URL streaming instead of base64 data URLs
  or (b) chunking the interop call. Not yet implemented. **FOLLOW-UP**: both
  known limitations were addressed in 1.22.0 Phase 1a (`ImageProxyController`
  streaming + true 16-bit client-side rendering with drag-speed W/L).

### Commits

- `b35c9002` — Release 1.21.0 — X-Ray Inspector (30 files, +5353 lines)

---

## [1.19.0] — 2026-04-08

Bundles four fixes and kicks off the Claude Vision teacher/student redesign
for the image splitter. Three data integrity bugs heal historical damage;
the fourth change is the foundation for measurably accurate splits.

### Fixed — CMR groupidentifier stuck state (650 rows healed)

`ContainerCompletenessService.CheckContainerCompletenessAsync` was treating
CMR clearance-type rows as valid Complete candidates even though CMR by
definition has no declaration number. That left the `groupIdentifier`
field as an empty string, which passed the `Status=Complete` check but
was then silently excluded by `IntakeWorker`'s
`!string.IsNullOrWhiteSpace(GroupIdentifier)` filter. Result: **650
containers invisible to analysts**, with images and scanner data but no
way to reach the analyst queue.

Fix: when the clearance type is CMR and the group identifier would be
empty, set `Status = "AwaitingDeclaration"` instead of `"Complete"`.
These rows now correctly wait for the CMR→IM/EX upgrade from 1.13.0
before being considered ready for analysis.

Cleanup SQL at `tools/migrations/cmr-and-crs-fixes/01-cmr-groupidentifier-cleanup.sql`
flipped all 650 existing stuck rows to the new state in-place.

### Fixed — Cross-record identification silently broken by 1.18.0

The 1.18.0 ASE comma-split fix had an unintended side effect:
`ContainerCompletenessOrchestratorService.RunPostICUMSValidationWorkflowAsync`
used to short-circuit on:

```csharp
if (alreadyCrossRecord || settledCount >= containerNumbers.Count)
    continue;
```

Before 1.18.0, a multi-container ASE scan produced one comma-joined CCS
row that rarely reached `Complete` status, so the `settledCount` branch
rarely fired. After 1.18.0, each container gets its own CCS row and
both reach `Complete` quickly, which made `settledCount >= containerNumbers.Count`
always true — and the validator started silently skipping every multi-container
scan it was supposed to check.

Measured impact: **318 multi-container `OriginalScanRecord` rows with
zero `CrossRecordScan` entries**. Every multi-container scan in the
database was being skipped (some of these were legitimately same-record
and would not produce a CRS, but they all should have been validated).

Fix: removed the `settledCount >= containerNumbers.Count` clause.
`alreadyCrossRecord` remains as an idempotency dedup. The next worker
tick will pick up the 318-row backlog and produce proper CRS entries
where warranted.

### Fixed — Image splitter restricted to cross-record scans only

`services/image-splitter/submit_backlog.py` was hardcoded to pull from
`crossrecordscans`. Combined with the CRS bug above, this meant the
splitter only ever saw 48 scans out of ~291 eligible multi-container
scans in `asescans` alone (~83% invisible). Cross-record is a
substantially narrower concept than "multi-container" — it only fires
when the two containers belong to DIFFERENT importers, which is rare.
Most multi-container scans are same-record and should still be split.

Rewrote `submit_backlog.py` to query `OriginalScanRecord WHERE
DerivedRecordCount >= 2` — the scanner-agnostic source of truth for
multi-container scans. `CrossRecordScan` is no longer consulted as a
filter. New CLI flags: `--limit N`, `--order oldest|newest`, `--dry-run`.

### Added — Claude Vision strategy for the image splitter (teacher)

New `strategies/claude_vision.py` integrates the Anthropic Claude Vision
API (`claude-sonnet-4-5` by default) as a splitting strategy. When an
`ANTHROPIC_API_KEY` env var is set, this strategy runs first in the
pipeline and its result becomes the primary split. When the key is not
set, the strategy silently returns None and the hand-engineered legacy
strategies run as before.

Design points:
- Image downsampled to 1568px on the long edge before sending to
  Claude (Anthropic's recommended max to avoid pointless token spend)
- Structured JSON response with `split_x`, `confidence`, `reasoning`
- Coordinate rescaled back to the original image space
- 0.95 confidence haircut so analyst-annotated ground truth still beats
  Claude in consensus scoring
- Full token usage + cost audit stored in result metadata
- Async-safe (wraps the sync anthropic client in `asyncio.to_thread`)
- Cost: ~$0.003–$0.008 per call. Latency: ~3–5 seconds.

The pipeline orchestrator was updated so that when Claude runs
successfully, it becomes the primary and all other strategies still run
for comparison telemetry (we want to measure steel_wall_midpoint vs
Claude on every image we have). When Claude is not configured, the
legacy short-circuit behaviour is preserved — `steel_wall_midpoint`
wins and remaining strategies are skipped.

New denormalised columns on `image_split_jobs`:
`claude_vision_split_x`, `claude_vision_confidence`,
`claude_vision_reasoning`, `claude_vision_input_tokens`,
`claude_vision_output_tokens`, `claude_vision_latency_ms`,
`claude_vision_model`, `claude_vision_ran_at`. Migration script at
`services/image-splitter/migrations/002_add_claude_vision_columns.sql`.

### Added — `bootstrap_claude_vision.py` — first-100 bootstrap script

New script at `services/image-splitter/bootstrap_claude_vision.py` runs
Claude Vision across the first N multi-container scans (default 100,
oldest first) to establish a ground-truth baseline. For each candidate
scan it either:

1. Submits it to the splitter if it has no existing job (the full
   pipeline runs including Claude Vision)
2. Re-runs JUST the Claude Vision strategy in-process against the
   existing job's stored image bytes and updates the denormalised
   columns + results table

After running, the analyst `/annotate` page will show Claude's split
alongside the hand-engineered strategies for every scan in the set.
Analysts can mark ground truth, and we can measure Claude's accuracy
against the analyst directly.

**Requires `ANTHROPIC_API_KEY` env var to be set on whatever host runs
the script.** Script exits immediately with a clear error if missing.

### Operational notes

- Migrations applied in order: `001_cmr_groupid_cleanup` (to production),
  then `002_claude_vision_columns` (to splitter DB)
- Rollback of the C# fixes: revert the commit. Rollback of the CMR
  cleanup: `UPDATE containercompletenessstatuses SET status = 'Complete'
  WHERE status = 'AwaitingDeclaration' AND clearancetype = 'CMR'`.
- The 318 CRS-backlog scans will be processed automatically by the next
  `RunPostICUMSValidationWorkflowAsync` tick after deploy (no manual
  action needed).
- To run Claude Vision on the first 100 scans:
  ```
  set ANTHROPIC_API_KEY=sk-ant-...
  set NICKSCAN_DB_PASSWORD=...
  set NSCIM_API_TOKEN=...
  cd services/image-splitter
  .\venv\Scripts\python bootstrap_claude_vision.py --limit 100
  ```

### Commits in this release

- `<TBD>` — Release 1.19.0: CMR fix + CRS fix + splitter broadening + Claude Vision foundation

---

## [1.18.0] — 2026-04-08

Critical ingestion bug fix discovered during a user report. Two containers
(`GCNU1350557` + `BMOU2828552`) showed up in the analyst queue as a single
group whose ICUMS panel was empty. Investigation revealed a systemic
corruption affecting **106 analysisrecords, 25 analysisgroups, 263
containercompletenessstatuses, and 2,629 containerscanqueues rows**, produced
continuously from 2026-03-18 through 2026-04-08.

### Root cause

The ASE scanner source database stores `"C1, C2"` as a single string in
`containernumber` when an inspection covers multiple containers (a truck
carrying two 20ft boxes past the portal in one event). This is legitimate at
the `asescans` table — it's the verbatim audit trail from the source.

But the completeness pipeline (`containerscanqueues` →
`containercompletenessstatuses` → `analysisgroups` → `analysisrecords`) is
keyed on a single container number. Every downstream BOE lookup does
`WHERE containernumber = 'C1, C2'`, which never matches anything in
`boedocuments`. The analyst sees an assigned group with an empty ICUMS panel.

`ContainerDataMapperService.GetPendingMappingsAsync` already had splitting
logic, but it only ran at the `ContainerBOERelation` layer — far too late in
the chain. The queue publish step in `AseDatabaseSyncService` was forwarding
the joined string verbatim to `containerscanqueues`, and every subsequent
service was dutifully propagating it.

### Fixed — `AseDatabaseSyncService.SplitAseScanIntoQueueItems`

- New helper splits the container number at the queue publish step so each
  physical container gets its own `ContainerScanInfo` queue item.
- Single-container scans pass through unchanged (fast path).
- Multi-container scans produce N queue items with InspectionId suffixes
  `-a`, `-b`, ... to preserve queue uniqueness.
- `Unknown` and empty tokens dropped.
- Deduped case-insensitively within the same scan.
- Original joined string preserved in the queue metadata field as
  `OriginalContainerNumber` + `MultiContainerScan: true` for audit.
- `asescans.containernumber` stays verbatim as the source-of-truth audit
  trail. Nothing upstream of the queue publish changes.

### Cleanup applied to production

New script at `tools/migrations/ase-comma-split/01-cleanup-comma-corruption.sql`:

1. Released all active analyst assignments on corrupt groups (unblocks the
   queue so analysts stop seeing empty ICUMS panels immediately).
2. Deleted all `imageanalysisdecisions` with comma-concatenated container
   numbers (prevents AI training flywheel from exporting nonsense).
3. Deleted the 106 corrupt `analysisrecords`.
4. Deleted the 25 corrupt `analysisgroups`.
5. Deleted the 263 corrupt `containercompletenessstatuses` rows.
6. Marked the 2,629 corrupt `containerscanqueues` rows as `Cancelled` with
   an error message noting the 1.18.0 cleanup.
7. Left `asescans` untouched (audit trail — the 291 corrupt rows remain as
   historical source data).

### What happens next, automatically

- The 1.18.0 fix stops producing new corrupt rows immediately.
- `RecordReconciliationWorker` (1.14.0) will pick up the individual
  containers on its next 30-minute tick from the ICUMS BOE downloads and
  create proper per-container `RecordCompletenessStatus` + `RecordExpectedContainer`
  rows.
- `RunRecordAnchoredIntakeAsync` (1.16.0) will then create properly-split
  `AnalysisGroup`s from those records on the next intake cycle.
- Analysts will see the containers in their queue correctly, each with its
  own BOE data and its own decision flow.

**The specific pair `GCNU1350557` + `BMOU2828552` from the user report is
fully cleaned up: group, record, assignment, CCS row all deleted. Each
container has its own `RecordCompletenessStatus` (1838 and 3352) from the
1.14.0 backfill, and will get a fresh AnalysisGroup when scanner events
arrive or via the record-anchored intake.**

### Operational notes

- The cleanup script is idempotent — re-runnable safely.
- Rollback: revert the code commit, no automatic restore of deleted data
  (a schema-level backup taken per `feedback_publish_wip.md` is the
  recovery path if needed).
- Watch for any new corrupt rows appearing after deploy — if the ASE sync
  is processing records created after the last `_lastSyncedInspectionId`,
  they'll flow through the new split path immediately.

### Commits in this release

- `<TBD>` — Release 1.18.0: ASE comma-split at queue publish + cleanup

---

## [1.17.0] — 2026-04-08

Legacy cleanup release. Marks the container-grain wave entities as obsolete,
deletes 1,206 lines of dead standalone worker code, and cleans up stale DI
registration comments. No runtime behaviour change — the marked entities are
still dual-written by the legacy paths so the record-first flow from 1.16.0
continues to work without disruption. Future releases will drop the marked
tables once all consumers migrate.

### Marked obsolete

- `AnalysisParentGroup` class — superseded by `RecordCompletenessStatus`.
  Still dual-written by wave processing for backward compat.
- `WavePendingContainer` class — superseded by `RecordExpectedContainer`
  which carries the full state machine. Still dual-written.
- `AnalysisGroup.ParentGroupId` field — use `RecordCompletenessStatusId`
  as the canonical parent.

These [Obsolete] attributes generate 62 CS0618 compiler warnings at the
existing consumer sites. Those are intentional and informational — they
flag exactly the call sites that will need to migrate before the legacy
tables can be dropped. The build still succeeds; no runtime impact.

### Deleted

- `src/NickScanCentralImagingPortal.Services/ImageAnalysis/IntakeWorker.cs`
  (661 lines). Was already marked `[Obsolete]` and not registered in DI.
  Its logic lives in `ImageAnalysisOrchestratorService.RunIntakeWorkflowAsync`.
- `src/NickScanCentralImagingPortal.Services/ImageAnalysis/HousekeepingWorker.cs`
  (545 lines). Same story — dead, unreferenced, consolidated into the
  orchestrator. Its logic lives in `RunHousekeepingWorkflowAsync`.
- Stale commented-out DI registrations in `ServiceConfiguration.cs` for the
  five individual image-analysis workers (replaced with a note pointing at
  the orchestrator).

Total deleted: **~1,210 lines of dead code**. Every remaining line in the
ImageAnalysis folder is now live code.

### Date-based group splitting hack — removed by file deletion

The `_YYYYMMDD_YYYYMMDD` GroupIdentifier suffix hack from the old
IntakeWorker (which prevented containers scanned >30 days apart under the
same master BL from being grouped together) was only present in the deleted
standalone IntakeWorker.cs file. The orchestrator's intake flow has never
used it — with record-grain grouping, each ICUMS declaration naturally
becomes its own record and date splitting is unnecessary.

### What's NOT dropped (explicit non-goals for 1.17.0)

- Schema columns on `containercompletenessstatuses` are unchanged.
  `GroupIdentifier`, `BOEDocumentId`, `ClearanceType`, `IsConsolidated`,
  `TotalHouseBLs`, `CompleteHouseBLs`, `ConsolidationDetails`,
  `WorkflowStage` all stay in place because ~38 files still read them.
  A full consumer audit + drop will happen in a later release.
- `analysisparentgroups` and `wavependingcontainers` tables are unchanged.
  Still populated by the dual-write paths. Drops wait for the consumer
  audit.
- `AnalysisGroup.ParentGroupId` column stays in place.

### Operational notes

- No schema changes.
- Rebuild from a clean checkout will produce 62 CS0618 warnings. Expected
  and intentional.
- Rollback: revert the commit, undelete the two files via git. Low risk.

### Commits in this release

- `<TBD>` — Release 1.17.0: [Obsolete] markers + dead code removal

---

## [1.16.0] — 2026-04-08

Record-anchored intake + wave dual-write. Continues the Option C pivot by
promoting the record table from "parallel shadow" (1.14.0/1.15.0) to "primary
driver" for new intake flows and keeping the legacy wave paths in lockstep
via dual-write. The old container-grouping pass still runs as fallback.

### Added — Record-anchored intake pass

- New `RunRecordAnchoredIntakeAsync` runs at the top of `RunIntakeWorkflowAsync`
  on every tick. Queries `RecordCompletenessStatus` rows where:
  - `Status = Ready` (all expected containers ready), OR
  - `Status = PartiallyReady` AND `ContainersReady >= WaveMinBatchSize`
  - AND `ArchivedAtUtc IS NULL`
  - AND no existing `AnalysisGroup` is linked via `RecordCompletenessStatusId`
- For each eligible record, creates an `AnalysisGroup` directly from the
  record + `AnalysisRecord` rows from the ready children. Populates the FK
  on creation so 1.15.0's decision rollup dual-write picks it up immediately.
- For `PartiallyReady` records, also seeds the legacy `AnalysisParentGroup` +
  `WavePendingContainer` rows so the existing wave scanner handles the
  not-yet-ready containers without a second code path.
- Best-effort: individual record failures are logged and skipped, not fatal.
- Duplicate detection via `IsUniqueConstraintViolation` handles the race
  where the legacy container-grouping pass creates the same group
  concurrently.

### Added — Wave dual-write

- `RunPartialWaveScanAsync` mirrors AwaitingScan/Pending → Ready promotions
  into `RecordExpectedContainer` so the record view reflects the same state
  the wave system sees. Wraps the mirror in a try/catch so record-side
  failures are non-fatal.
- `AutoCloseExpiredWaveParentsAsync` mirrors the auto-close into the record
  table: linked `RecordCompletenessStatus` gets `Status=Archived` +
  `ArchivalReason=WaveAutoClosed`, and any remaining AwaitingScan/Pending
  children flip to `NoScanReceived`/`NoImageAvailable` respectively. The
  legacy parent close path is unchanged.

### Behavior notes

- The record-anchored pass runs FIRST, so records become the preferred
  source for new groups. The legacy container-grouping pass still runs
  afterwards and handles any containers whose records don't yet exist
  in `RecordCompletenessStatus` (pre-1.14.0 data, edge cases).
- No schema changes in this release. Everything is code-level.
- Record-anchored groups are keyed by `DeclarationNumber` as the
  `GroupIdentifier`, which matches the non-consolidated convention used by
  the legacy path. Match Corrections and Analyst Assignment keep working
  unchanged because they look up groups by identifier.

### Operational notes

- After deploy: watch `[INTAKE-RECORD]` log lines for "Created AnalysisGroup
  from record ..." on the next intake tick. Zero if no eligible records,
  positive if the reconciliation worker has promoted records into Ready
  state.
- The linkage rate (`AnalysisGroup.RecordCompletenessStatusId != null`)
  will climb over time as new groups are created record-first.
- Rollback: revert the commit, redeploy 1.15.0. No schema changes to undo.

### Commits in this release

- `<TBD>` — Release 1.16.0: record-anchored intake + wave dual-write

---

## [1.15.0] — 2026-04-08

Option C pivot release. Builds on the 1.14.0 record foundation to surface the
integrity gap in the operator UI, link AnalysisGroups to RecordCompletenessStatus,
and dual-write decision rollups into the new record table. The old container-grain
pipeline continues to run; nothing is ripped out. The path is set for 1.16.0/1.17.0
to deprecate the legacy grain entirely.

### Added — Record Completeness Blazor page

- New page `/validation/record-completeness` showing declaration-level records
  with their full expected-container sets. Filter tabs: Active, Partially Ready,
  Ready, Pending, Archived, All. Search by declaration / BL / rotation.
  Expand-drawer shows per-container state chips.
- Backed by new `RecordCompletenessController` with `/api/recordcompleteness`,
  `/api/recordcompleteness/{id}`, `/api/recordcompleteness/by-declaration/{n}`,
  and `/api/recordcompleteness/summary`. All AdminOnly policy `ImageAnalyst`.
- New permission `pages.validation.recordcompleteness` seeded into prod for
  Analyst, Audit, Supervisor, Manager, Admin, SuperAdmin (6 roles).
- Deep-linkable via `?status=PartiallyReady&declaration=...`.
- Added to NavMenu under the Validation group.

### Added — Record Integrity Banner on ContainerCompleteness.razor

- Prominent banner at the top of the 2,052-line Operations page showing record
  counts, multi-container count, partially-ready count, and integrity gap in
  containers. Links directly to `/validation/record-completeness?status=PartiallyReady`.
- Border colour flips from warning to success based on whether any gap exists.
- Loaded via the new summary endpoint, auto-refreshes on the existing 30s timer.
- Zero changes to the underlying container-grain table logic; strictly additive.

### Added — AnalysisGroup.RecordCompletenessStatusId FK

- New nullable column on `analysisgroups`. Partial index when not null.
- Backfill SQL matches existing AnalysisGroups to the new record table by
  GroupIdentifier/NormalizedGroupIdentifier = DeclarationNumber. Result:
  **915 of 1,611 existing groups linked** (57%). The remainder are Pattern A
  (container-keyed) or date-split groups that will stay unlinked legacy until
  1.16.0.
- New `TryLinkGroupToRecordAsync` helper called after every group
  creation/merge in `RunIntakeWorkflowAsync` so all future groups get the FK.
- `ProcessWaveIntakeAsync` populates the FK at group-creation time for wave
  groups.

### Added — Wave monitor extension

- `GET /api/imageanalysis-management/wave-monitor` response extended with
  record-level fields: `RecordCounts`, `RecordIntegrityGapContainers`,
  `RecordReconciliationLastTickAtUtc`, `RecordReconciliationWatermarkUtc`,
  `RecordReconciliationContainersPromotedTotal`.
- Surfaces on PANEL 3.5 of `ImageAnalysisManagement.razor` so operators can see
  the reconciliation worker's health next to the wave processing counts.

### Fixed — Decision side effects now dual-write record rollup

- `DecisionSideEffectsService.ApplyAsync` gained a new step 3c that, when the
  group has `RecordCompletenessStatusId` set, flips the matching
  `RecordExpectedContainer` row to `Decided` and recomputes the parent
  `RecordCompletenessStatus` counts. Derives parent Status from child state
  (Submitted→Completed, Decided→InAudit, etc.).
- Dual-writes alongside the existing `AnalysisParentGroup` rollup — the old
  path is unchanged, the new path runs in parallel. Best-effort: failures log
  a warning but do not block the analyst's decision save.

### Legacy grain marked obsolete

- `AnalysisGroup.ParentGroupId` documented as legacy, scheduled for removal
  in 1.17.0.
- `AnalysisParentGroup` and `WavePendingContainer` remain functional for
  backward compat — no behaviour change. 1.16.0 will pivot the wave processing
  to read from `RecordExpectedContainer` and deprecate the old tables.

### Migrations applied

- `tools/migrations/record-completeness/03-link-analysisgroups-to-records.sql`
  — adds `recordcompletenessstatusid` FK + partial index + backfill.

### Operational notes

- No EF Core migration in this release (the FK is added via raw SQL to match
  the existing migration pattern for this slice).
- `/api/diagnostics/record-completeness` from 1.14.0 continues to work and is
  still the source of truth for integrity-gap metrics.
- After deploy: verify the Operations page shows the new banner, and
  `/validation/record-completeness` loads with 85 PartiallyReady records.

### Commits in this release

- `<TBD>` — Release 1.15.0: Option C pivot — record view UI + AnalysisGroup linkage + dual-write rollup

---

## [1.14.0] — 2026-04-08

Record Completeness foundation release. Addresses a critical data-integrity
issue discovered while cross-checking NSCIM against ICUMS: multi-container
declarations were routinely arriving at image analysis with a fraction of
their expected containers, and the system had no way of knowing anything was
missing. The smoking-gun case was declaration `40126052701` — a 20-container
gypsum-powder shipment showing in NSCIM as a 1-container record marked
"Complete". 53% of multi-container declarations NSCIM had any visibility on
were in the same partial state.

**This release introduces a proactive, record-anchored completeness model
that pre-populates expected containers from the ICUMS download feed, so
missing containers become visible *before* they would have been missed.**

### Added — `RecordCompletenessStatus` canonical record state

- New `RecordCompletenessStatus` entity: one row per ICUMS declaration,
  keyed by `DeclarationNumber` (globally unique customs filing identifier).
- Carries rollup counts for the full container lifecycle: `AwaitingScan`,
  `Pending`, `Ready`, `Decided`, `Submitted`, `NoImage`, `NoScan`.
- `Status` in `Pending | PartiallyReady | Ready | InAnalysis | InAudit | PendingSubmission | Submitted | Completed | Archived | Failed`.
- `ContainerGroupKey` field handles the "used cars in one container" case
  (Pattern A) where multiple declarations share a single physical container.
- Master BL is recorded for display only; **never used as an identifier**
  because unrelated customers can share a shipping contract.

### Added — `RecordExpectedContainer` child state

- New `RecordExpectedContainer` entity: one row per container the declaration
  expects, per ICUMS' container list.
- State machine: `AwaitingScan -> Pending -> Ready -> Decided -> Submitted`,
  with `NoImageAvailable` and `NoScanReceived` as terminal states.
- Pre-populated at record creation time with `Status=AwaitingScan` for
  every container in the BOE, so the integrity gap is known immediately.

### Added — `RecordReconciliationWorker` background service

- New 30-minute reconciliation loop:
  1. Pulls new/updated BOE rows from nickscan_downloads since the last watermark
  2. Upserts RecordCompletenessStatus rows per declaration
  3. Handles ICUMS amendments (new containers bump LastNewContainerAtUtc)
  4. Scans for newly-arrived scanner events and promotes AwaitingScan -> Pending, then Pending -> Ready when images exist
  5. Recomputes rollup counts and derives Status / WorkflowStage
  6. Applies the **30-day archive rule**
  7. Persists the watermark + throughput counters
- Configurable via new AnalysisSettings tunables: RecordReconciliationEnabled,
  RecordReconciliationIntervalMinutes, RecordArchiveAfterDays, RecordReconciliationBatchSize.
- 60-second initial startup delay so it doesn't fire during service startup.

### Added — GET /api/diagnostics/record-completeness endpoint

- AdminOnly diagnostics endpoint returning total records, breakdown by status,
  integrity gap metrics (multi-container fully/partially/all-missing buckets),
  reconciliation worker stats (last tick, throughput counters), and archive
  rule status. Before this release, measuring the integrity gap required
  manual cross-database SQL. Now it's a single API call.

### Added — Pattern A handling (shared-container declarations)

- New RecordCompletenessBuilder helper encodes the hybrid detection rule using
  declaration number as the primary identifier. Pattern A declarations
  (exactly 1 container, that container appears on multiple other declarations)
  get ContainerGroupKey = ContainerNumber and a DeclarationsJson metadata
  field with sibling declaration info for UI display.

### Migration applied to production

- **Schema**: tools/migrations/record-completeness/01-add-record-completeness-tables.sql
  creates the three new tables, adds 4 tunables to analysissettings, enables
  RLS matching the Phase 1 tenancy pattern.
- **Backfill**: two-phase script (02a-export-from-icums.sql + 02b-import-to-production.sql)
  that exports every IM/EX BOE container row from nickscan_downloads and builds
  records in nickscan_production with pre-reconciled state from existing
  ContainerCompletenessStatus data.

### Backfill results (2026-04-08 snapshot)

- **12,200 records** created
- **22,121 expected container rows**
- **2,387 multi-container records** (the integrity problem space)
- **1,172 Pattern A records** detected and flagged with ContainerGroupKey
- **2,213 expected containers immediately flipped to Ready** from existing
  ContainerCompletenessStatus state with HasImageData=true
- **85 records in PartiallyReady state** — the exact operational integrity
  gaps that were previously invisible
- **10,290 records in Pending** — the all-missing backlog ICUMS says exists
  but no scanner events have arrived for yet

**Smoking-gun verification**: declaration 40126052701 (the 20-container gypsum
shipment) now correctly shows totalExpected=20, awaiting=19, ready=1,
Status=PartiallyReady — exactly the transparency this release was built for.

### Zero behaviour change to existing pipelines

- ContainerCompletenessStatus, ContainerCompletenessService,
  AnalysisParentGroup, WavePendingContainer, IntakeWorker, SubmissionWorker,
  ImageAnalysisOrchestratorService, and all other existing code paths run
  **completely unchanged**.
- 1.14.0 is strictly additive: new tables, new service, new endpoint.
- 1.15.0 will pivot the wave processing to read from the new tables and
  surface the record view in the operator UI.
- 1.16.0 will deprecate the old container-grain pipeline.

### Operational notes for the deploy

- Migrations applied before publish as always.
- After deploy: hit /api/server/version (expect 1.14.0), then
  /api/diagnostics/record-completeness to see the integrity gap.
- Watch the worker logs `[RECORD-RECON]` for the first tick ~60 seconds
  after startup, then every 30 minutes.
- The 85 PartiallyReady records are real integrity gaps that have existed
  in production for weeks. They become visible in the operator UI in 1.15.0.

### Files added

- src/NickScanCentralImagingPortal.Core/Entities/RecordCompletenessStatus.cs
- src/NickScanCentralImagingPortal.Core/Entities/RecordExpectedContainer.cs
- src/NickScanCentralImagingPortal.Core/Entities/RecordReconciliationState.cs
- src/NickScanCentralImagingPortal.Services/RecordCompleteness/RecordCompletenessBuilder.cs
- src/NickScanCentralImagingPortal.Services/RecordCompleteness/RecordReconciliationWorker.cs
- tools/migrations/record-completeness/01-add-record-completeness-tables.sql
- tools/migrations/record-completeness/02a-export-from-icums.sql
- tools/migrations/record-completeness/02b-import-to-production.sql

### Files modified

- ApplicationDbContext.cs (DbSet registrations + model config)
- AnalysisSettings.cs (4 new tunables)
- Program.cs (BackgroundService registration)
- DiagnosticsController.cs (new endpoint + DTOs)
- CHANGELOG.md, Directory.Build.props

### Commits in this release

- `<TBD>` — Release 1.14.0: Record Completeness foundation + reconciliation worker

---

## [1.13.0] — 2026-04-08

CMR→IM/EX lifecycle hardening release. While cross-checking NSCIM's local
ICUMS data against the GRA-published Tema yellow-channel statistics, an
audit of the `boedocuments` table surfaced two CMR-lifecycle issues that
have likely been latent since the lifecycle service was first deployed.
This release fixes the underlying upgrade flow, captures provenance so
upgrades become auditable, backfills the existing stuck rows, and adds a
diagnostics endpoint so the lifecycle can be watched over time.

### Background — what CMR actually means in ICUMS

Documenting this in the changelog because both the team and outside
analysts get it wrong on first contact:

`CMR` in ICUMS context = **Cargo Movement Record**, NOT the WCO road-
transit convention. A CMR row in `boedocuments` means **two things at
once**:
1. **Manifest-only data — no customs declaration filed yet.**
   `declarationnumber` is NULL on 97.6% of CMR rows for this reason.
2. **Tracks intra-port physical movements between terminals.** The
   `deliveryplace` column carries an intra-Tema-port terminal code
   (`WTTMA1MPS3`, `WITMA1TRST`, etc.), not a destination country.

CMR is a **lifecycle stage**, not a cargo category. Every container that
ever gets declared starts as CMR and upgrades to IM (or EX) when the BOE
arrives. The existing `cmr_boe_lifecycle.md` design captures this; what
this release fixes are the gaps in the upgrade flow.

### Fixed — 998 half-state CMR rows that the upgrade service couldn't catch

Discovered during the lifecycle audit: ICUMS sometimes emits messages
with `clearancetype = 'CMR'` that **already carry** a populated
`declarationnumber`, `regimecode`, and `crmslevel`. These look like CMR
rows on the surface but they're fully-formed import declarations riding
on the cargo-movement document type. The existing CMR→IM lifecycle
service in `IcumJsonIngestionService` never gets a chance to upgrade
them because no separate IM message ever arrives — the IM data is
already inside the CMR message.

In production at the time of release: **998 such rows** all with valid
import-side WCO regime codes (40 home use, 70 warehousing, 80 inward
processing, 90 other). The backfill SQL flips them to `clearancetype =
'IM'` while preserving their CMR origin via the new provenance columns.

### Added — CMR upgrade provenance columns

Two new nullable columns on `boedocuments`:

| Column | Type | Meaning |
|---|---|---|
| `originalclearancetype` | `varchar(20)` | Clearance type at first ingest, before any upgrade. NULL = current type is original. |
| `cmrupgradedat` | `timestamptz` | When the upgrade fired. NULL = never upgraded. |

Both nullable so the migration is non-blocking on the existing 62k+ rows.
Set ONCE on first upgrade — `UpdateExistingDocumentAsync` uses
`COALESCE(existing, new)` semantics in the SQL so a re-upgrade never
overwrites first-upgrade provenance. Lightweight partial index
`idx_boedocuments_cmrupgradedat WHERE cmrupgradedat IS NOT NULL` keeps
diagnostics queries fast without bloating the index.

### Added — Implicit upgrade handler in `IcumJsonIngestionService`

New code path between the intra-file dedup check and the existing
explicit CMR→IM lifecycle check (around line 567 of
`IcumJsonIngestionService.cs`). When the parser sees a `CMR`-typed
message that already has a non-empty `DeclarationNumber` AND a non-empty
`RegimeCode`, it:

1. Inspects the regime code prefix (`4/7/8/9` → IM, `1/2` → EX, otherwise
   leave as CMR).
2. If the inferred type is IM or EX, flips `boeDocument.ClearanceType`
   in memory before the rest of the ingest pipeline runs.
3. Stamps `OriginalClearanceType = "CMR"` and `CmrUpgradedAt =
   DateTime.UtcNow` on the document.
4. Logs `[CMR→IM IMPLICIT]` so the upgrade is greppable in prod logs.
5. Continues through the normal save path — which now writes the new
   clearance type AND the provenance columns through the COALESCE-
   protected UPDATE statement.

This stops the half-state pile from accumulating going forward. Existing
998 rows are handled by the backfill below.

### Added — `GET /api/diagnostics/cmr-lifecycle` endpoint

AdminOnly diagnostics endpoint on `DiagnosticsController` returning a
`CmrLifecycleHealth` payload:

- `pendingCmrTotal` — total rows currently at `clearancetype='CMR'`
- `pendingCmrWithoutDeclaration` — the "classic" pre-declaration backlog
- `stuckHalfStateCmr` — should be **0** after this release; counts CMR
  rows with a non-empty declaration number (the implicit upgrade handler
  should catch all of these on ingest)
- `oldestPendingCmrCreatedAt` and `oldestPendingAgeHours` — how long the
  oldest unresolved CMR has been waiting
- `upgradedTotal` — total rows that have ever been upgraded (provenance
  set)
- `upgradedLast24Hours` and `upgradedLast7Days` — flow rate
- `upgradeBreakdown[]` — counts grouped by `(originalClearanceType,
  currentClearanceType)`, so you can see e.g. `CMR→IM = 998, CMR→EX = 0`

This is the diagnostic that lets operators actually see the lifecycle
working. Without provenance columns there was no way to look back and
say "the upgrade fired N times yesterday" because the old in-place
upgrade left no trace.

### Updated — `UpgradeCMRToBOEAsync` (explicit lifecycle path)

When the explicit lifecycle path fires (i.e. an IM/EX message arrives
that matches a prior CMR row by `(container, rotation, BL)`),
`IcumDownloadsRepository.UpgradeCMRToBOEAsync` now stamps the existing
row's clearance type into `OriginalClearanceType` and sets
`CmrUpgradedAt = DateTime.UtcNow` on the upgraded document **before**
calling `UpdateExistingDocumentAsync`. The COALESCE in the UPDATE then
guarantees these are written exactly once.

### Migrations introduced

- `tools/migrations/cmr-upgrade-provenance/01-add-cmr-upgrade-columns.sql`
  — adds the two columns + partial index. Idempotent.
- `tools/migrations/cmr-upgrade-provenance/02-backfill-half-state-cmr.sql`
  — backfills the 998 half-state rows. Idempotent.

Both apply to the **`nickscan_downloads`** database (the
`IcumDownloadsDbContext` target), not the main NSCIM application
database. No EF Core migration in this release because the EF tooling
can't reach a design-time `IcumDownloadsDbContext` factory in this repo;
direct SQL is the established pattern for this DB anyway.

### Operational notes for the deploy

- **Apply both SQL scripts before publish** (per `feedback_publish_wip.md`).
  Order: `01-add-cmr-upgrade-columns.sql` then `02-backfill-half-state-cmr.sql`.
  Both completed in <500 ms on the production snapshot.
- After deploy: hit `/api/server/version` for `1.13.0`, then hit
  `/api/diagnostics/cmr-lifecycle` to see the post-backfill state.
  Expected `stuckHalfStateCmr = 0`, `upgradedTotal ≥ 998`, breakdown
  shows `CMR→IM = 998`.
- Watch `upgradedLast24Hours` over the next few days — once the implicit
  handler starts catching ICUMS' CMR-with-declaration messages on ingest,
  this number should creep up. Each one represents a row that **would
  have** become a stuck half-state row in 1.12.0 and earlier.
- Existing operator workflows are unaffected. Rows that flipped from
  CMR to IM in the backfill now correctly appear in IM analytics
  (regime 40/70/80/90 imports), which means yellow-channel and other
  per-regime stats may show small positive deltas — these are
  corrections, not regressions.

### Commits in this release

- `<TBD>` — 1.13.0: CMR upgrade provenance + implicit upgrade handler + diagnostics

---

## [1.12.0] — 2026-04-08

Closes the AI training flywheel chapter. Three deferred items from 1.11.0
land in this release: Gap 2 backfill, Gap 2 column deprecation, and the
BOE Lookup feature for analysts. While investigating Gap 2 a latent bug
in the existing dual-write was discovered and fixed: it had been silently
writing zero typed annotations since 1.10.0 due to a JSON case-sensitivity
mismatch.

### Fixed — Gap 2 dual-write was writing zero rows since 1.10.0

- Root cause: `ImageAnalysisDecisionController.SaveDecision` parsed
  `SuspiciousAreas` JSON looking for lowercase keys (`x` / `y` / `width` /
  `height`), but every row written by the existing UI uses **PascalCase**
  keys (`X` / `Y` / `Width` / `Height`). `System.Text.Json` is case-
  sensitive by default, so every box silently failed every property check
  and the dual-write produced 0 typed `ContainerAnnotation` rows for
  every decision saved between 1.10.0 and 1.11.0. Verified directly:
  `SELECT COUNT(*) FROM containerannotations WHERE imageanalysisdecisionid IS NOT NULL`
  returned 0 in production.
- Fix: new `TryReadDouble(JsonElement obj, string lowerKey)` helper does
  a case-insensitive lookup that tries the lowercase form first then a
  PascalCase fallback, and accepts both `Number` and numeric `String`
  JSON values. Also dropped the previous precondition that required a
  `ThreatCategoryId` or `RevenueAnomalyCategoryId` to be set on the
  decision: historical decisions and uncategorised newer ones still need
  typed rows so the JSON column can eventually be retired (the COCO
  export already filters by category at read time).

### Added — Gap 2 historical backfill

- New idempotent SQL backfill at
  `tools/migrations/gap2-backfill/backfill_suspicious_areas.sql`. Walks
  every `imageanalysisdecisions` row with a non-empty `SuspiciousAreas`
  JSON blob and emits one `ContainerAnnotation` row per rectangle linked
  via `imageanalysisdecisionid`. Skips any decision that already has
  typed annotations linked (re-runnable). Categories on the parent
  decision propagate onto each box.
- Applied to production: **48 historical decisions → 61 typed rows**.
  Confirmed via `SELECT COUNT(DISTINCT imageanalysisdecisionid) FROM
  containerannotations WHERE imageanalysisdecisionid IS NOT NULL` = 48,
  matching `SELECT COUNT(*) FROM imageanalysisdecisions WHERE
  suspiciousareas IS NOT NULL AND length(suspiciousareas) > 2` = 48.

### Changed — `ImageAnalysisDecision.SuspiciousAreas` is now `[Obsolete]`

- Marked with `ObsoleteAttribute` and a clear migration message. The
  column is still written by `SaveDecision` and still read by the legacy
  Blazor draw tools and image overlay components for backward compat;
  the canonical store is now `ContainerAnnotation` rows linked via
  `ImageAnalysisDecisionId`. The column will be dropped in a future
  release once UI components migrate to query `ContainerAnnotation`
  directly. Existing call sites keep working but generate CS0618 warnings
  to motivate the migration.

### Fixed — COCO export was about to double-emit every backfilled box

- After the Gap 2 backfill the COCO export's previous logic would have
  emitted every annotation **twice**: once from the typed
  `ContainerAnnotation` rows (correctly linked via decision id) and
  again from `EmitLegacySuspiciousAreas` parsing the same boxes out of
  the JSON column on the decision row.
- Fix: `CocoExportService` now keys typed annotations primarily by
  `ImageAnalysisDecisionId` (canonical post-backfill) with a container-
  keyed fallback for any free-floating rows. The legacy
  `SuspiciousAreas` emission only fires when there are **no decision-
  linked typed rows** for the decision id, eliminating the double-count.
  Helper logic refactored into a new `EmitTypedAnnotations` method.

### Added — BOE Lookup page (analysts and above)

- New `/validation/boe-lookup` Blazor page. Universal cargo / BOE search
  across container number, declaration number, BL number, master BL,
  house BL, rotation number, and **VIN** (via the `VehicleImports`
  table). Each result row shows the matched field and clicks through to
  the standard `/containers/{ContainerNumber}` details view.
- Backed by new `GET /api/cargogroup/lookup?q=...&limit=...` endpoint on
  `CargoGroupController`. Case-insensitive ILIKE; min 3-character query;
  default limit 50, max 200; results truncated with a warning chip.
  Returns `CargoLookupResponse` with `Query`, `TotalReturned`, `Limit`,
  `Truncated`, and `Results[]`.
- New permission `pages.validation.boelookup`. Granted in code via
  `PermissionSeeder.GetAnalystPermissions()` (Audit and above inherit it
  through the existing role hierarchy). Seeded directly into
  `rolepermissions` for the production deployment so existing Analyst,
  Audit, Supervisor, Manager, Admin and SuperAdmin roles immediately get
  the new page without waiting for the next seeder run.
- API endpoint guarded by `[Authorize(Policy = "ImageAnalyst")]` (added
  `PagesValidationBoeLookup` to `analystPermissions` in `Program.cs`).
- Cross-link added: `/validation/match-corrections` now has a "BOE
  Lookup" button next to "Manual flag" so admins on a match-correction
  flow can pivot to a free-form search without losing context. The new
  page is also added to `NavMenu.razor` under the Validation group.
- Deep-linkable via `?q=...` query string for sharable lookups.

### Migrations introduced

- No EF Core migration in this release. The Gap 2 backfill is a one-off
  SQL script committed under `tools/migrations/gap2-backfill/`. The new
  permission row is seeded by `PermissionSeeder` on next startup; the
  manual SQL script applied during deploy grants it to existing roles
  without waiting.

### Operational notes for the deploy

- **Apply migrations before publish** (per `feedback_publish_wip.md`).
  This release has no EF migrations; only the Gap 2 backfill SQL and the
  permission seeding SQL run against prod, both applied during the
  preparation of this release.
- After deploy, hit `/api/server/version` and confirm `1.12.0`.
- Spot-check `/validation/boe-lookup` as an analyst-or-above user. Try
  searches by container, BL, and VIN.

### Commits in this release

- `<TBD>` — 1.12.0 release: Gap 2 backfill + BOE Lookup + dual-write fix

---

## [1.11.0] — 2026-04-08

Wave processing hardening release. Eight findings from a top-to-bottom audit
of the wave system are addressed in this release, plus the multi-tenancy
plumbing the new flywheel tables were missing, plus a long-overdue FK on
`wavependingcontainers.parentgroupid`.

### Added — Wave Processing Monitor (Wave #3)

- New PANEL 3.5 on `Pages/Validation/ImageAnalysisManagement.razor`:
  parent-group counts, oldest pending/ready containers, stuck-over-24h
  alert, recent parents table. Loads on init, refresh button included.
- Backed by new `[HttpGet("wave-monitor")]` on
  `ImageAnalysisManagementController` returning `WaveMonitorResponse`
  with settings snapshot, counts, stuck containers, and the most recent
  20 parent groups.

### Added — Multi-tenancy plumbing for flywheel tables

- The five new flywheel tables (`threatcategories`,
  `revenueanomalycategories`, `manifestsnapshots`, `matchqualityflags`,
  `auditimagedecisions`) were missing the `tenant_id` + RLS pattern that
  every other NSCIM table has. Closed via two new Phase 1 scripts:
  `tools/migrations/phase1-tenancy/13-nickscan-flywheel-add-tenant-id.sql`
  and `23-nickscan-flywheel-rls.sql`. Idempotent, default `tenant_id = 1`,
  composite `(tenant_id, id)` index on each.

### Added — Wave processing FK constraint

- `fk_wavependingcontainers_analysisparentgroups_parentgroupid` with
  `ON DELETE CASCADE`. Configured in `ApplicationDbContext.cs` and
  enforced via migration `AddWavePendingContainerFk`. Prevents orphaned
  pending-container rows when a parent group is removed.

### Fixed — Wave #4: 72-hour Pending-without-images timeout

- Per-container watchdog in `ImageAnalysisOrchestratorService.RunPartialWaveScanAsync`.
  A `WavePendingContainer` that has been `Pending` for more than 72 hours
  (no images yet ingested) is now flipped to `NoImageAvailable`, releasing
  the parent group from the indefinite "ghost container" wait. Previously
  the only safety net was the 30-day parent auto-close.

### Fixed — Wave #5: race condition in CreateNewWaveAsync

- Two concurrent wave scans on the same parent could both compute the
  same `maxWaveNumber` and create duplicate wave rows. Added
  `pg_advisory_xact_lock(parent.Id)` at the top of `CreateNewWaveAsync`
  so wave creation per parent is now serialised at the Postgres level.
  The catch on duplicate-key insertion was promoted from `LogDebug` to
  `LogWarning` so future races become visible.

### Fixed — Wave #6: parent-group rollup never happened

- Parents were stuck in `Active` forever — even after every wave under
  them had been decided. `DecisionSideEffectsService.ApplyAsync` now,
  on the transition from `AnalystAssigned`/`Ready` to `AnalystCompleted`,
  increments the parent's `CompletedWaveCount` and (if no
  Pending/Ready `WavePendingContainers` remain) advances the parent to
  `Complete`. Guarded by a `groupJustCompleted` flag so re-entry of
  `ApplyAsync` cannot double-count.

### Fixed — Wave #7: dead fields removed

- `AnalysisParentGroup.AutoCloseDate` and `WavePendingContainer.AssignedToWaveId`
  were both written by code but never read by anything. Dropped from the
  entities and from the database via migration `DropDeadWaveFields`.
  `CompletedWaveCount` is **kept** because Wave #6 now populates it and
  the Wave #3 monitor displays it.

### Fixed — Wave #8: orphaned-column audit (no-op, recorded for completeness)

- Confirmed via `information_schema` that no orphaned `completedatutc`
  columns exist on the wave tables. Nothing to drop.

### Fixed — Latent bug carry-over from 1.10.1

- `ICUMSArchiveController` was still using
  `[Authorize(Policy = Permissions.PagesIcumsView)]`, which the
  `DynamicAuthorizationPolicyProvider` cannot resolve (same shape as the
  500 that hit `/validation/match-corrections` on 1.10.0). Switched to
  `[Authorize(Policy = "CustomsOfficer")]`. No live traffic had hit it
  yet, but the next person who tried would have got a 500.

### Cleanup

- Removed dead `OnSaveClick` method on `ImageDecisionView.razor` that
  was wired to nothing.
- Removed three `[Fact(Skip = "Stale...")]` test stubs from
  `CriticalPathTests`.
- Three baseline wave migration files had `'AwaitingImages'` as the
  `Status` default; production was already `'Pending'`. Source files
  brought into agreement with the live schema so a fresh-environment
  rebuild matches prod exactly.

### Migrations introduced

- `20260407210009_AddWavePendingContainerFk` — wave parent FK with cascade delete
- `20260408052032_DropDeadWaveFields` — drops `assignedtowaveid` and `autoclosedate`
- `tools/migrations/phase1-tenancy/13-nickscan-flywheel-add-tenant-id.sql` — `tenant_id` columns on the five flywheel tables (raw SQL, applied via psql before publish)
- `tools/migrations/phase1-tenancy/23-nickscan-flywheel-rls.sql` — RLS policies for the same five tables (raw SQL, applied after #13)

### Operational notes for the deploy

- **Apply migrations BEFORE running `dotnet publish`** (per `feedback_publish_wip.md`).
  Order: phase-1 tenancy scripts (13, 23) → EF idempotent script for the
  Application context. Both EF migrations in this release are pure column
  drops / FK adds and apply cleanly.
- After deploy, hit `/api/server/version` and confirm `1.11.0`.
- Spot-check the new PANEL 3.5 on `/validation/image-analysis-management`
  to confirm the monitor renders.

### Deferred to 1.12.0

- Gap 2 backfill of historical `SuspiciousAreas` JSON into typed
  `ContainerAnnotation` rows (needs prod-DB-copy testing first).
- Gap 2 column deprecation (depends on the backfill).
- BOE lookup on `CargoGroupDetails` (Option B: extend the existing page
  with declaration / master-BL / house-BL / container / loose-cargo-BL /
  VIN search; analysts and up).

### Commits in this release

- `f234d659` — Wave processing fixes #4-#8: timeouts, race fix, parent rollup, dead field cleanup
- `f27aa23a` — Wave #3: wave processing monitor panel on ImageAnalysisManagement
- `63c6a09e` — 1.11.0 pre-release: multi-tenancy + wave FK + source cleanup

---

## [1.10.1] — 2026-04-07

Post-release hotfix. 1.10.0 was deployed and a user immediately hit a 500 on
the new `/validation/match-corrections` page. Diagnosed, fixed, and
re-published within minutes. Two separate issues had to be resolved:

1. **Authorization policy mismatch** (code fix)
2. **Pending migrations never applied to production** (operational fix)

### Fixed — Match Corrections page returned 500 on first hit (auth)

- Root cause: `AdminMatchCorrectionController` and `AiTrainingExportController`
  used `[Authorize(Policy = Permissions.PagesValidationMatchCorrections)]`
  and `[Authorize(Policy = Permissions.PagesAdminDatabase)]` respectively.
  The API-side `DynamicAuthorizationPolicyProvider` only recognises
  policies prefixed with `"Permission:"`; raw permission strings fall
  through to the base provider which throws `InvalidOperationException:
  The AuthorizationPolicy named: '<name>' was not found`.
- Fix: switched both controllers to `[Authorize(Policy = "AdminOnly")]`,
  matching every other admin controller in the codebase (AccessReview,
  Audit, DatabaseAdmin, Debug, Diagnostics, ErrorInvestigation).
- The `Permissions.PagesValidationMatchCorrections` constant stays in
  place so the Blazor page (which uses a *different* policy provider
  that does resolve raw permission strings) keeps working for nav
  visibility and page gating.
- A latent bug with the same shape exists in `ICUMSArchiveController`
  (`[Authorize(Policy = Permissions.PagesIcumsView)]`). It hasn't
  fired because no live traffic reaches that endpoint. Worth fixing
  on the next touch.

### Fixed — Pending migrations never applied to production (operational)

- Root cause: migrations in the NSCIM repo are applied **manually**
  (there is no `Database.Migrate()` call on startup). The 1.9.0 deploy
  on 2026-04-06 and the 1.10.0 deploy today both shipped new entities
  and migration files without the migrations ever being applied to the
  production database. 1.9.0 happened to work at the binary level
  because nothing actively tried to read the new tables; 1.10.0 broke
  immediately because the new Match Corrections UI queries
  `matchqualityflags` on first page load.
- The six pending migrations were:
  - `20260406200622_AddThreatAndRevenueAnomalyCategories`
  - `20260406210111_BaselineWaveProcessingForFreshEnvironments`
  - `20260406211515_AddManifestSnapshot`
  - `20260406220307_BaselineWaveProcessing`
  - `20260407183021_AddMatchQualityFlags`
  - `20260407185718_AddAuditImageDecisions`
  - `20260407191703_AddDecisionLinkageToContainerAnnotation`
- Two complications made automatic application impractical:
  1. A rogue `20260327130849_AddWaveProcessingSupport` history row
     exists in production with no corresponding source file. `dotnet
     ef database update` correctly ignored it (the CLI only emits
     pending source migrations), but it was a land mine.
  2. The EF `migrations script --idempotent` output had a latent
     syntax error in the seed `INSERT` blocks: the `migrationBuilder.Sql(@"…")`
     heredocs in `AddThreatAndRevenueAnomalyCategories` (and a
     pre-existing one in `AddDecisionAgent`) have no trailing
     semicolon inside the raw SQL, which is invalid inside EF's
     `DO $EF$` wrapper.
- Fix: wrote a hand-crafted SQL script
  (`C:/AI/apply_pending_manual.sql`, not committed to source) that
  re-implements the six pending migrations using idempotent DDL
  (`CREATE TABLE IF NOT EXISTS`, `ADD COLUMN IF NOT EXISTS`,
  `ON CONFLICT ... DO NOTHING`) and inserts the history rows
  manually. Schema backup taken first
  (`backups/migrations_2026-04-07/schema_before_110.sql`).
- Result: 6 tables created (or column additions applied), 13+13
  category rows seeded, `__EFMigrationsHistory` now shows all six
  new migrations. Match Corrections endpoint returns 200.

### Source fix for the seed-SQL semicolon bug

The seed `migrationBuilder.Sql(@"…")` blocks in the three migrations
I wrote on 2026-04-06 / 2026-04-07 had the missing-semicolon bug.
They've been corrected in source so future fresh-env deploys can use
`dotnet ef database update` normally without tripping over the bug.

### What this teaches future-me

When adding a new admin controller with `[Authorize(Policy = ...)]`:

- **API controllers** must use one of: `"AdminOnly"`, `"CustomsOfficer"`,
  `"ScannerOperator"`, `"ImageAnalyst"`, `"AuditReviewer"`, or a
  `"Permission:xxx"`-prefixed string.
- **Blazor page attributes** can use raw permission constants —
  they resolve through the WebApp-side provider, not the API-side one.

When adding a new EF migration:

- **Run `dotnet ef database update` against a dev DB** (or apply the
  generated SQL script manually) **before declaring a deploy done**.
  Migrations in NSCIM are not applied on startup.
- **Any `migrationBuilder.Sql(@"…")` block must end with a semicolon**
  inside the heredoc. The idempotent-script path wraps each Sql call
  in a `DO $EF$ IF ... THEN <your sql> END IF; END $EF$;` block,
  and a missing semicolon in your SQL makes the whole block invalid.

### Commits

- `<this release>` Hotfix: AdminOnly policy + source seed semicolons

---

## [1.10.0] — 2026-04-07

End-of-day sweep of the outstanding work list — ten code commits on top of
1.9.0 closing production gaps, completing deferred UI work, and adding real
regression coverage.

### Added — Match Correction Tool

A new admin tool for inspecting and correcting wrong image-to-BOE matches,
closing the production exposure that produced record 80126035944.

- New `MatchQualityFlag` entity + `matchqualityflags` table
  (migration `20260407183021_AddMatchQualityFlags`). Persistent,
  resolvable record of matching anomalies, replacing the historical
  log-only approach.
- Three new prevention hooks in `ContainerCompletenessService`:
  - **`NullDeliveryPlace` block** — matches against BOE rows with no
    `DeliveryPlace` are now **blocked** (previously allowed with a
    warning) and raised as Critical flags.
  - **`FycoMismatch` detection** — cross-checks scanner `FycoPresent`
    against `BOE.ClearanceType`. Export scan → Import BOE blocks as
    Critical; Unknown scan → Export BOE allowed with a Warning flag.
  - **`DuplicateImage` detection** — raises a Warning flag when two
    distinct containers share the same FS6000 image filename.
- New `AdminMatchCorrectionController` at `/api/admin/match-corrections`
  with list / detail / unmatch / rematch / flag / resolve endpoints.
- New admin UI at `/validation/match-corrections` with detail,
  unmatch, rematch, and manual-flag dialogs.
- New permission `pages.validation.matchcorrections`.

### Added — Per-image audit decisions

Closes the deferred plan request that image analysis records N decisions
per container but audit historically recorded only one Approve/Reject per
container.

- New `AuditImageDecision` child entity + `auditimagedecisions` table
  (migration `20260407185718_AddAuditImageDecisions`).
- `AuditReviewController.SubmitAudit` extended with optional
  `ImageDecisions[]` on `ContainerAuditDecisionDto`. When supplied:
  - Parent rollup: any Rejected child forces parent Rejected
  - Child rows written via EF; pre-existing children removed first
    so re-submissions don't double-count
  - Best-effort: child failures log a warning but never fail the
    parent submit
- `AuditReviewDialog.razor` rebuilt: one row per image inside each
  container's Audit Decision tab with per-image Approve/Reject +
  notes, container rollup chip ("X approved, Y rejected, Z pending"),
  fallback to container-level radio if image preload hasn't completed.

### Added — AI training flywheel Gap 4 (COCO export)

Closes Gap 4 of the approved flywheel plan. Joins analyst decisions +
typed annotations + manifest snapshots into a COCO Object Detection JSON
corpus suitable for training a YOLO / DETR / Faster R-CNN model offline.

- New `Services/AiTraining/CocoExportService.cs` producing output in
  the schema of the PhD reference module at
  `C:\AI\sample_training_export.json`.
- New `AiTrainingExportController` with:
  - `GET /api/admin/ai-training/coco/summary` (counts only, cheap)
  - `GET /api/admin/ai-training/coco/download` (full JSON file)
  - Query params: `from`, `to`, `includeUncategorized`, `maxRows`
  - Permission: reuses `pages.admin.database`
- Default behaviour skips Pending and uncategorised rows so the
  output is automatically training-clean.
- Revenue category ids offset by 1000 to share the `category_id`
  space with security categories.

### Added — AI training flywheel Gap 2 (annotation linkage, partial)

First step toward making `ContainerAnnotation` the canonical bounding-box
store. The JSON `SuspiciousAreas` column stays as source of truth for
existing readers.

- New nullable `ImageAnalysisDecisionId` FK on `ContainerAnnotation`
  + index (migration `20260407191703_AddDecisionLinkageToContainerAnnotation`).
- `ImageAnalysisDecisionController.SaveDecision` now dual-writes the
  `SuspiciousAreas` JSON into typed `ContainerAnnotation` rows after
  the manifest snapshot capture:
  - Only runs when the decision has a finding category set
  - Idempotent: prior rows linked to the decision are removed first
  - Best-effort: parser/persistence failures log a warning but never
    block the analyst's save
- COCO export already prepared for this path — now starts seeing
  typed rows for new saves instead of falling through to the JSON
  parser path.

### Added — `ImageAnalysisManagement` pagination & filtering

Resolves the deferred plan request for a more structured view of Queues
and Assignments.

- Six tables (Ready Queue Consolidated / Non-Consolidated + four Active
  Assignments tabs) now have:
  - `MudTablePager` with `PageSizeOptions { 10, 25, 50, 100 }`, default 25
  - Sortable headers on Group, Scanner/Analyst, Count, LeaseUntil
  - Per-tab string filter for Ready Queue
  - Single shared filter + "Expiring < 15 min" toggle for the four
    Assignments tabs (oncall triage)

### Added — Flywheel dropdown ripple to `ImageViewer.razor`

- The legacy fullscreen image viewer now carries the security +
  revenue finding-category dropdowns, matching
  `ImageAnalysisViewer.razor`.
- Investigated and intentionally skipped:
  - `ImageAnalysisViewDialog` — only POST is a BulkDecision action
    where forcing a single category onto many containers is wrong UX
  - `ImageDecisionView` — read-only display + dead `OnSaveClick`
    method with no UI binding
  - `AnnotationOverlay`, `NewContainerCompletenessModel` — read-only

### Added — Live schema regression tests

- Five new `NewEntitySchemaTests` in the Tests project using fresh
  in-memory `DbContext` per call (no `WebApplicationFactory`). Coverage:
  - `ImageAnalysisDecision` finding ids round-trip
  - `ContainerAnnotation` decision linkage round-trip
  - `MatchQualityFlag` `(container, type, !resolved)` lookup pattern
  - `ManifestSnapshot` field round-trip
  - Multiple `AuditImageDecision` children for one parent
- **5/5 passing** on first proper run.

### Changed — Documentation

- `BaselineWaveProcessingForFreshEnvironments` migration gained a
  "DUPLICATION NOTE" explaining that a parallel baseline
  (`20260406220307_BaselineWaveProcessing`) exists for the same
  schema, both co-exist safely because both use `IF NOT EXISTS`
  guards, and **neither file may be deleted** because both are in
  deployed `__EFMigrationsHistory` tables.

### New migrations

| Migration | Purpose |
|---|---|
| `20260407183021_AddMatchQualityFlags` | `matchqualityflags` table |
| `20260407185718_AddAuditImageDecisions` | `auditimagedecisions` child table |
| `20260407191703_AddDecisionLinkageToContainerAnnotation` | `imageanalysisdecisionid` FK on `containerannotations` |

### ⚠️ Behaviour change — brief operations before the deploy

The Match Correction Tool's `NullDeliveryPlace` block starts blocking
matches the moment 1.10.0 deploys. Approximately 269 BOE records that
were previously allowed through the location gate with a warning will
now hold as `Status = "Missing"` instead. Operators will see them as
Critical open flags on the new `/validation/match-corrections` page.
**This is expected** — it's the fix for record 80126035944 — but it
will look like a sudden spike in unmatched containers if nobody's
expecting it. Brief ops before the deploy.

### Commits

- `cb87e268` Match Correction Tool: prevention hardening + admin recovery UI
- `5f084a4e` ImageAnalysisManagement: pagination, sort, search, expiring-soon filter
- `18538d4d` ImageViewer (legacy fullscreen): wire AI training flywheel dropdowns
- `418ecc18` Per-image audit decisions: backend slice
- `f559036d` AI training flywheel — Gap 4: COCO export endpoint
- `735d1679` AuditReviewDialog: per-image verdicts UI
- `1acc01b3` Gap 2 (annotation linkage): dual-write SuspiciousAreas → typed rows
- `325264aa` Tests: live schema regression coverage
- `377d63e7` Document the duplicate wave-baseline migration situation

---

## [1.9.0] — 2026-04-06

AI training flywheel release — the groundwork for capturing analyst
decisions as training data for a future AI model, built directly into
NSCIM rather than as a side Python module.

### Added — Dual-domain finding vocabulary

- New `ThreatCategory` + `RevenueAnomalyCategory` lookup entities and
  tables (migration `20260406200622_AddThreatAndRevenueAnomalyCategories`).
- Seeded with 13 security categories (weapons, drugs, contraband,
  hazmat, etc.) and 13 revenue assurance categories (undeclared goods,
  undervaluation, misclassification, transit diversion, concealment,
  etc.) drawn from the WCO commercial fraud taxonomy and Ghana
  operational realities.
- Nullable `ThreatCategoryId` + `RevenueAnomalyCategoryId` FK columns
  on `imageanalysisdecisions` and `containerannotations`. Both
  nullable so existing rows and front-ends keep working unchanged.
- New `InspectionFindingCategoriesController` read endpoints for the
  analyst dropdowns.
- `ImageAnalysisDecisionController.SaveDecision` DTO + SQL extended
  to accept and persist the new FK ids with "preserve existing on
  null" semantics.
- Two `MudSelect<int?>` dropdowns wired into the Image Analysis
  Blazor page (`ImageAnalysisViewer.razor`) — security + revenue, both
  optional, both Clearable.

### Added — Manifest snapshot at decision time (Gap 0)

Addresses the reproducibility gap discovered on 2026-04-06: NSCIM linked
to manifest data via a foreign key into the mutable ICUMS database, so
historical analyst decisions could drift as manifests were amended,
re-downloaded, or purged.

- New `ManifestSnapshot` entity + `manifestsnapshots` table
  (migration `20260406211515_AddManifestSnapshot`). Frozen-in-time
  copy of the ICUMS manifest with `DeclaredGoodsDescription`, HS
  codes JSON, quantities / values JSON, parties, clearance type, and
  a `RawManifestJson` forensic dump.
- New `ManifestSnapshotService` that resolves
  `ContainerCompletenessStatus.BOEDocumentId`, reads from
  `IcumDownloadsDbContext`, and persists via `ApplicationDbContext`
  inside the SaveDecision transaction.
- `ImageAnalysisDecisionController.SaveDecision` hooks the snapshot
  capture after the decision insert/update.
- Best-effort by design: ICUMS failures log a warning and record
  `Source = "no_data"` rows instead of blocking the analyst's save.

### Added — Wave-processing baseline migration

Closes the EF migration history gap left by the original
`Migrations/wave_processing_schema.sql` script.

- New migration `20260406210111_BaselineWaveProcessingForFreshEnvironments`.
  Idempotent `CREATE TABLE IF NOT EXISTS` / `ADD COLUMN IF NOT EXISTS`
  for the wave-processing schema. No-op on production (tables already
  exist), real schema creation on fresh dev / test databases.

### Fixed — Tests project compiles again

- `CriticalPathTests.cs` + `TestConfiguration.cs` restored from 42
  build errors to 0. Added `using Xunit;`, `public partial class
  Program {}` declaration for `WebApplicationFactory<Program>`,
  removed references to deleted API surface. Four tests that
  reference dead code quarantined as `[Fact(Skip = ...)]` stubs for
  future rewrite.

### New migrations

| Migration | Purpose |
|---|---|
| `20260406200622_AddThreatAndRevenueAnomalyCategories` | Finding category lookup tables + FK columns + 13+13 seed rows |
| `20260406210111_BaselineWaveProcessingForFreshEnvironments` | Idempotent wave schema baseline |
| `20260406211515_AddManifestSnapshot` | `manifestsnapshots` table |

### Commits

- `019d3b58` AI training flywheel — Gap 1a backend: dual-domain finding categories
- `298de12d` Baseline wave-processing schema for fresh environments
- `05c65ec1` Restore Tests project to a compiling state
- `d33ff2d4` AI training flywheel — Gap 1a UI: dropdowns in ImageAnalysisViewer
- `c5295c00` AI training flywheel — Gap 0 phase A: ManifestSnapshot entity + table
- `f4baf30d` AI training flywheel — Gap 0 phase B: ManifestSnapshotService + SaveDecision hook
- `b24dcce3` Bump version 1.8.0 → 1.9.0 for AI training flywheel release

### Post-deploy note (captured at 1.10.0 time)

The duplicate wave-baseline migration situation discovered in 1.9.0 was
not fixed by deleting one of the migrations — both were already in the
deployed `__EFMigrationsHistory` table, so deletion would cause EF to
log "model has pending changes" warnings and force manual history-table
cleanup. The cosmetic redundancy was intentionally kept and documented
in 1.10.0 (`377d63e7`).

---

## [1.8.0 and earlier]

This changelog started at 1.10.0. Earlier releases — including the 1.8.0
image splitter, BOE completeness, ICUMS ingestion, wave processing, and
audit workflow work — are traceable via `git log` on `main`. Notable
pre-changelog commits worth remembering:

- `c8b9beb3` Drop vestigial `IcumContainerData` + `IcumManifestItems` DbSets
- `1f083e34` Reconcile EF snapshot drift from hand-applied wave processing schema
- `55b46eee` Split ASE comma-joined container numbers at ingestion; filter Unknown
- `1071e5ed` Image Split Review: inline expandable rows
- `745a5a9e` Fix Image Split Review original scan image rendering

Going forward, every version bump should add an entry at the top of this
file describing what landed. The entry format is:

```
## [X.Y.Z] — YYYY-MM-DD

Short summary of the release theme.

### Added / Changed / Fixed / Removed — <area>

- ...

### New migrations

- ...

### ⚠️ Behaviour change — brief operations before the deploy (when applicable)

- ...

### Commits

- `shortSHA` commit title
```

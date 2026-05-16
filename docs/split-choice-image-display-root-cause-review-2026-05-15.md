# Split Choice Image Display Root Cause Review

Date: 2026-05-15
Status: Reviewed, first corrective patch deployed
Scope: Image Analysis split-choice cards for stored option A/B crops.

## Executive Finding

The stored split image data is present and the API can serve it. The repeated display failure is primarily in the WebApp render layer, with two workflow design issues making the symptom look like a backend/image-storage failure.

## Evidence

- Analysis record `3418` has split job `effda69a-d3a3-476d-8b14-8095d3f4e35f` and stored option result IDs:
  - option A: `87936a62-3798-40a0-b622-af67cc6bd62e`
  - option B: `919c2835-c5fc-4b83-9d62-0a14e4ea9902`
- The raw splitter result-list endpoint still returns `500` for that job, but the API fallback correctly recovers choices from stored result IDs.
- Live WebApp proxy verification returns nonzero image bytes:
  - original: `200 image/jpeg`, `145754` bytes
  - option A lossless: `200 image/png`, `534551` bytes
  - option A preview: `200 image/jpeg`, `71684` bytes
  - option B lossless: `200 image/png`, `558536` bytes
  - option B preview: `200 image/jpeg`, `75134` bytes
- API logs showed the split option flow could request the original split image while option crop images were not consistently requested. That aligns with deferred browser loading rather than missing server assets.
- The split option crop `<img>` tags were using `loading="lazy"` inside a tabbed, scrollable MudBlazor dialog. The original image was eager. In this layout, browser lazy loading can defer crop image requests indefinitely or until scroll heuristics decide the image is visible.
- The Images tab renders `SplitChoiceDialog` and `ImageDecisionView` at the same time. The normal viewer issues repeated `/api/scan-assets/{id}/image` requests for the combined scan, which made the logs look like the split-choice cards were failing even when the normal viewer was the surface generating those requests.

## Corrective Patch Deployed

- `src/NickScanWebApp.New/Components/Operations/SplitChoiceDialog.razor`
  - Split option crop images now render through the same-origin WebApp image proxy.
  - Crop images now use eager loading instead of lazy loading.
  - Crop cards now reserve an explicit image frame so layout cannot collapse or hide the image area.
- Controlled WebApp deploy completed from:
  - `deploy-staging\webapp-split-choice-eager-20260515-135520`
- Production WebApp backup:
  - `deploy-backups\webapp-pre-split-choice-eager-20260515-135520`
- Deployed WebApp DLL hash:
  - `FB2CE0C6EFC9563D7AA70A39EE1401D7EE13B6084FF7803E595E15D4CA574955`

## Remaining Design Gaps

- `ImageDecisionView` should be gated while a split choice is still required. Rendering both surfaces at once causes confusing image traffic and can show the unsplit combined image while the analyst is supposed to choose a crop.
- Consolidated image-analysis mode passes `GroupIdentifier` as `ContainerNumber` to `SplitChoiceDialog`. This works only when a good `SourceScanResolution` is already available. A safer contract is to pass `analysisRecordId`, `splitJobId`, and the physical child container number explicitly.
- Browser-side telemetry is weak. The server can prove image bytes exist, but the app does not currently report whether each split option image element loaded or errored in the analyst browser.
- The raw splitter `/results` endpoint remains unhealthy for this job. The stored-ID fallback is correct for the analyst path, but the raw endpoint should still be fixed or demoted in monitoring so it does not keep reappearing as a suspected root cause.

## Recommended Next Fixes

1. Add a split-choice state callback so `ImageAnalysisViewDialog` hides `ImageDecisionView` until the analyst chooses or skips the split.
2. Change split-choice identity to prefer `analysisRecordId` and `splitJobId` over `ContainerNumber`, especially in consolidated/group views.
3. Add lightweight browser load/error telemetry for each split option image source.
4. Add a focused UI regression that asserts stored option A/B render as eager proxied image URLs, not lazy direct API URLs.
5. Separately repair or suppress the raw splitter result-list `500` so it stops muddying operational diagnosis.

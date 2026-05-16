# Image Analysis Summary Resolution Plan

Date: 2026-05-13

## Problem

The Image Analysis summary surfaces were still relying on older assumptions:

- Consolidated work means the identifier is a container number that must be converted to Master BL.
- Non-consolidated work means the identifier is a BOE/declaration number.
- The fullscreen image viewer document icon can call `/api/cargogroup/{id}` directly.

Those assumptions are no longer enough after wave processing and CMR composite-key progression. A valid analysis assignment can now arrive as a wave-suffixed analysis group identifier or as a synthetic `CMR-*` operational key backed by rotation number, container number, and BL number.

## Scenarios To Cover

- Normal consolidated cargo: container number resolves to Master BL and loads the cargo summary.
- Normal non-consolidated cargo: declaration number loads the cargo summary.
- Wave assignments: `_W` analysis group identifiers normalize back to their parent BL/declaration key.
- CMR composite assignments: `CMR-*` operational keys resolve through `RecordCompletenessStatus`, `RecordExpectedContainer`, and the matching CMR BOE rows.
- Container-only fallback: when a fullscreen image is opened outside the assignment dialog, the document icon can still try the physical container number.
- Misclassified groups: callers may pass the wrong consolidated/non-consolidated hint; the backend still retries the opposite type.

## Implementation

- Backend cargo group service recognizes CMR operational keys before normal type detection.
- CMR lookup prefers the record-completeness link, then verifies matching BOE rows by recomputing the composite key from rotation number, container number, and BL number.
- CMR cargo summary data is built directly from matched BOE rows, expected containers, manifest items, scanner data, and image data.
- Image Analysis summary tab now tries the API's scenario-aware cargo resolver first, ICUMS-only, before falling back to legacy container/declaration handling.
- Fullscreen image viewer document icon now tries the assignment `GroupIdentifier` first and the physical container second, ICUMS-only, using optional GETs so one failed route does not break the panel.

## Verification Checklist

- CMR key `CMR-C40FEA9B3C7FA383450D` returns a cargo group with ICUMS groups.
- Image Analysis summary tab renders for the CMR assignment.
- Fullscreen document icon renders goods/manifest data for the same CMR assignment.
- Wave assignment summary tab still renders through normalized group lookup.
- Existing consolidated and non-consolidated assignments still load summaries.
- Build and guardrail tests pass before deployment.

## Rollback

- If the summary path misbehaves, revert only the cargo-summary resolver changes in `CargoGroupService`, `ImageAnalysisViewDialog`, and `ImageAnalysisViewer`.
- CMR progression remains controlled separately by `CmrCompositeProgression:Enabled`; this summary fix does not enable progression by itself.

# Eagle A25 Air Cargo Scanner Implementation Plan

Date: 2026-05-14  
Repository: `C:\Shared\NSCIM_PRODUCTION`  
Scope: implementation plan plus initial code rollout notes. The first module pass now includes local schema, sync service, API endpoints, scanner workflow gates, and UI visibility. Production migration, secret injection, and deployment are still separate operational steps.

## Executive Summary

Enroll the Eagle A25 as a new air-cargo scanner source without letting early discovery data leak into normal assignment queues. The first implementation should copy source data from the A25 environment into NSCIM-controlled storage, preserve source identity, expose the assets through scanner-neutral APIs, and add operator UI visibility only after validation proves that the copied metadata and images line up with the canonical cargo identity.

SQL access is now resolved through a read-only login. Do not store or repeat the password in this repo, documentation, appsettings files, scripts, screenshots, or tickets. Keep the credential in the approved secret store only.

## Known Source Identity

| Field | Value |
| --- | --- |
| Scanner family | Eagle A25 |
| Scanner role | Air cargo X-ray scanner |
| NSCIM scanner type | `EAGLE_A25` |
| Source DB host | `10.0.5.163` |
| Source DB name | `xray` |
| SQL access | Read-only login, password intentionally omitted |
| Source file share | Eagle A25 network file share; mount/read with least-privilege service identity and record the final UNC path in deployment notes, not in source code if it includes sensitive share credentials |
| Assignment intake default | Ignore by default until explicitly enabled |

The NSCIM identity should not reuse `ASE`, `FS6000`, or Heimann labels. Treat Eagle A25 as its own scanner type from the first copied row so downstream metrics, completeness, UI filters, and audit history can distinguish it.

## Enrollment Rules

- Copy/sync only May 2026 forward data at first. The initial production rule is `scan_timestamp >= 2026-05-01 00:00:00` using the source system's authoritative scan timestamp.
- Do not backfill older Eagle A25 scans in the first pass unless a separate validation window is approved.
- Treat source DB access as read-only. NSCIM must never update the `xray` database on `10.0.5.163`.
- Treat the file share as read-only from NSCIM. Copy into NSCIM-owned storage before serving images or derived assets.
- Preserve source row IDs, source timestamps, source file paths, file hashes, and copy timestamps for traceability.
- Assignment-intake ignore default: Eagle A25 rows must be copied as discovery/quarantine records and must not enter analyst assignment, audit assignment, readiness caches, decision agents, or submission workflows until an explicit `AssignmentIntakeEnabled` or equivalent release switch is turned on.

## Canonical Source Joins

The canonical source read model should be built from the A25 `xray` database plus the file share, then normalized before NSCIM workflow use.

Required join shape:

| Canonical concept | Source requirement |
| --- | --- |
| Physical scan identity | One immutable source scan row ID from `xray` plus scanner type `EAGLE_A25` |
| Cargo/container identity | Join source scan metadata to container, ULD, airway bill, flight, manifest, or inspection reference fields available in `xray`; do not invent a fake container when only air-cargo identifiers exist |
| Image asset identity | Join the scan row to the source file/share asset records or file-path fields that locate original and derived image files |
| Inspection/time identity | Use scan timestamp, inspection timestamp if separate, lane/site/device ID, operator/session fields where available |
| Business correlation | Prefer exact manifest/airway-bill/container references from the source; fall back to a quarantined unresolved-cargo state instead of pushing ambiguous rows into assignment |

Implementation should produce a stable NSCIM source key:

```text
ScannerType = EAGLE_A25
SourceDatabase = xray
SourceHost = 10.0.5.163
SourceScanId = <immutable A25 scan row id>
SourceAssetId = <source file/share asset id or normalized path hash>
```

Do not use display labels, file names, or comma-joined cargo text as workflow identity. If one A25 scan maps to multiple cargo references, store the physical scan once and add child mapping rows for each logical cargo target.

## Asset Types

Plan for these asset classes even if the first copy only confirms a subset:

| Asset type | Purpose |
| --- | --- |
| Original vendor image | Immutable source-of-truth image copied from the A25 file share |
| High-energy plane | Viewer and material discrimination if available |
| Low-energy plane | Viewer and material discrimination if available |
| Material/classification plane | Material overlays if the A25 source provides one |
| Vendor composite/JPEG | Fast UI preview and A/B comparison against NSCIM rendering |
| Thumbnail | Queue/list preview generated in NSCIM storage |
| Metadata JSON | Normalized scan, cargo, source path, hash, and copy metadata |
| Split/crop candidates | Future child assets when one scan contains multiple cargo targets |
| Analysis derivatives | NSCIM-generated render modes, annotations, decisions, and audit evidence |

Store each copied file with source hash, byte length, copied-at timestamp, source modified timestamp, source share path, scanner type, and source scan ID.

## NSCIM Storage Work

1. Create an Eagle A25 staging/copy area under NSCIM-controlled storage, separate from the source share.
2. Add a scanner-neutral asset record for each copied source object, keyed by `EAGLE_A25` plus source scan ID and source asset ID/path hash.
3. Keep discovery rows quarantined from assignment intake by default.
4. Record copy status: pending, copied, hash-matched, missing-source-file, unsupported-format, ambiguous-cargo, and ready-for-validation.
5. Add retention and replay rules before broad sync: the copy process must be idempotent and able to re-check hash/size without duplicating assets.

## NSCIM API Work

Planned API behavior:

- Add resolver support for `EAGLE_A25` in the scanner-neutral source asset flow.
- Return source metadata separately from normalized cargo identity so unresolved air-cargo scans can still be inspected safely.
- Provide read endpoints for copied original image, preview, metadata, and capability summary.
- Gate assignment-intake APIs so `EAGLE_A25` is ignored unless the release switch is enabled and the row has passed validation.
- Surface source copy errors and ambiguity reasons through monitoring endpoints rather than silently dropping rows.

Compatibility rule: existing `ASE` and `FS6000` routes must keep working. Eagle A25 should enter through canonical scanner-neutral paths first, with compatibility aliases added only if a real UI/API caller needs them.

## NSCIM UI Work

Planned UI behavior:

- Add `EAGLE_A25` scanner filter labels in scanner dashboards, image tooling, and monitoring once copied data exists.
- Add a read-only validation view for A25 discovery rows before enabling assignment.
- Show source identity, copy state, file availability, asset hashes, and cargo-resolution status.
- Keep analyst assignment queues clean by default: A25 scans should not appear in normal work queues until assignment intake is explicitly enabled.
- Add operator warnings for unresolved cargo identity, missing source files, unsupported image format, and ambiguous multi-cargo scans.

## Validation Gates

Before assignment intake can be enabled:

1. Confirm read-only SQL connectivity to `xray` on `10.0.5.163` without exposing the password.
2. Confirm read-only file-share access and successful copy into NSCIM storage.
3. Prove May 2026 forward sync picks the correct source rows and excludes older rows.
4. Validate canonical source joins against a sample of known A25 scans.
5. Confirm every copied asset has byte length, hash, source path, source scan ID, and copied-at timestamp.
6. Confirm API reads serve copied NSCIM assets, not live source-share files.
7. Confirm the UI can inspect A25 discovery rows without creating assignments.
8. Confirm assignment intake still ignores `EAGLE_A25` by default after deployment.

## Open Items

- Final UNC path for the Eagle A25 file share.
- Exact `xray` table and column names for scan rows, cargo identifiers, timestamps, and file paths.
- Whether A25 provides separate high/low/material planes or only vendor composite images.
- Whether cargo identity is container-based, ULD-based, airway-bill-based, flight/manifest-based, or mixed.
- Approved secret-store location/name for the read-only SQL login and file-share identity.

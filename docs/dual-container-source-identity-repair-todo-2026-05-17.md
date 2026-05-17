# Dual-Container Source Identity Repair TODO

Date: 2026-05-17
Status: Data-heal applied - operational comma identity pollution cleared in production

## Problem Statement

Dual-container ASE scans are still leaking comma-joined identifiers into workflow tables after the original ingestion split fix. The expected invariant is:

- Source scanner/audit tables may preserve the raw scanner label, for example `C1, C2`.
- Queue, completeness, record completeness, analysis, audit, cache, and ICUMS submission rows must use one physical container number per row.
- When a child container needs an image, it must resolve back to the original source scan and optional split choice rather than using the comma-joined source label as its operational identifier.

## Live Evidence Before This Repair

- `containerscanqueues.containernumber`: 743 comma/semicolon rows.
- `containercompletenessstatuses.containernumber`: 212 comma/semicolon rows.
- `analysisgroups.groupidentifier`: 2 comma/semicolon rows; both cancelled legacy split-pair groups.
- `analysisrecords.containernumber`: 3 comma/semicolon rows.
- `imageanalysisdecisions.containernumber`: 2 comma/semicolon rows.
- `analysisqueueentries.containersjson`: 0 comma/semicolon rows at repair time.
- Recent queue rows created on 2026-05-17 still included combined ASE containers, proving the issue was active and not only historical.

## Work Tracker

- [x] Phase 1: Stop new pollution from ASE queue recovery.
  - [x] Lift ASE split-to-queue logic into one reusable helper.
  - [x] Use the helper from ASE ingestion.
  - [x] Use the helper from queue recovery.
  - [x] Preserve raw source container string only in queue metadata.
  - [x] Ensure recovered multi-container queue items get suffixed inspection IDs.
- [x] Phase 2: Add guardrails.
  - [x] Unit/architecture guard that queue recovery cannot publish raw comma-joined ASE identifiers.
  - [x] Guard that ingestion and recovery share the same ASE queue split helper.
  - [x] Publisher-level single-container guard after recovery is patched.
- [ ] Phase 3: Fix completeness image evidence.
  - [x] Make ASE image existence token/source aware.
  - [x] Keep child completeness rows as single-container rows for new/recovered ASE queue work.
  - [x] Remove historical combined completeness rows during audited healing after child rows exist.
- [ ] Phase 4: Fix analysis and ICUMS image lookup.
  - [x] Resolve suffixed ASE inspection IDs back to the base inspection/source scan.
  - [x] Prefer source scan identity when building ICUMS payload image data.
  - [x] Preserve selected split lineage through repaired manifest-backed decisions.
- [x] Phase 5: Data-heal existing polluted rows.
  - [x] Split or supersede existing combined queue rows.
  - [x] Split or supersede existing combined completeness rows.
  - [x] Repair combined analysis records and decisions.
  - [x] Verify child records have assignment/image/submission progression.
- [x] Phase 6: Cache and UI/API hardening.
  - [x] Prevent predictive preload from caching comma-joined single-container keys.
  - [x] Return clear errors from single-container endpoints when passed multi-container identifiers.
  - [x] Keep source/aggregate usage on existing source-scan and split-context routes; single-container predictive routes now reject composite labels.
- [ ] Phase 7: Verification and closeout.
  - [x] Run focused tests.
  - [x] Build affected projects.
  - [x] Re-run read-only production pollution audit.
  - [x] Commit coherent completed slice.
  - [x] Ask before merge/deploy.

## Current Slice

Implemented and locally validated the first repair slice:

- ASE ingestion and queue recovery now share `AseScanQueueItemFactory`.
- Recovered ASE source rows with two containers are split into one queue row per physical container with suffixed inspection IDs.
- Completeness ASE image evidence is token/source aware.
- Image analysis and ICUMS payload lookup can resolve suffixed ASE inspection IDs and tokenized ASE source rows.
- Focused factory tests, architecture guardrails, services build, and API build pass locally.

Deployment and live verification:

- API-only controlled deploy completed from staged publish `deploy-staging/api-dual-container-identity-20260517-174603`.
- Live API config hashes were preserved across deploy.
- `NSCIM_API` restarted from `C:\Shared\NSCIM_PRODUCTION\publish\API`.
- `https://10.0.1.254:5206/health` returned Healthy after restart.
- Post-deploy pollution audit, filtered from the API restart window, returned zero comma/semicolon operational rows across queue, completeness, analysis groups, analysis queue entries, and image decisions.
- Recent post-deploy ASE queue rows are single-container rows.
- No dual-container ASE source rows existed in the last 24 hours, so the deployed recovery path had no fresh dual-source candidate to split during this watch window.

Audited production data-heal:

- Script added: `scripts/postgres/Repair-DualContainerOperationalIdentity.ps1`.
- Dry-run before healing confirmed:
  - `containerscanqueues`: 743 polluted rows, 3,190 child tokens, 3,114 child matches, 38 rows with no matching child queue rows.
  - `containercompletenessstatuses`: 212 polluted rows, 424 child tokens, 406 child matches, 6 rows with no matching child completeness rows.
  - `analysisgroups`: 2 cancelled legacy comma groups.
  - `analysisrecords`: 3 polluted rows.
  - `imageanalysisdecisions`: 2 polluted rows.
- Live repair run: `dual-container-identity-20260517-liveheal`.
- API was stopped during the transactional data-heal and restarted afterwards.
- Repair actions committed:
  - Inserted 76 missing child queue rows.
  - Deleted 743 polluted queue rows.
  - Inserted 18 missing child completeness rows.
  - Repointed 36 analysis groups away from polluted completeness rows.
  - Deleted 212 polluted completeness rows.
  - Reassigned manifest snapshot `1071` to existing child decision `3274` for `MSBU1383695`.
  - Repaired decision `3276` to child container `MSMU3308349` with split job `dc7cd1aa-5541-478f-9978-142584428446` and split result `80f6d78d-e4b1-4737-bdf2-bf094a436d1b`.
  - Marked analysis record `5039` (`MSMU3308349`) `Decided`.
  - Deleted the three comma-valued analysis records.
  - Renamed the two cancelled comma-valued groups to `SPLIT-SUP-{groupId}`.
- Audit backups were written to `maintenance.dual_container_identity_repair_audit` for:
  - 743 `containerscanqueues` rows.
  - 212 `containercompletenessstatuses` rows.
  - 38 `analysisgroups` rows/updates.
  - 3 `analysisrecords` rows.
  - 2 `imageanalysisdecisions` rows.
  - 2 `manifestsnapshots` rows.
- Post-repair verification:
  - All tracked operational pollution counts are zero across queue, completeness, analysis groups, analysis records, decisions, analysis queue entries, record expected containers, and audit decisions.
  - Repeat dry-run is clean and reports zero polluted rows.
  - `NSCIM_API` and `NSCIM_WebApp` are running.
  - `https://10.0.1.254:5206/health` returned `200 Healthy`.
  - Windows service event log shows `NSCIM_API` stopped and restarted successfully.
  - `Data/Logs/nickhr-20260517.log` had no new matching `Error`, `Exception`, `Unhandled`, `42P01`, or cancellation lines in the checked tail after restart.

Final guard/cache hardening:

- `ContainerNumberListMatcher` now exposes a shared composite-container detector.
- `ContainerScanQueuePublisherService` refuses direct single-item or batch queue publishes where `ContainerNumber` is a comma/semicolon composite source label.
- `PredictivePreloadService` excludes composite `AnalysisRecord.ContainerNumber` values from assignment container lists.
- Predictive container preload/invalidate/get flows reject composite identifiers and do not create comma-valued cache keys.
- `PredictivePreloadController` returns clear `400 BadRequest` messages for composite container labels on single-container cache routes.
- Focused validation:
  - `dotnet test tests/NickScanCentralImagingPortal.Integration.Tests/NickScanCentralImagingPortal.Integration.Tests.csproj --filter "FullyQualifiedName~PredictivePreloadServiceTests"` passed: 13/13.
  - `dotnet test tests/NickScanCentralImagingPortal.Core.Tests/NickScanCentralImagingPortal.Core.Tests.csproj --filter "FullyQualifiedName~StateOwnershipGuardrailTests"` passed: 15/15.

Merge note: `main` is checked out in `C:\Shared\NSCIM_PRODUCTION_CMR_PAIR_FIX` and was clean during the final hardening pass. Per workflow rule, merge still requires explicit user approval after the branch commits are complete.

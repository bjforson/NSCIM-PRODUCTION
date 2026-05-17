# Dual-Container Source Identity Repair TODO

Date: 2026-05-17
Status: Active - first repair slice deployed to API and post-deploy audit passed

## Problem Statement

Dual-container ASE scans are still leaking comma-joined identifiers into workflow tables after the original ingestion split fix. The expected invariant is:

- Source scanner/audit tables may preserve the raw scanner label, for example `C1, C2`.
- Queue, completeness, record completeness, analysis, audit, cache, and ICUMS submission rows must use one physical container number per row.
- When a child container needs an image, it must resolve back to the original source scan and optional split choice rather than using the comma-joined source label as its operational identifier.

## Live Evidence Before This Repair

- `containerscanqueues.containernumber`: 743 comma/semicolon rows.
- `containercompletenessstatuses.containernumber`: 212 comma/semicolon rows.
- `analysisrecords.containernumber`: 1 comma/semicolon row.
- Recent queue rows created on 2026-05-17 still included combined ASE containers, proving the issue was active and not only historical.

## Work Tracker

- [x] Phase 1: Stop new pollution from ASE queue recovery.
  - [x] Lift ASE split-to-queue logic into one reusable helper.
  - [x] Use the helper from ASE ingestion.
  - [x] Use the helper from queue recovery.
  - [x] Preserve raw source container string only in queue metadata.
  - [x] Ensure recovered multi-container queue items get suffixed inspection IDs.
- [ ] Phase 2: Add guardrails.
  - [x] Unit/architecture guard that queue recovery cannot publish raw comma-joined ASE identifiers.
  - [x] Guard that ingestion and recovery share the same ASE queue split helper.
  - [ ] Optional publisher-level single-container guard after recovery is patched.
- [ ] Phase 3: Fix completeness image evidence.
  - [x] Make ASE image existence token/source aware.
  - [x] Keep child completeness rows as single-container rows for new/recovered ASE queue work.
  - [ ] Mark or ignore combined completeness rows as superseded during healing.
- [ ] Phase 4: Fix analysis and ICUMS image lookup.
  - [x] Resolve suffixed ASE inspection IDs back to the base inspection/source scan.
  - [x] Prefer source scan identity when building ICUMS payload image data.
  - [ ] Preserve selected split lineage through analyst decision, audit, and submission.
- [ ] Phase 5: Data-heal existing polluted rows.
  - [ ] Split or supersede existing combined queue rows.
  - [ ] Split or supersede existing combined completeness rows.
  - [ ] Repair the remaining combined analysis record.
  - [ ] Verify child records have assignment/image/submission progression.
- [ ] Phase 6: Cache and UI/API hardening.
  - [ ] Prevent predictive preload from caching comma-joined single-container keys.
  - [ ] Return clear errors from single-container endpoints when passed multi-container identifiers.
  - [ ] Add explicit source/aggregate route usage where the original combined image is required.
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

Merge note: `main` is checked out in `C:\Shared\NSCIM_PRODUCTION_CMR_PAIR_FIX` and currently has an unrelated dirty modification in `src/NickScanWebApp.Shared/Services/ContainerDetailsService.cs`; merge is intentionally deferred until that worktree is clean or the owner confirms how to handle it.

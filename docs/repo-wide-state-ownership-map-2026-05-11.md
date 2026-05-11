# Repo-Wide State Ownership Map

Date: 2026-05-11

Purpose: make the architectural review reproducible across the whole repository. This map identifies each durable status/completeness surface, the intended owner, the expected invariant, the current write paths, and the drift found during review.

## Executive Summary

The codebase now has some strong state ownership patterns, especially `AnalysisGroup.Status`, where the setter is `internal` and the intended write path is `AnalysisGroupStateMachine.TransitionAsync`. The same discipline has not yet been applied consistently to completeness rows, record rows, submission queues, download queues, and operational processing statuses.

The highest-risk issue is not that one service has a bug. It is that several entities have status fields that are treated as canonical by some code and derived/cache fields by other code. That lets valid-looking local changes violate repo-wide invariants.

Highest-priority drift:

| Area | Risk | Finding |
| --- | --- | --- |
| Container completeness | High | Queue rows can be marked completed before the completeness update is durably saved. |
| Container completeness | High | `Status = "Complete"` has multiple writers and no single complete predicate. Some paths require scanner + ICUMS + image/group readiness; others complete on BOE/ICUMS presence only. |
| Container completeness | High | CCS identity is inconsistent: service dedupes by container + scanner + inspection, while a raw migration creates uniqueness on container + scanner only. |
| Container completeness | High | Step 2 can advance to `WorkflowStage = "ImageAnalysis"` without the Step 1 CMR/no-group guard. |
| Record completeness | Medium | Parent `RecordCompletenessStatus.ScannerType` is not maintained while child rows do carry scanner type. |
| Queue retry accounting | Medium | Container scan and ICUMS download queues increment retry count both when processing starts and when retry info is updated. |
| ICUMS submission queue | Medium | Service uses `Pending`, `Processing`, `Submitted`, `Failed`; controller and UI also use `Queued`, `Submitting`, and `Successful`. Manual retry can place a row in a status the worker does not consume. |
| Analysis group workflow | Medium | `AnalysisGroup.Status` is protected by the state machine, but `WorkflowStageStatusHelper` still documents `ContainerCompletenessStatus.WorkflowStage` as canonical, contradicting the architecture tests and entity comments. |

## Core Rule

Every status surface should have exactly one of these shapes:

1. Canonical state: only one service/facade may write it, and every transition is validated.
2. Derived/cache state: it is recomputed from canonical fields, and direct writes are forbidden except inside the recompute owner.
3. Operational queue state: repository/service owns the finite state machine and retry accounting.

Fields that do not fit one of those shapes are where the bugs are clustering.

## Ownership Map

### `AnalysisGroup.Status`

Entity: `src/NickScanCentralImagingPortal.Core/Entities/Analysis/AnalysisGroup.cs`

Intended owner:

- `src/NickScanCentralImagingPortal.Infrastructure/Data/AnalysisGroupStateMachine.cs`
- Validator: `AnalysisStatusValidator`
- Audit: `AnalysisGroupStatusTransition`
- Vocabulary: `AnalysisStatuses`

Invariant:

- Existing groups should transition through `AnalysisGroupStateMachine.TransitionAsync`.
- Transition validity and audit insertion should happen with the status mutation.
- Creation-time defaults and object initializers may set the initial `Ready` state, but post-creation workflow movement should use the facade.

Current safeguards:

- `AnalysisGroup.Status` has an `internal set`, so controllers and services outside Core/Infrastructure cannot directly mutate it.
- Architecture tests describe `AnalysisGroup.Status` as authoritative, with CCS workflow fields informational/aspirational for routing.

Drift:

- `WorkflowStageStatusHelper` says `ContainerCompletenessStatus.WorkflowStage` is canonical and `AnalysisGroup.Status` is cached/derived. That contradicts the entity comment and the architecture tests.
- The helper is still used by image-analysis orchestration paths to compute group status from CCS workflow distribution.

Required guardrails:

- Add/keep an architecture test that rejects post-creation `AnalysisGroup.Status = ...` writes outside `AnalysisGroupStateMachine`.
- Rename or rewrite `WorkflowStageStatusHelper` documentation so it cannot be read as a competing ownership model.
- Decide whether the helper is only a reconciliation hint or an allowed transition input. If allowed, it should transition through the state machine and explain why CCS workflow is sufficient evidence.

### `ContainerCompletenessStatus.Status` and `WorkflowStage`

Entity: `ContainerCompletenessStatus`

Intended owner:

- Primary: `ContainerCompletenessService`
- Supporting writers/reconcilers: `ContainerCompletenessOrchestratorService`, `ContainerStatusReconciliationService`, `ManualBOESelectivityService`, `PostICUMSValidationService`, `IcumJsonIngestionService`, admin/repair controllers.

Expected invariant:

- `Status = "Complete"` should mean the row has enough evidence to enter downstream analysis/submission logic.
- The complete predicate should be explicit and shared. Based on current service behavior, the candidate predicate is: scanner data present, ICUMS/BOE data present, image data present, and group/declaration gating satisfied.
- `WorkflowStage = "ImageAnalysis"` should only be set when the complete predicate and the Step 1 CMR/no-group guard are both satisfied.

Current write paths and drift:

- `ContainerCompletenessService` has the richest predicate, but there are multiple local implementations and side writers.
- `ContainerStatusReconciliationService` marks containers complete when BOE data exists, without checking scanner/image/group readiness.
- `PostICUMSValidationService` can set `Status = "Complete"` as part of validation.
- `ManualBOESelectivityService` can advance `WorkflowStage` when fields look complete.
- `ContainerCompletenessOrchestratorService` and admin correction paths can directly update status rows.
- Step 1 has a CMR/no-group pending guard; Step 2 sets `Status = "Complete"` and `WorkflowStage = "ImageAnalysis"` without the same guard.
- Image/evidence matching uses container number in several paths without inspection-level disambiguation.
- `ContainerDataMapperService` joins scanner rows to CCS by container number rather than the same identity tuple used by the scan queue.

Identity drift:

- Service dedupe uses `ContainerNumber + ScannerType + InspectionId`.
- Entity comments indicate `InspectionId` allows repeated scans of the same container.
- Raw sprint migration creates unique `(containernumber, scannertype)`, excluding `InspectionId`.

Required guardrails:

- Extract a single `ContainerCompletenessPolicy` or equivalent method for complete/readiness decisions.
- Reject direct `Status = "Complete"` and `WorkflowStage = "ImageAnalysis"` writes outside the policy owner, or require all such writes to call the policy.
- Add a migration/model consistency test for CCS uniqueness.
- Add regression coverage for the Step 2 CMR/no-group case.
- Add a test that queue completion happens only after the CCS save succeeds.

### `RecordCompletenessStatus` and `RecordExpectedContainer`

Entities:

- `RecordCompletenessStatus`
- `RecordExpectedContainer`

Intended owner:

- `RecordCompletenessBuilder`
- `RecordBuildingService`
- `RecordReconciliationWorker`

Expected invariant:

- Parent record status/workflow stage is derived from expected child containers.
- Child rows represent per-container evidence state.
- Parent aggregate counters and workflow stage should be recomputed together.

Current write paths:

- `RecordCompletenessBuilder` creates the parent and recomputes derived status.
- `RecordBuildingService` attaches expected containers and scanner evidence.
- `RecordReconciliationWorker` repairs missing/late evidence.
- `DecisionSideEffectsService` and image-analysis orchestration paths update parent/child workflow fields after analyst decisions.

Drift:

- Parent `ScannerType` is initialized as null while child rows carry scanner type. Downstream code needing scanner context must infer from children.
- Evidence grouping is container-number based in key reconciliation paths, which can mix inspections when the same container appears more than once.
- The safety-net interval fallback differs from comments/docs/migration expectations.
- The builder uses `DateTime.UtcNow` directly, making deterministic transition tests harder.

Required guardrails:

- Decide whether parent `ScannerType` is intentionally nullable for mixed-scanner records. If yes, document it and add a derived scanner summary; if no, populate it.
- Add inspection-aware evidence matching where scanner/container evidence is joined to expected containers.
- Inject time into the builder/reconciler for deterministic tests.
- Add one test that a parent recompute cannot mark a record downstream from stale or cross-inspection evidence.

### `ContainerScanQueue`

Entity: `ContainerScanQueue`

Intended owner:

- `ContainerScanQueueRepository`
- Processors: `QueueRecoveryService`, container completeness processing paths.

Expected invariant:

- `Pending -> Processing -> Completed` for successful work.
- `Pending -> Processing -> Pending/Failed` for failed attempts.
- `RetryCount` should count failed processing attempts, not both starts and failures.
- `Completed` should mean the side effect is durably applied.
- Recovery should not requeue terminal completed work unless explicitly requested as a replay.

Drift:

- `MarkAsProcessingAsync` increments `RetryCount`.
- `UpdateRetryInfoAsync` increments `RetryCount` again for the same failed attempt.
- Completeness processing can mark the queue item completed before the CCS `SaveChangesAsync`.
- Recovery checks only pending/processing rows when determining if something is already in queue, so completed/failed rows can be re-added.

Required guardrails:

- Move retry increment to one place.
- Complete the queue row after the durable completeness transaction.
- Make replay/recovery policy explicit for completed and failed scans.

### `ICUMSDownloadQueue`

Entity: `ICUMSDownloadQueue`

Intended owner:

- `ICUMSDownloadQueueRepository`
- `IcumPipelineOrchestratorService`

Expected invariant:

- `Pending -> Processing -> Completed` for successful download/ingestion.
- `Pending -> Processing -> Pending/Failed` for failed attempts.
- Retry accounting should increment once per failed attempt.

Drift:

- `MarkAsProcessingAsync` increments `RetryCount`.
- `UpdateRetryInfoAsync` increments `RetryCount` again.
- Some paths treat "no data" as successful completion; that may be correct, but should be explicitly documented as a terminal policy.

Required guardrails:

- Share a queue retry policy with `ContainerScanQueue` or add parallel tests for both repositories.
- Document terminal "no data" behavior.

### `ICUMSSubmissionQueue`

Entity: `ICUMSSubmissionQueue`

Intended owner:

- `ICUMSSubmissionService`
- Controller only for user actions that call the same vocabulary/policy.

Expected invariant:

- Worker-consumed statuses: `Pending`, `Processing`, `Submitted`, `Failed`.
- Retry should put failed rows back into a worker-consumed status.
- UI status buckets should use the same vocabulary as the service/API.

Drift:

- The worker selects `Pending` and retryable `Failed`.
- The retry service sets failed submissions back to `Pending`.
- `ICUMSSubmissionQueueController.RetrySubmission` sets `Status = "Queued"`, which the worker does not select.
- API stats count `Queued` as pending, while service stats count `Pending`.
- The Blazor page buckets use `Pending`, `Submitting`, `Successful`, and `Failed`, while the service writes `Processing` and `Submitted`.

Required guardrails:

- Introduce constants/enum for submission queue status.
- Make controller retry set `Pending` or call the service retry method.
- Align UI buckets with API/service vocabulary.
- Add a regression test that manual retry is picked up by `ProcessPendingSubmissionsAsync`.

### `CMRRedownloadQueue`

Entity: `CMRRedownloadQueue`

Intended owner:

- `CMRRedownloadService`

Expected invariant:

- `Pending -> Processing -> Completed`.
- On failure, increment retry count and either return to `Pending` or mark `Failed` when retries are exhausted.

Current assessment:

- This service appears more self-contained than the scan/download/submission queues.
- It should still be included in shared queue architecture tests so it does not drift.

Required guardrails:

- Add it to a queue status vocabulary inventory.
- Add a retry-count invariant test matching the final policy.

### Scanner and Ingestion Processing Statuses

Surfaces:

- FS6000 scan/file sync statuses
- ASE/image-processing statuses
- `DownloadedFile.ProcessingStatus`
- `ContainerImage.ProcessingStatus`
- `VehicleImport.ProcessingStatus`
- batch/report generation statuses

Expected invariant:

- These are operational pipeline statuses, not business completeness states.
- They should not be used as proof that CCS or record completeness is complete unless translated by the owning completeness policy.

Drift to watch:

- Several repositories use free-form string statuses without shared constants.
- Some UI/read-model code treats `ProcessingStatus`, `Status`, and completeness status as equivalent readiness signals.

Required guardrails:

- Inventory these as lower-priority finite state machines.
- Move shared vocabularies to constants where practical.
- Prevent operational statuses from directly driving CCS `Complete` without the completeness policy.

## Repo-Wide Guardrail Backlog

1. Add an architecture test for `AnalysisGroup.Status` ownership.
2. Add a search-based architecture test for direct CCS `Status = "Complete"` and `WorkflowStage = "ImageAnalysis"` writes outside an allowlist.
3. Extract or centralize a CCS completeness predicate and make all writers call it.
4. Add an EF/model/migration test for CCS uniqueness: service identity must match database uniqueness.
5. Add queue retry invariant tests for `ContainerScanQueue`, `ICUMSDownloadQueue`, `ICUMSSubmissionQueue`, and `CMRRedownloadQueue`.
6. Add a manual ICUMS submission retry test proving the retried row is selected by the worker.
7. Add inspection-aware evidence matching tests for record and container completeness.
8. Add operational SQL diagnostics for impossible states:
   - CCS rows marked complete without all required evidence.
   - CCS image-analysis rows blocked by CMR/no-group requirements.
   - ICUMS submission rows in unknown statuses.
   - queue rows whose retry count exceeds attempts by more than policy allows.

## Why The Earlier Architecture Review Missed These

The earlier review appears to have found the architectural intent: state machine facade, completeness services, record builder/reconciler, and queue repositories. That is different from proving every writer obeys the intent.

The missed issues live in cross-service invariants:

- A service-local review sees `ContainerStatusReconciliationService` updating CCS and can consider it reasonable.
- A repo-wide invariant review asks whether that update uses the same complete predicate as `ContainerCompletenessService`.
- A service-local queue review sees retry count incremented in a processing method and again in retry handling.
- A repo-wide invariant review asks what a single failed attempt should mean across all queues.
- A state-machine review sees `AnalysisGroup.Status` protected.
- A repo-wide ownership review notices another helper still documents CCS workflow as canonical.

The next useful architectural review should therefore be grep-backed and invariant-led: start from every durable status field, enumerate every writer, classify the owner, and then reject writes that do not go through the owner.

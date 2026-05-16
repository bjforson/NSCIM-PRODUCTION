# CMR Composite-Key Remaining Work Reanalysis

Date: 2026-05-15
Scope: docs-only reanalysis after the image/split interruption work
Primary input docs:

- `docs/cmr-composite-key-blast-radius-and-implementation-plan-2026-05-13.md`
- `docs/cmr-composite-key-giant-implementation-todo-2026-05-13.md`
- `docs/cmr-composite-key-completeness-scope-2026-05-13.md`

## Executive Position

The remaining CMR work is no longer "add composite-key completeness." The current tree already has the guarded composite-key foundation:

- `CmrCompositeKeyHelper` creates route-safe `CMR-{hash}` operational keys from rotation number, container number, and BL number.
- `ContainerCompletenessPolicy` can allow CMR rows through when scanner, ICUMS, image, and all composite-key parts exist, while keeping incomplete CMR rows held.
- `ContainerCompletenessService` and `ContainerStatusReconciliationService` pass CMR composite inputs into the policy.
- `RecordCompletenessBuilder`, `RecordBuildingService`, and `RecordReconciliationWorker` can build CMR `RecordCompletenessStatus` and `RecordExpectedContainer` rows behind `CmrCompositeProgression:Enabled`.
- `ImageAnalysisOrchestratorService` has record-anchored intake that uses `GroupType = "CMR"` and `RecordCompletenessStatusId`.
- `DecisionSideEffectsService` now syncs analyst decisions back into record-completeness rollups.
- `SubmissionWorkflowStageSync` now has a central path to move CCS rows to `PendingSubmission` / `Submitted` and update record-completeness submission rollups.
- Source-scan/split work from the interruption has improved resolver-backed image display and protects against comma-joined ASE pseudo-container strings becoming the primary analysis identity.

The next safe implementation target is the end-to-end CMR lifecycle from an already-built CMR record through analyst, audit, ICUMS payload, submitted state, and later CMR-to-BOE upgrade. That corridor still has verification and some likely implementation gaps.

Both checked config files keep `CmrCompositeProgression:Enabled = false`; `ICUMS:Submission:LiveSubmitEnabled` also remains false in `appsettings.json`. Treat the current implementation as staged and guarded, not broadly live.

## Services Touched Or In The Blast Radius

Current confirmed touched services and controllers:

- Core identity and policy:
  - `src/NickScanCentralImagingPortal.Core/Helpers/CmrCompositeKeyHelper.cs`
  - `src/NickScanCentralImagingPortal.Core/Helpers/ContainerCompletenessPolicy.cs`
  - `src/NickScanCentralImagingPortal.Core/Entities/RecordCompletenessStatus.cs`
  - `src/NickScanCentralImagingPortal.Core/Entities/RecordExpectedContainer.cs`
- Container completeness:
  - `src/NickScanCentralImagingPortal.Services/ContainerCompleteness/ContainerCompletenessService.cs`
  - `src/NickScanCentralImagingPortal.Services/ContainerCompleteness/ContainerStatusReconciliationService.cs`
  - `src/NickScanCentralImagingPortal.Services/ContainerCompleteness/ContainerScanQueuePublisherService.cs`
  - `src/NickScanCentralImagingPortal.Services/ContainerCompleteness/ICUMSSubmissionService.cs`
- Record completeness:
  - `src/NickScanCentralImagingPortal.Services/RecordCompleteness/RecordCompletenessBuilder.cs`
  - `src/NickScanCentralImagingPortal.Services/RecordCompleteness/RecordBuildingService.cs`
  - `src/NickScanCentralImagingPortal.Services/RecordCompleteness/RecordReconciliationWorker.cs`
  - `src/NickScanCentralImagingPortal.Services/RecordCompleteness/RecordCompletenessRollupSync.cs`
- Image analysis, assignment, audit, and submission:
  - `src/NickScanCentralImagingPortal.Services/ImageAnalysis/ImageAnalysisOrchestratorService.cs`
  - `src/NickScanCentralImagingPortal.Services/ImageAnalysis/DecisionSideEffectsService.cs`
  - `src/NickScanCentralImagingPortal.Services/ImageAnalysis/SubmissionWorkflowStageSync.cs`
  - `src/NickScanCentralImagingPortal.Services/ImageAnalysis/ReadyGroupsCacheService.cs`
  - `src/NickScanCentralImagingPortal.API/Controllers/ImageAnalysisController.cs`
  - `src/NickScanCentralImagingPortal.API/Controllers/ImageAnalysisDecisionController.cs`
  - `src/NickScanCentralImagingPortal.API/Controllers/AuditReviewController.cs`
- ICUMS and validation:
  - `src/NickScanCentralImagingPortal.Services/IcumApi/IcumJsonIngestionService.cs`
  - `src/NickScanCentralImagingPortal.Services/IcumApi/ICUMSDownloadQueueService.cs`
  - `src/NickScanCentralImagingPortal.Services/ContainerValidation/ContainerValidationService.cs`
  - `src/NickScanCentralImagingPortal.API/Controllers/IcumsPayloadController.cs`
  - `src/NickScanCentralImagingPortal.API/Controllers/ICUMSSubmissionQueueController.cs`
- Source scan and split interruption area:
  - `src/NickScanCentralImagingPortal.Services.ImageProcessing/ScanAssetResolver.cs`
  - `src/NickScanCentralImagingPortal.Services/ImageSplitter/TwoContainerSplitIntakeService.cs`
  - `src/NickScanCentralImagingPortal.API/Controllers/ScanAssetsController.cs`
  - `src/NickScanCentralImagingPortal.API/Controllers/ImageSplitterController.cs`
  - WebApp image/audit/split views under `src/NickScanWebApp.New/Components/Operations` and `src/NickScanWebApp.New/Pages/ImageAnalysis`
- Config and controls:
  - `CmrCompositeProgression:Enabled`
  - `ICUMS:Submission:LiveSubmitEnabled`
  - `ScannerWorkflow:*`, especially Eagle A25 assignment and split intake gates
  - Decision Agent settings and active-human-assignment guard

## Composite Key Progression Rules

These are the progression rules the remaining work should preserve:

1. A CMR row may progress only when all of these are true:
   - scanner data exists
   - ICUMS CMR data exists
   - image data or a resolver-backed usable source scan exists
   - rotation number is present
   - container number is present
   - BL number is present
   - `CmrCompositeProgression:Enabled` is true

2. The operational key is not the BOE/declaration number. It is a synthetic route-safe key:
   - source parts: `rotation|container|bl`
   - stored key: `CMR-{20 hex chars}`
   - display label: `CMR {container} / {rotation} / {bl}`

3. `AnalysisRecord.ContainerNumber` must always be the real logical container number. Comma-joined source scan labels such as `CONT1, CONT2` are scanner-source labels only.

4. `AnalysisGroup.GroupIdentifier` for CMR should be the CMR operational key, with `GroupType = "CMR"` and `RecordCompletenessStatusId` populated.

5. CMR rows missing any key part must stay blocked or pending. They must not enter analyst assignment as "complete."

6. When a later IM/EX BOE arrives, the system must link or migrate to the real BOE identity without creating duplicate RCS, AG, AR, active assignment, audit, or submission rows.

## Current Implementation Read

### Record Completeness

Implemented enough to support a controlled CMR pilot:

- CMR records can be built by composite key through `RecordCompletenessBuilder.BuildCmr`.
- Event-driven building exists through `RecordBuildingService.BuildOrUpdateCmrRecordAsync`.
- Safety-net reconciliation exists through `RecordReconciliationWorker.ReconcileCmrCompositeRecordsAsync`.
- `RecordCompletenessBuilder.Recompute` now understands `Ready`, `InAudit`, `PendingSubmission`, and `Completed` from child states.
- Analyst decision sync calls `RecordCompletenessRollupSync.MarkContainerDecidedAsync`.
- Submission success sync calls `RecordCompletenessRollupSync.MarkContainerSubmittedAsync`.

Remaining record-completeness work:

- Verify the exact pilot CMR record reaches `Ready` from real CCS evidence after the split/image interruption changes.
- Add or run an end-to-end test from CMR RCS -> AG/AR -> analyst decision -> record child `Decided` -> record parent `InAudit`.
- Add or run an end-to-end submission test from CMR audit completion -> ICUMS payload -> record child `Submitted` -> record parent `Completed`.
- Verify that the current safety-net reconciliation does not create duplicate CMR records when event-driven building has already run.
- Verify CMR-to-BOE upgrade behavior in `IcumJsonIngestionService` for a CMR that already has RCS/AG/AR rows.

### Container Completeness

Implemented foundation:

- Creation and recheck paths call `ContainerCompletenessPolicy.Evaluate` with CMR composite inputs.
- Existing CMR operational keys are protected from being overwritten by blank declaration/group identifiers.
- Reconciliation can derive a CMR operational key and set CCS `GroupIdentifier` when enabled.

Remaining container-completeness work:

- Re-run a read-only count of CMR rows with scanner + ICUMS + image + complete composite key that are still blocked at `AwaitingDeclaration`.
- Verify existing `AwaitingDeclaration` rows are recomputed/backfilled to `Complete` / `ImageAnalysis` only for complete composite keys.
- Verify every pilot CCS row is stamped to the CMR operational key before assignment materialization.
- Normalize or intentionally document the audit-completion CCS stage path. `AuditReviewController` currently has a direct path that marks matched CCS rows `WorkflowStage = "Completed"` after audit, while the submission workflow expects to transition eligible rows to `PendingSubmission` and then `Submitted`. The submission sync accepts `Completed` as a source stage, but the lifecycle is harder to reason about and needs a direct CMR regression test or a code normalization phase.

### Image And Audit Completion Rollups

Implemented or partially implemented:

- Record-anchored intake creates CMR analysis groups with `GroupType = "CMR"` and real container `AnalysisRecord` rows.
- `DecisionSideEffectsService` now updates `AnalysisRecord` to `Decided` and syncs the linked record-completeness child/parent.
- `SubmissionWorkflowStageSync` uses group identifiers plus `AnalysisRecords` to find CCS rows even when CCS group identifiers drift.
- `ReadyGroupsCacheService` has resolver and record-linked paths that reduce earlier "ready but invisible" risks.

Remaining rollup work:

- Audit completion still needs a first-class CMR side-effect test. The controller currently completes `AnalysisGroup` and CCS, but record-completeness `PendingSubmission` state is mostly inferred later through submission generation rather than being a clearly owned audit-completion side effect.
- Verify no drift sweeper, orphan sweeper, or Decision Agent path cancels or bypasses valid CMR groups.
- Verify active assignment rows are released after analyst and audit completion for CMR groups.
- Verify audit views and APIs resolve CMR operational keys and scanner-specific groups without falling back to container-only ambiguity.
- Keep Decision Agent out of CMR until there is an explicit rule and test for CMR eligibility.

### ICUMS Submission

Implemented or partially implemented:

- The orchestrator generates per-container ICUMS JSON payloads in the submission outbox.
- The payload shape currently includes `DeclarationNumber`, `RotationNumber`, `BlNumber`, `ContainerNumber`, scan timing, analyst/auditor, verdict, findings, and image document.
- `ValidateSubmissionGateAsync` intentionally excludes declaration-completeness rules so CMR payloads are not automatically rejected solely because BOE/declaration is blank.
- Live HTTP submission is guarded by `ICUMS:Submission:LiveSubmitEnabled`.
- On HTTP success, `SubmissionWorkflowStageSync.MarkContainerSubmittedAsync` moves CCS and linked record rollups to submitted.

Remaining submission work:

- Confirm with ICUMS whether CMR payloads should send:
  - `DeclarationNumber = ""`
  - omit `DeclarationNumber`
  - or send another agreed placeholder.
- Replace or verify the payload BOE lookup for CMR. The current payload builder loads BOE data by container and selects the latest row. For CMR, this should be verified against the linked record's `PrimaryBoeDocumentId` or the composite key parts to avoid accidentally selecting a later IM/EX row or another CMR row for the same container.
- Verify `RotationNumber` and `BlNumber` in the generated payload are the linked CMR values, not just whichever BOE row was most recently created for the container.
- Verify payload file naming and retry extraction continue to use the real container number and group id safely for CMR.
- Verify acknowledged-file reconciliation and retry paths can mark the correct CMR CCS/RCS rows if the app crashes after HTTP success or after file archive but before DB update.
- Verify the payload viewer and logs display CMR identity as CMR, not as a strange BOE key.

## Key Remaining Implementation Phases

### Phase A: Re-baseline After Image/Split Work

Purpose: prove the interruption work did not leave the CMR pilot unable to load images or split crops.

Actions:

- Re-run the current dry-run candidate query for CMR composite keys.
- Re-run the known split/image smoke for `40426305424_W1` or the current pilot's source-scan equivalent.
- Confirm the pilot CMR's scanner tab, image tab, fullscreen image, split-choice original, and split crops use resolver-backed identity and nonzero image bytes.
- Confirm `CmrCompositeProgression:Enabled` and `ICUMS:Submission:LiveSubmitEnabled` remain off before any production pilot action.

Exit gate:

- The pilot CMR has scanner, ICUMS, image/source-scan, and complete composite-key evidence without relying on a comma-joined pseudo-container as the business identity.

### Phase B: Complete CMR Readiness To Assignment

Purpose: move one pilot CMR from CCS/RCS readiness into normal analyst work.

Actions:

- Recompute or backfill the pilot CCS row to `Complete` / `ImageAnalysis` with the CMR operational key.
- Build or verify the CMR `RecordCompletenessStatus` and `RecordExpectedContainer`.
- Run record-anchored intake and confirm:
  - `AnalysisGroup.GroupIdentifier = CMR-*`
  - `GroupType = "CMR"`
  - `RecordCompletenessStatusId` is set
  - `AnalysisRecord.ContainerNumber` is the real container
  - queue materialization sees the group for an active Analyst user
- Confirm predictive preload does not need full image bytes and does not poison cache with missing image/source-scan negatives.

Exit gate:

- The pilot CMR appears as assignable analyst work with correct CMR identity and image access.

### Phase C: Analyst And Audit Lifecycle

Purpose: close the point-7 workflow gap before touching live ICUMS submission.

Actions:

- Have Analyst save a normal pilot decision.
- Verify:
  - `ImageAnalysisDecision` row exists
  - `AnalysisRecord.Status = Decided`
  - linked `RecordExpectedContainer.Status = Decided`
  - linked `RecordCompletenessStatus.Status = InAudit`
  - `WorkflowStage = Audit`
  - assignment is released or moved correctly
- Have Auditor save the pilot audit.
- Verify:
  - `AuditDecision` row exists and is completed
  - `AnalysisGroup` transitions to `AuditCompleted` or the configured submission-ready state
  - CCS stage is either normalized to `PendingSubmission` or intentionally covered by `SubmissionWorkflowStageSync` from `Completed`
  - no sweeper cancels the group
  - record completeness can move to `PendingSubmission` without waiting for an unrelated reconciliation tick

Exit gate:

- The pilot CMR progresses through Analyst and Audit with coherent CCS, RCS, AG, AR, assignment, and audit states.

### Phase D: CMR Payload And Submission State

Purpose: prove generated payload identity and safe state updates before live batch drain.

Actions:

- Generate the pilot ICUMS payload with live submit off.
- Inspect the JSON:
  - real `ContainerNumber`
  - CMR `RotationNumber`
  - CMR `BlNumber`
  - agreed `DeclarationNumber` handling
  - nonempty image document
  - stable scan reference
  - analyst/auditor names
  - verdict/findings
- Confirm Layer 5 submission gate does not fail solely due to CMR blank declaration.
- With ICUMS approval and live submit controlled, submit one payload.
- Verify:
  - payload is archived before DB submitted-state update
  - correct CCS row moves to `Submitted`
  - correct `RecordExpectedContainer` moves to `Submitted`
  - `RecordCompletenessStatus` reaches `Completed`
  - retry does not duplicate submission
  - acknowledged-file reconciliation can repair any partial state

Exit gate:

- The pilot CMR payload is accepted by ICUMS and NSCIM shows submitted/completed state on the correct records.

### Phase E: CMR-To-BOE Upgrade Protection

Purpose: prevent duplicate work when a CMR later receives a real BOE declaration.

Actions:

- Trace `IcumJsonIngestionService` CMR upgrade behavior for:
  - CMR before assignment
  - CMR during analyst or audit assignment
  - CMR after payload generation
  - CMR after submitted/acknowledged state
- Ensure later IM/EX record building either links to or safely coexists with the CMR RCS/AG lineage.
- Ensure `FindExistingCmrCompositeGroupForRealRecordAsync` blocks duplicate real-declaration AG creation when the active CMR group already covers the same container/rotation/BL.
- Add regression tests for each upgrade timing.
- Define whether the UI should immediately switch from CMR display to BOE display or preserve CMR label until submission is complete.

Exit gate:

- BOE arrival does not create duplicate assignments or lose analyst/audit/submission history.

### Phase F: Small-Batch Rollout

Purpose: drain production CMR backlog in controlled, observable steps.

Actions:

- Keep broad feature flag off by default.
- Use one-container pilot first.
- Expand only after clean pilot:
  - batch 5
  - batch 25
  - batch 100
- After each batch verify:
  - duplicate CMR composite keys = 0
  - duplicate RCS for CMR operational keys = 0
  - duplicate AG for CMR operational keys/scanner = 0
  - duplicate AR `(GroupId, ContainerNumber)` = 0
  - duplicate active assignments = 0
  - no stuck `AuditCompleted` without payload
  - no `Submitted` payload with CCS/RCS still not submitted
  - no CMR missing-key row entered assignment

Exit gate:

- CMR backlog drains with no duplicate assignments, no wrong submissions, and documented blockers for any rows left behind.

## Blockers

Hard blockers before broad rollout:

- ICUMS agreement is still needed for exact CMR `DeclarationNumber` behavior in outbound payloads.
- No current evidence in this reanalysis proves the post-interruption pilot has been driven through Analyst -> Audit -> ICUMS submission.
- Audit-completion to submission-ready state ownership is not clean enough yet. It needs either normalization in code or a direct CMR regression test proving the current `Completed` -> `PendingSubmission` bridge is reliable.
- CMR payload BOE lookup by latest container row must be verified or tightened to the linked CMR document/composite key.
- CMR-to-BOE upgrade duplicate-prevention is still a required gate before batch expansion.

Operational blockers:

- `CmrCompositeProgression:Enabled` is false in checked config.
- `ICUMS:Submission:LiveSubmitEnabled` is false in checked config.
- At least one active Analyst and one active Audit user must be ready for pilot assignment/audit validation.
- Decision Agent must stay excluded or disabled for CMR until explicitly tested.

## Risks

- Incomplete CMR reaches analyst queue because a row has scanner/image but a missing rotation/container/BL part.
- Comma-joined ASE source labels leak into `AnalysisRecord.ContainerNumber`, cache keys, UI routes, or payload identity.
- Audit completion marks CCS `Completed` while record completeness remains `InAudit`, creating a hidden gap until submission.
- ICUMS payload selects the wrong BOE/CMR row for a reused container number.
- Live submit marks the wrong CCS row because matching falls back to container-only identity.
- CMR upgrades to IM/EX and creates a second AG/AR/assignment for the same work.
- Decision Agent processes CMR without business approval.
- UI shows `CMR-*` as if it were a BOE/declaration number.
- Predictive preload caches a negative scan/image result before split resolution finishes and keeps serving stale missing data.

## Test Plan Still Needed

Already covered or partially covered:

- `CmrCompositeKeyHelperTests`
- `ContainerCompletenessPolicyTests`
- `RecordBuildingServiceCmrTests`
- Source guardrail tests around CMR feature gate, record-backed intake, duplicate protection, and split pseudo-container prevention
- Queue progression tests for decision rollup and submission rollup
- Source-scan/split resolver tests around analysis record and split job identity

Add or run before broad rollout:

- CMR intake-to-queue service test with an active Analyst user.
- CMR analyst decision side-effects test from AG/AR through RCS child/parent.
- CMR audit side-effects test proving audit completion moves the correct lifecycle state toward submission readiness.
- CMR payload generation test proving rotation/container/BL identity and agreed declaration behavior.
- CMR submission-gate test with blank declaration and valid CMR composite data.
- CMR submitted-state sync test where CCS group identifier is stale but AG/AR has the right container.
- CMR acknowledged-file reconciliation test after partial success.
- CMR-to-BOE upgrade tests before assignment, during assignment, after audit, and after submission.
- CMR UI smoke checklist for Workbench, Audit Review, Cargo Group, Record Completeness, Payload Viewer, and CMR Validation.
- Predictive preload CMR assignment/context test.
- Batch dry-run/apply tests for pilot, 5, 25, and 100 row batches.

Suggested focused test order:

1. `dotnet test tests/NickScanCentralImagingPortal.Core.Tests/NickScanCentralImagingPortal.Core.Tests.csproj --filter "CmrCompositeKeyHelperTests|ContainerCompletenessPolicyTests|StateOwnershipGuardrailTests|CmrCompositeRecordIntakeGuardrailTests"`
2. `dotnet test src/NickScanCentralImagingPortal.Tests/NickScanCentralImagingPortal.Tests.csproj --filter "RecordBuildingServiceCmrTests|QueueProgressionRegressionTests|SourceScanSplitFlowRegressionTests|ScanAssetResolverTests"`
3. Add/run a new CMR audit-and-submission focused regression slice before live submission.
4. Run the staging/pilot runner dry-run and apply only after the focused tests are green.

## Verification Plan

Read-only baseline before enabling anything:

- Count progressable CMR candidates:
  - CMR rows with scanner + ICUMS + image/source-scan + rotation + container + BL.
- Count blocked CMR rows by missing key part.
- Count duplicate composite keys.
- Count existing CMR RCS rows.
- Count existing CMR AG rows.
- Count CMR active assignments.
- Count CMR groups in `AuditCompleted`, `PendingSubmission`, `Submitted`, and `Completed`.
- Count CMR payload files in Outbox and Acknowledged folders.

Pilot verification:

- Use one known pilot CMR operational key.
- Verify CCS:
  - `Status = Complete`
  - `WorkflowStage = ImageAnalysis` before assignment
  - `GroupIdentifier = CMR-*`
  - `ClearanceType = CMR`
- Verify RCS:
  - `DeclarationNumber = CMR-*`
  - `ClearanceType = CMR`
  - `RotationNumber`, `BlNumber`, and child container match ICUMS CMR row
  - rollup reaches `Ready`
- Verify AG/AR:
  - AG uses `GroupType = CMR`
  - AG has `RecordCompletenessStatusId`
  - AR container is the real container
- Verify analyst:
  - decision saved
  - AR child moves to `Decided`
  - RCS child moves to `Decided`
  - RCS parent moves to `InAudit` / `Audit`
- Verify audit:
  - audit decision saved
  - AG moves to `AuditCompleted`
  - CCS/RCS are in the expected submission-ready path
  - assignments are released
- Verify payload:
  - JSON identity is the CMR composite identity, not a stray latest BOE row
  - image document is present
  - Layer 5 gate passes
- Verify live submission only after ICUMS approval:
  - payload archived
  - CCS submitted
  - RCS submitted/completed
  - no duplicate payload/submission on retry

Batch verification after each expansion:

- Run duplicate counts for RCS, AG, AR, assignments, and payloads.
- Check logs for:
  - `RECORD-BUILD`
  - `RECORD-RECON`
  - `INTAKE-RECORD`
  - `ASSIGNMENT-EVENT`
  - `DECISION-FX`
  - `ICUMS-PAYLOAD`
  - `ICUMS-SUBMIT`
  - `ICUMS-ACK-RECONCILE`
- Check dashboards/queues for stuck CMR work.
- Pause expansion immediately on any wrong-row submission, duplicate active assignment, or CMR missing-key assignment.

## Recommended Next Step

Do not jump straight to batch drain. The next implementation phase should be a controlled pilot focused on point 7:

1. Reconfirm the pilot CMR image/split surface after the interruption fixes.
2. Drive the same pilot through Analyst and Audit.
3. Generate the CMR ICUMS payload with live submit off.
4. Fix any identity or stage-sync gaps found there.
5. Get ICUMS confirmation for payload declaration handling.
6. Only then enable one live pilot submission and proceed to small batches.

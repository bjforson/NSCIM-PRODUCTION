# CMR Composite-Key Completeness Scope

Date: 2026-05-13
Status: Draft scope before implementation plan

## Goal

Allow CMR records to progress to image analysis when the record has the ICUMS-approved composite key:

- Rotation number / manifest number
- Container number
- BL number

This replaces the old behavior where CMR records were held at `AwaitingDeclaration` until a BOE/declaration number arrived.

## Recommendation

Use the existing record-backed analysis pipeline. Do not create a separate CMR-only assignment queue.

The CMR fix should make complete CMR containers produce normal `RecordCompletenessStatus` records, then let the existing record-anchored image analysis intake create `AnalysisGroup`, `AnalysisRecord`, assignment queue entries, analyst work, audit work, and submission state.

## Production Facts From Analysis

- All checked CMR BOE-document rows have the three proposed key fields.
- The composite key is unique across checked CMR data.
- Existing key lengths fit current `RecordCompletenessStatus.DeclarationNumber` and `AnalysisGroup.GroupIdentifier` limits.
- About 1,323 production CMR rows currently have scanner data, ICUMS data, and image data but remain blocked at `AwaitingDeclaration`.
- Some blocked ASE rows are composite scan rows with comma-joined container strings. Those need careful handling so payload and analysis records use the real BOE/container identity, not the comma string as a fake container.

## Services And Areas To Touch

### 1. Completeness Gate

Files:

- `src/NickScanCentralImagingPortal.Core/Helpers/ContainerCompletenessPolicy.cs`
- `src/NickScanCentralImagingPortal.Services/ContainerCompleteness/ContainerCompletenessService.cs`

Required changes:

- Add a CMR-ready path when scanner data, ICUMS data, image data, rotation number, container number, and BL number are present.
- Stop treating all declaration-less CMR rows as `AwaitingDeclaration`.
- Stamp a stable CMR group identifier instead of leaving `GroupIdentifier` blank.
- Keep incomplete CMR rows in `AwaitingDeclaration` or `Missing` with clear missing-field reason.
- Preserve existing IM/EX behavior.

### 2. CMR Composite Key Helper

Likely new file:

- `src/NickScanCentralImagingPortal.Core/Helpers/CmrCompositeKeyHelper.cs`

Required changes:

- Centralize validation and key generation.
- Normalize trim/case consistently.
- Produce a stable key from rotation number, container number, and BL number.
- Prefer a route-safe key if UI/API routes pass group identifiers directly.

Candidate key formats:

- Human-readable: `CMR|{rotation}|{container}|{bl}`
- Route-safe: `CMR-{hash}` with display fields stored separately

Recommendation: start with route-safe storage if routes are a concern; otherwise readable key is acceptable based on current length analysis.

### 3. Record Completeness

Files:

- `src/NickScanCentralImagingPortal.Services/RecordCompleteness/RecordBuildingService.cs`
- `src/NickScanCentralImagingPortal.Services/RecordCompleteness/RecordReconciliationWorker.cs`
- `src/NickScanCentralImagingPortal.Services/RecordCompleteness/RecordCompletenessBuilder.cs`
- `src/NickScanCentralImagingPortal.Core/Entities/RecordCompletenessStatus.cs`
- `src/NickScanCentralImagingPortal.Core/Entities/RecordExpectedContainer.cs`
- `src/NickScanCentralImagingPortal.API/Controllers/RecordCompletenessController.cs`

Required changes:

- Add a CMR record-building path keyed by the CMR composite key, not `DeclarationNumber`.
- Create/update `RecordCompletenessStatus` for CMR rows with:
  - `ClearanceType = "CMR"`
  - synthetic operational identity in the existing declaration/group identity field, or a new explicit field if we choose a schema migration
  - `RotationNumber`
  - `BlNumber`
  - `PrimaryBoeDocumentId`
  - scanner type
  - expected container children
- Add CMR rows to the reconciliation worker, which currently only scans `IM` and `EX` declarations.
- Recompute CMR records using the same Ready, PartiallyReady, PendingSubmission, InAudit, Completed status logic.
- Backfill or build records for existing blocked CMR rows after deployment.
- Ensure the record-completeness API displays CMR records clearly as CMR, not as odd BOE declarations.

This is the central workstream. Without it, complete CMR containers will still fail to enter the active analyst assignment path.

### 4. Image Analysis Intake

Files:

- `src/NickScanCentralImagingPortal.Services/ImageAnalysis/ImageAnalysisOrchestratorService.cs`
- `src/NickScanCentralImagingPortal.Services/ImageAnalysis/ReadyGroupsCacheService.cs`

Required changes:

- Let record-anchored intake create `AnalysisGroup` and `AnalysisRecord` rows from CMR `RecordCompletenessStatus` rows.
- Use `GroupType = "CMR"` or another explicit CMR marker.
- Set `RecordCompletenessStatusId` so downstream decisions update record completeness.
- Back-stamp `ContainerCompletenessStatus.GroupIdentifier` using the CMR key.
- Refresh queue entries after CMR group creation.
- Harden orphan checks and queue counts for CMR records, especially composite ASE scans.

### 5. Analyst Assignment Queue

Files:

- `src/NickScanCentralImagingPortal.Services/ImageAnalysis/ReadyGroupsCacheService.cs`
- Relevant assignment controllers if display fields need to change.

Required changes:

- Confirm CMR groups appear as assignable Analyst work.
- Display useful identity fields to users: container, rotation number, BL number, scanner type.
- Avoid exposing only a synthetic internal key as the main user-facing label.
- Confirm predictive preload sees CMR assignments after normal queue materialization.

### 6. Decision And Audit Side Effects

Files:

- `src/NickScanCentralImagingPortal.Services/ImageAnalysis/DecisionSideEffectsService.cs`
- `src/NickScanCentralImagingPortal.API/Controllers/ImageAnalysisDecisionController.cs`
- `src/NickScanCentralImagingPortal.API/Controllers/AuditReviewController.cs`

Required changes:

- Verify decisions move CMR `AnalysisRecord` rows through the same status path.
- Verify `RecordExpectedContainer` rows are updated through the `RecordCompletenessStatusId` linkage.
- Verify CCS rows move from `ImageAnalysis` to `Audit`, `PendingSubmission`, and `Submitted`.
- Add CMR-specific regression tests where needed.

### 7. ICUMS Submission And Payloads

Files:

- `src/NickScanCentralImagingPortal.Services/ImageAnalysis/ImageAnalysisOrchestratorService.cs`
- `src/NickScanCentralImagingPortal.Services/ContainerValidation/ContainerValidationService.cs`
- `src/NickScanCentralImagingPortal.Services/Validation/CMRValidationService.cs`
- `src/NickScanCentralImagingPortal.API/Controllers/IcumsPayloadController.cs`

Required changes:

- Generate CMR submission payloads with rotation number, container number, and BL number.
- Do not require BOE/declaration number for CMR submission.
- Update submission-gate validation so valid CMR composite-key records pass.
- Mark the correct CCS rows as `Submitted`, even for composite scan cases.
- Confirm retry logic handles CMR payloads without falling back to declaration-only assumptions.

### 8. CMR To BOE Upgrade Lifecycle

Files:

- `src/NickScanCentralImagingPortal.Services/IcumApi/IcumJsonIngestionService.cs`
- `src/NickScanCentralImagingPortal.Infrastructure/Repositories/IcumDownloadsRepository.cs`

Required changes:

- Preserve the existing CMR-to-BOE upgrade lookup by container, rotation number, and BL number.
- Decide what happens when a CMR already entered analysis and later becomes IM/EX:
  - If analysis is not submitted, relink/update the existing record/group to the BOE declaration.
  - If analysis is already submitted, preserve audit lineage and avoid duplicate analysis.
  - If a duplicate IM/EX record would be created, suppress or merge it.
- Add audit logging for upgrade actions that affect record completeness or analysis groups.

### 9. API And UI Display

Files to verify:

- `src/NickScanCentralImagingPortal.API/Controllers/RecordCompletenessController.cs`
- `src/NickScanCentralImagingPortal.API/Controllers/ImageAnalysisController.cs`
- `src/NickScanCentralImagingPortal.API/Controllers/AuditReviewController.cs`
- `src/NickScanWebApp.New` assignment and analysis views
- `src/NickScanWebApp.Shared` DTOs if CMR labels need fields added

Required changes:

- Show CMR identity as container + rotation + BL.
- Avoid confusing users by displaying synthetic CMR keys as if they were BOE numbers.
- Ensure routes can safely carry the group identifier, or pass a numeric/group id instead.

### 10. Data And Backfill

Required changes:

- Add a one-time backfill or operator job for currently blocked CMR rows.
- Recompute CCS rows that are `AwaitingDeclaration` but now satisfy CMR composite-key readiness.
- Build missing CMR record-completeness rows.
- Let normal intake create analysis groups and queue entries.
- Run in small batches first, then full production batch after verification.

## Database Scope

Expected minimum:

- No required table redesign if we reuse existing identity fields with a CMR synthetic key.
- Add or verify indexes for CMR lookup by `ClearanceType`, `ContainerNumber`, `RotationNumber`, and `BlNumber`.

Optional, cleaner schema:

- Add explicit operational identity fields, such as `RecordIdentityType`, `OperationalKey`, or `CmrCompositeKey`.
- This improves clarity but increases migration and UI/API scope.

Recommendation:

- Use existing columns for the first safe implementation.
- Add schema fields only if route/display limitations make synthetic keys risky.

## Regression Tests Required

- CMR completeness policy marks complete when the three-key rule is satisfied.
- CMR incomplete rows remain held when any key is missing.
- IM/EX completeness behavior remains unchanged.
- CMR record completeness row is created from BOE document plus CCS/image data.
- CMR record-anchored intake creates analysis group and analysis records.
- Ready groups cache exposes CMR group as Analyst assignable.
- Decision side effects advance CMR record and CCS states.
- CMR payload generation uses rotation, container, and BL.
- Submission gate accepts valid CMR without BOE.
- CMR-to-BOE upgrade does not create duplicate assignments or duplicate submissions.
- Composite ASE scan rows do not create comma-string pseudo-container analysis records.

## Main Risks

- Accidentally letting incomplete CMR records into analyst work.
- Creating duplicate analysis groups when CMR later upgrades to IM/EX.
- Marking the wrong CCS row submitted for composite scan records.
- Displaying internal synthetic keys to users as BOE numbers.
- Breaking IM/EX record completeness if the shared builder is changed too broadly.

## Current Conclusion

Record completeness is a required first-class part of the fix. The implementation should not bypass it. The safest path is:

1. Add CMR composite-key identity.
2. Update container completeness to mark eligible CMR rows complete.
3. Build CMR record-completeness rows.
4. Let existing record-anchored image analysis intake create assignments.
5. Patch CMR submission and CMR-to-BOE upgrade handling.

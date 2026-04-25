# Refactor Plan: Split `IcumJsonIngestionService` (2,513 LOC)

**Status:** PLAN ONLY — not started.
**Created:** 2026-04-25
**Audit reference:** Architecture finding "mega files", 4th-largest in codebase.

---

## Why split

- 6 distinct responsibilities crammed into one `BackgroundService`.
- Hot-spot for ingestion bugs (5 of the 14 audit fixes this cycle landed here).
- Untestable as-is: no DI seams, no tests exist, and the constructor reaches into
  `IServiceProvider.CreateScope()` to resolve scoped collaborators.

## Why NOT split today (must-do prereqs)

1. **Zero tests today.** Splitting without tests is a regression vector for the
   most critical ingestion path. **Add characterization tests first** against
   the existing seams (`ProcessSingleFileAsync`, `ParseBOEDocumentAsync`,
   `ValidateCriticalFieldsAsync`).
2. **Field-extraction state is shared** (`FieldExtractionTracker`,
   `StreamingJsonParser`, `_recordBuildingService`). These need to become
   first-class services injected into the new collaborators, not transferred
   via constructor field-passing.
3. **Constructor scope juggling.** The existing class manually creates and
   holds `IServiceScope _scope`. Split classes can't all do this — the scope
   must be owned by one orchestrator and passed down, OR all collaborators
   must be transient + receive the scope as a parameter.

## Proposed seams

| New class | Responsibility | Lines moved | LOC |
|---|---|---|---|
| **IcumJsonIngestionService** (kept) | `BackgroundService` shell, outer loop, circuit breaker, batch driver | 158-468 | ~310 |
| **BOEDocumentParser** | JSON → BOEDocument mapping (`ParseBOEDocumentAsync`, `ParseBOEDocumentForVINAsync`, `ProcessVINRecordAsync`) | 1059-1817 | ~760 |
| **IcumDocumentValidator** | `ValidateCriticalFieldsAsync`, `ValidateIngestedDocumentAsync`, `DetectAndClassifyConsolidatedCargoAsync`, `LogDataQualityMetrics` | 1818-2148 | ~330 |
| **IcumFileLifecycleManager** | `CleanupArchivedFilesAsync`, `ArchiveProcessedFileAsync`, `AddToFailedQueueAsync`, `TryEnqueueFailedFileAsync`, `TryUpdateFileStatusWithRetryAsync` | 2150-2347 + helpers | ~400 |
| **IcumIngestionVerifier** | `RunIngestionVerificationAsync`, `CountFieldAccuracy` | 2462+ | ~250 |
| **CmrUpgradeService** | `CascadeCMRUpgradeAsync` (single concern, has its own scope) | 1407-1604 | ~200 |

**Net:** 2,513 LOC → 6 files averaging ~375 LOC each. Largest stays under 800.

## Recommended sequence (3-4 sessions)

### Session 1: characterization tests
- Add `IcumJsonIngestionService.Tests` project (xUnit).
- Cover the happy path: feed a synthetic JSON file through
  `ProcessSingleFileAsync` end-to-end against an in-memory or
  testcontainers-Postgres `IcumDownloadsDbContext`.
- Cover the 3 failure modes the audit caught:
  - DB persist fails inside `SaveChangesAsync` (C5)
  - Empty `RawJsonData` in `CountFieldAccuracy` (C10)
  - Outer-loop exception triggers consecutive-failure counter (C6)
- **Stop here, commit, deploy.** If tests pass against current code, baseline
  is locked in.

### Session 2: extract BOEDocumentParser
- Move parse methods to a new file in `Services/IcumApi/Parsing/`.
- Inject `FieldExtractionTracker` and `StreamingJsonParser` into the parser
  rather than passing as constructor args from the service.
- Make parser stateless / thread-safe so the parallel branch can use one
  instance across all files in a cycle.
- Re-run characterization tests.

### Session 3: extract IcumDocumentValidator + IcumFileLifecycleManager
- Two related extractions. Validator depends only on a `BOEDocument` +
  the `_logger`. Lifecycle manager depends on `IIcumDownloadsRepository`
  (already passed in via `ProcessSingleFileAsync`).
- The retry / queue-enqueue helpers added in C5 belong with the lifecycle
  manager.
- Re-run tests.

### Session 4: extract IcumIngestionVerifier + CmrUpgradeService
- Verifier is a reporting concern, no mutation. Easy.
- CMR upgrade service has its own scope-creation pattern; carries its own
  state machine.
- Final tests + commit + deploy.

## Risks per session

| Session | Risk | Mitigation |
|---|---|---|
| 1 (tests) | Test setup against real Postgres flaky in CI | Use Testcontainers OR document local-only test project |
| 2 (parser) | Field-extraction state corruption if parser becomes singleton | Keep parser scoped; verify no static field mutations |
| 3 (validator + lifecycle) | Constructor-explosion in retained shell | Use a single `IIcumIngestionDependencies` aggregate parameter |
| 4 (verifier + CMR) | CMR cascade has implicit ordering dependencies | Document and preserve method-call order in the new service |

## What this DOESN'T fix

- The deferred `H13` ChangeTracker.Clear pattern — split won't help.
- The intentional outer-catch-and-continue (C6) — design choice, not file-size issue.
- The decision to use a custom `ColorCodedLogger` instead of structured `ILogger<T>` — separate concern.
- Performance: this is purely structural. No throughput change expected.

## Decision checkpoint

**Don't start this refactor if:**
- The next 30 days have a hard go-live or feature deadline. Splitting hot
  ingestion code is a regression-risk project.
- No one on the team will own the test infrastructure long-term.
- The existing service is being actively modified by another track of work.

**Do start if:**
- Ingestion is stable in prod (current state per 2026-04-25 SQL verification:
  14 logs/24h, 0 warnings, 0 failed queue — looks healthy).
- You want to start adding new ingestion features (each one currently bloats
  the file further).
- An audit is on the calendar.

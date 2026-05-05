# NSCIM v1 — End-to-End Cold Audit Report (2026-05-05)

**Status:** Phase 3 consolidation complete. Phase 4 triage pending.
**Inputs:** 8 cold-audit reports (`01-topology.md` … `08-observability.md`),
~177 raw findings across 7 deep-dive scopes plus topology.
**Read first:** `00-charter.md` for framing, constraints, severity definitions.

---

## TL;DR

The cargo pipeline is in worse structural shape than the surface symptoms have suggested. The audit surfaces **6 P0 findings**, **~45 P1**, and ~120 P2/P3. Three independent themes dominate:

1. **The 2026-04-25 phase-1 RLS rollout silently broke multiple production-critical paths.** Background services have been query-blind for hours. The application-logs sink has been dead for 9 days. We didn't see any of it because the *next* deploy (and the design decisions before it) broke observability — five of seven services log nowhere durable.
2. **Recent fixes have been landing in dead code.** `AssignmentWorker.cs`, `SubmissionWorker.cs`, `ImageAnalysisBootstrapper.cs` are not registered as hosted services. Today's 2.16.1 orphan-AG `Cancel`-on-expire fix never executed at runtime. The 14 orphan AGs cancelled today were cancelled by a manual probe, not by code.
3. **The 6-layer match-correctness model has only 4–5 layers in code, and the persistence + submission paths around it are silently lossy.** Layer 5 (submission-time port rule) does not exist. ICUMS submission is a simulation with random failure injection. BOE re-arrival silently drops `MasterBlNumber` and 7 other safety-net columns on every UPDATE.

The user-reported symptom ("assignments not showing up for analysts") is a downstream consequence triangulated by **three independent agents from different starting points** — Agent 2 (RLS-blinded background services), Agent 4 (16 Ready AGs all 100% orphan, dead-code Cancel-on-expire), Agent 7 (zero `Active` assignments). The cold audit's framing held: the symptom surfaced naturally rather than driving the inventory.

**This is not a one-week fix-it sprint.** The structural corrections require coordinated work across ingestion, the orchestrator, the schema, and the observability layer. A safe sequencing is proposed in §6.

---

## 1 · P0 inventory (6 findings)

| ID | Scope | One-line | Status today |
|---|---|---|---|
| **2.01** | Intake | `TenantConnectionInterceptor` does not re-fire on Npgsql pooled-connection retries under `EnableRetryOnFailure`. Background services see RLS as fail-closed `'0'` and read 0 rows. | **Active.** CompletenessService logs `0 pending` for 14+ hours while the queue has 1,533 rows. |
| **2.02** | Intake | FS6000 dedup keys on `ContainerNumber` alone in `_existingContainersCache`. Re-scans of the same container are silently dropped. | **Active.** 532 + 612 recent containers have no CCS row. |
| **3.01** | Match-correctness | Layer 5 of the 6-layer model (submission-time port rule) does not exist in code. CHANGELOG and topology have been claiming it for 3 days. | **Active.** `LiveSubmitEnabled=true` overrides `appsettings.json:208`. Real HTTP submissions land on ICUMS without a final port gate. |
| **4.01** | Assignment | `AssignmentWorker.cs` is dead code. Recent commits (incl. 2.16.1 orphan-AG `Cancel`-on-expire) are runtime no-ops. | **Latent.** Today's 14 Cancelled AGs were manual; orchestrator never produces `Cancelled`. |
| **5.01** | ICUMS | `IcumDownloadsRepository.UpdateExistingDocumentAsync` silently drops `MasterBlNumber`, 20 unmapped-field pairs, ingestion-warnings flags from every BOE re-arrival. | **Active.** 0 of 119,669 BOE rows have unmapped-field tracking; only 17% have MasterBlNumber populated. **All 5,721 IsConsolidated=true rows have NULL MasterBlNumber.** |
| **5.02** | ICUMS | Two parallel BOE ingestion paths with divergent field mappings. On-demand path copies `MasterBlNumber → BlNumber`, never sets `MasterBlNumber`, hardcodes `ContainerQuantity=1`. | **Active.** Same DB destination, two writers. Today's 2.16.3 symptom is a downstream manifestation. |

Note: `5.04` (ICUMS submission is a `SimulateICUMSSubmissionAsync` stub with `Random.Shared.NextDouble() < 0.1` failure injection) is technically dormant (queue empty), but ranks as a *latent P0* — the moment something writes to `icumssubmissionqueues` it lands fake-success rows in production. **Treat as P0 for sprint planning.**

---

## 2 · P1 inventory (highlights, not exhaustive)

The full P1 set lives across the 7 per-agent reports. The cross-cutting ones that need group-thinking:

### RLS / tenancy aftermath of the 2026-04-25 deploy

| ID | Issue |
|---|---|
| **8.01** | `applicationlogs` PG sink dead for 9 days. RLS rejects every Serilog insert because the sink uses raw Npgsql + never sets `app.tenant_id`. |
| **8.02** | `LogManagementController` raw-Npgsql reads return 0 rows for the same reason. Admin "View Logs" UI silently empty. |
| **8.03** | `ErrorMonitoringBackgroundService` blind for 9 days. Detects 0 errors per cycle. |
| **7.02** | `analysisqueueentries` has no `tenant_id` column at all (entity class lacks the property; table created post the manual phase-1 RLS rollout). |
| **7.03** | `splitter_consensus_corpus` same — 24 rows of training data, no RLS, no tenant_id. |

**Pattern:** every code path that opens a non-EF connection (raw Npgsql, Serilog sinks, third-party background services) has been silently failing. The EF interceptor is not the only place tenancy needs to be set.

### Dead code with critical logic

| ID | Issue |
|---|---|
| **4.01** | `AssignmentWorker.cs` — orphan-AG Cancel transition + only `UpsertQueueEntryAsync` callsite (P0). |
| **4.07** | `SubmissionWorker.cs` `[Obsolete]`, `ImageAnalysisBootstrapper.cs` unmarked but unhosted. All three duplicate orchestrator logic verbatim. |
| **5.12** | `IcumDataTransferService.cs` not registered. Orchestrator inlines its logic. |
| **6.05** | `SignalRService.cs` registered Singleton in DI but targets nonexistent `/hubs/scanner`, no auth. Footgun. |

### Match-correctness pipeline integrity

| ID | Issue |
|---|---|
| **3.02** | CCS Step 2 port-mismatch branch writes no MQF (asymmetric vs Step 1). 24 silent re-blocks in last 7 days. |
| **3.03** | CCS Step 2 has no fyco rule at all. Stale `hasICUMSData=true` after BOE clearancetype changes. |
| **3.04** | Mapper hashes Guid `scan.Id → int32` for `CBR.ScannerDataId`. Birthday collisions. 1,567 negative hashes in CBR. 18 containers with multiple active CBRs (MRKU2369877 has 3). |
| **3.05** | `CascadeCMRUpgradeAsync` not called on the `cmrUpgraded=true` in-place path. 5 Export-Hold containers stuck (`MEDU7718311` + siblings). 1,706 BOE rows with `OriginalClearanceType='CMR'` at risk. |
| **3.06** | Three different `OrderBy` choices (`CreatedAt` × 2, `Id desc` × 1) for "pick canonical BOE per container" across mapper/Step1/Step2. Race-window inconsistency. |
| **3.07** | Implicit CMR→IM upgrade is regime-blind first-char switch. Will mis-classify regime 27 (export) if it arrives. |
| **3.08** | `FycoClassifier.Classify` and `IsExportFlag` use different parsers. Two sources of truth for the same data. |

### Assignment / queue lifecycle

| ID | Issue |
|---|---|
| **4.02** | Live `AutoAssignByRoleAsync` creates assignments without calling `UpsertQueueEntryAsync`. Queue entries materialize only via `ReconcileQueueAsync` (1–2 min lag). Operator opens workbench during gap → assignment exists, isn't visible. |
| **4.03** | Live `ReclaimExpiredAssignmentsAsync` doesn't transition orphan AGs to `Cancelled` (the same fix lives in dead code at 4.01). Lease-cycle churn never converges. |
| **4.04** | `AnalysisStatusValidator` missing `AgentProcessing`, `Cancelled`, `Archived`. Unknown-from fallback returns `true`. `Cancelled` AGs can transition out arbitrarily. |
| **4.06** | `DecisionSideEffectsService` instantiated with null `_queueService` in 3 of 4 callsites. Decision-save → queue-purge silently no-ops. |

### ICUMS data integrity

| ID | Issue |
|---|---|
| **5.03** | Order-flip fix from `f4ec289` was applied to retry path only. Primary submission path still does DB UPDATE then File.Move. |
| **5.05** | `analysissubmissions.status` set to `TestSaved` at insert and never updated. 750 rows stuck. Last newest 2026-04-21. |
| **5.06** | `ManualBOERequest` lifecycle dead since 2026-03-25. **1,747 rows stuck `Queued` for 47+ days.** Operator escape hatch silently broken. |
| **5.07** | `ICUMSDownloadQueue` (prod-side) frozen since 2026-03-20. 55 Pending, zero retries. |
| **5.08** | All 289 `ingestionlogs` rows in `Started` status, NULL `endtime`. Try/catch swallows the UPDATE failure. |
| **5.09** | Mass IsConsolidated mis-tagging — every consolidated row has NULL MasterBlNumber (consequence of 5.01). |

### Schema integrity

| ID | Issue |
|---|---|
| **7.01** | Zero in-DB FK constraints on cargo-pipeline core (6 tables). Schema held together by app-side discipline. |
| **7.04** | 24 active CCS rows have `hasicumsdata=true` + NULL `boedocumentid` + NULL `clearancetype`. Export-Hold workflow stage 13/13 damaged. |
| **7.05** | 18 containers with multiple `isactive=true` CBRs (37 excess rows). Triple-write bug from 2026-03 ingestion. |
| **7.06** | 392 `imageanalysisdecisions` reference deleted CCS rows. Last orphan 2026-04-21. |
| **7.07** | 30 CCS rows `hasicumsdata=true` with no active CBR. |
| **7.08** | 481 active CBRs with no matching CCS. |
| **7.09** | 7 Ready AGs with zero assignment row at all. |
| **7.10** | 11,437 `analysisassignments`, **zero in `Active` state**. Lease-cycle expires faster than analysts can claim. |
| **7.20** | NickHR.WebApp uses `postgres` superuser + hardcoded JWT key. RLS bypass automatic. |

### Frontend contract violations

| ID | Issue |
|---|---|
| **6.01** | `IsConsolidated => GroupIdentifier IS container number` assumption hardcoded in 5 places (`ImageAnalysisViewDialog.razor` + `Workbench.razor`). 2.16.3 patched the upstream queue entry; the contract assumption is unchanged. Any future mistagging recreates the symptom. |
| **6.02** | `ScannerDataTab` silently shows "no data" for both "container not found" and "container has no scans yet." Indistinguishable failure modes. |
| **6.03** | `GET /api/containerdetails/icums/{cn}` returns 200 + empty PagedResult for "no BOE" — same surface as "container unknown." |
| **6.04** | **`ConsolidatedCargoQueries.GetContainersByDeclarationAsync:317` still has the stale `!b.IsConsolidated` filter.** I missed this site in `4c4931c`. One-line fix. |
| **6.05** | `SignalRService` dead but DI-registered. |

### Observability collapse

| ID | Issue |
|---|---|
| **8.04** | HealthChecks.UI throws `FileNotFoundException: IdentityModel 5.2.0.0` once per minute. 1,326 errors/day. `/health-ui` page broken. |
| **8.05** | `ContainerDataMapperService` logs **1,828 PORT MISMATCH ERR rows in 8 hours** for designed-to-fire match-rule rejections. Real exceptions buried. |
| **8.06** | NSCIM_WebApp / Portal / NickHR.WebApp / NickFinance.WebApp / NickComms.Gateway have no file sink. Five services log to /dev/null. |
| **8.31** | NickHR.API Serilog uses *relative path*; LocalSystem WD = `C:\Windows\System32`, so logs land in `C:\Windows\System32\logs\`. Files stop after 2026-04-24. |

---

## 3 · Themes (the picture behind the findings)

### Theme A — *The 2026-04-25 RLS rollout was a half-finished migration.*

The phase-1 multi-tenancy work added `tenant_id` columns and `FORCE ROW LEVEL SECURITY` policies to ~180 tables, with a fail-closed default of `'0'`. The accompanying `TenantConnectionInterceptor` was supposed to set `app.tenant_id` on every connection open. But:

- The interceptor doesn't re-fire on Npgsql pooled-connection retries under `EnableRetryOnFailure` (`2.01`).
- The interceptor doesn't apply to non-EF code paths: Serilog PG sink (`8.01`), `LogManagementController`'s raw NpgsqlConnection (`8.02`), `ErrorMonitoringBackgroundService` raw queries (`8.03`).
- Tables created *after* the rollout were never tenanted: `analysisqueueentries` (`7.02`), `splitter_consensus_corpus` (`7.03`).
- One service connects as `postgres` superuser, bypassing RLS entirely (NickHR.WebApp, `7.20`).

The combined effect: background services and observability surfaces have been silently failing — and the dead observability surfaces *prevented us from seeing the failures*. We've been operating on faith for 9–14 days.

### Theme B — *Multiple recent commits landed in dead code.*

`AssignmentWorker.cs`, `SubmissionWorker.cs`, `ImageAnalysisBootstrapper.cs`, `IcumDataTransferService.cs` are not hosted services. Logic was lifted into `ImageAnalysisOrchestratorService` and `IcumPipelineOrchestratorService` but the originals weren't deleted — they continue to compile, ship, and accumulate "fixes." Today's 2.16.1 orphan-AG `Cancel` transition lives only in `AssignmentWorker.ReclaimExpiredAssignmentsAsync` and `ExpireAssignmentsInBatchesAsync` — both unreachable. The 14 Cancelled AGs in production today were Cancelled by my manual `OrphanAgSweep.cs` probe, not by running code.

This is the second time this audit cycle that a "fix" has been a runtime no-op. **Build-and-deploy verification is not enough; we need a runtime check that the code path was actually executed.**

### Theme C — *The 6-layer match-correctness model is more design than implementation.*

CHANGELOG 2.16.0 documents 6 layers of match-correctness gating. The cold reading found:

- **Layer 5 (submission-time port rule) does not exist** (`3.01`). The `ValidatePortMatchAsync` and `ValidateFycoImportExportAsync` methods exist but are only wired into `GatewayOrchestrationService`'s read-only aggregator endpoint — not the real submission path. Live HTTP submissions (`LiveSubmitEnabled=true`) land without the final gate.
- **CCS Step 2 (re-check) is materially weaker than Step 1**: no fyco rule (`3.03`), no MQF on port mismatch (`3.02`).
- **Mapper-time CMR cascade has a missing call hole** on the in-place upgrade path (`3.05`). 5 Export-Hold containers stuck with `hasicumsdata=true, boedocumentid=null, clearancetype=null` are the smoking gun.
- **The mapper's "already mapped" check uses Guid.GetHashCode() → int32** (`3.04`). Birthday collisions guaranteed at scale; 18 containers carry duplicate active CBRs today.
- **354 BOEs with NULL DeliveryPlace defeat Layer 1 entirely** (`7.12`). The cardinal port rule needs DP to discriminate.

Net: the match-correctness arc has been working *for the cases its code can see*, but the population it can't see (orphan/missing/duplicate denorm) is wider than CHANGELOG implies.

### Theme D — *The 6-layer model is also lossy at the edges* (data ingestion + persistence).

- **BOE re-arrival drops 8 columns silently** (`5.01`) — including the MasterBlNumber that today's 2.16.3 is built around. The 5,721 IsConsolidated=true rows with NULL MasterBlNumber are a direct consequence; my 2.16.3 fix is a guard at the wrong layer.
- **Two ingestion paths with divergent mappings** (`5.02`). Same DB destination, different writers, different column choices.
- **Submission is fake** (`5.04`). `ICUMSSubmissionService` runs a 1–3s delay then 90/10 success/fail simulation. Currently dormant. Ready to land fake-success rows the moment something writes to its queue.
- **Order-flip fix from `f4ec289` only landed on the retry path** (`5.03`). Primary submission still does DB UPDATE then File.Move.
- **Manual escape hatches both broken**: `ManualBOERequest` (1,747 stuck `Queued` for 47+ days, `5.06`) and `ICUMSDownloadQueue` (55 stuck `Pending` since 2026-03-20, `5.07`).

### Theme E — *The frontend has the same-shape contract bug in many places.*

Today's MasterBlNumber bug taught us that the dialog's `IsConsolidated => GroupIdentifier IS container number` assumption is fragile. The audit finds that assumption hardcoded in **5 places** (`6.01`), with parallel bugs in adjacent surfaces:
- `ScannerDataTab` shows the same UI for "container not found" and "no scans yet" (`6.02`).
- `GET /api/containerdetails/icums/{cn}` returns 200+empty for "no BOE" — indistinguishable from "container unknown" (`6.03`).
- `ConsolidatedCargoQueries.GetContainersByDeclarationAsync:317` still has the stale filter `4c4931c` was supposed to fix everywhere (`6.04`). **This is a one-line fix I missed.**

The root cause: **disambiguation is happening client-side**. The dialog tries to figure out from `(GroupIdentifier, IsConsolidated)` which API shape to call. One server endpoint that takes the whole tuple and dispatches server-side would delete most of the bugs.

### Theme F — *Schema discipline is held by app code, not the database.*

- **Zero in-DB FK constraints** on the 6 core cargo tables (`7.01`). Today's cross-DB orphan count is *zero* — but only because every read path is written as a 2-pass C# join.
- **Triple-write bug** in 2026-03 created 18 containers with multiple active CBRs (`7.05`). A partial unique index would have prevented it and would prevent recurrence.
- **Bidirectional drift**: 30 CCS-without-active-CBR + 481 active-CBR-without-CCS (`7.07`/`7.08`).
- **State machines have unknown values**: 28 AGs with NULL scannertype, AnalysisStatusValidator missing 3 statuses (`4.04`/`7.22`).

### Theme G — *Observability of all this is broken.*

We could not have caught any of the above by looking at production. Five of seven services log nowhere durable (`8.06`). The PG-backed log table has been dead for 9 days (`8.01`). The admin Log UI returns 0 rows (`8.02`). The error-monitoring service has been blind (`8.03`). Background-worker logs lack CorrelationId (`8.10`). The dashboard reports hardcoded values for throughput/latency/system-load (`8.08`/`8.09`/`8.27`). There is no alerting (`8.25`).

The errors log is **99% noise**: 1,828 designed-to-fire match-rule rejections at ERROR level + 459/day HealthChecks UI exceptions = real bugs are invisible. After fixing the four XS observability quick-wins, the errors log becomes 99% real signal again.

---

## 4 · Dependency graph

Some fixes unblock others. The audit suggests this ordering:

```
   ┌────────────────────────────────────────────────────────────────────┐
   │ Group A — RLS / observability quick wins (XS each, must go first) │
   │   8.01  applicationlogs sink — set app.tenant_id on PG connection │
   │   8.02  LogManagementController — SET LOCAL app.tenant_id          │
   │   8.03  ErrorMonitoring — SET LOCAL app.tenant_id                  │
   │   8.04  Pin IdentityModel for HealthChecks UI                      │
   │   8.05  Demote PORT MISMATCH from Error to Warning                 │
   │   8.31  NickHR.API absolute log path                               │
   │ → after this we can SEE production again                           │
   └────────────────────────────────────────────────────────────────────┘
                                       │
                                       ▼
   ┌────────────────────────────────────────────────────────────────────┐
   │ Group B — RLS / tenancy structural fix (M, blocks production fix) │
   │   2.01  TenantConnectionInterceptor pooled-conn fix                │
   │ → background services see rows again; CCS pipeline resumes         │
   └────────────────────────────────────────────────────────────────────┘
                                       │
                                       ▼
   ┌────────────────────────────────────────────────────────────────────┐
   │ Group C — dead code excision + orphan-AG fix relocation (S+M)     │
   │   4.01  Delete AssignmentWorker, SubmissionWorker, Bootstrapper   │
   │   4.03  Move orphan-AG Cancel-on-expire into orchestrator          │
   │   4.02  Add UpsertQueueEntryAsync to AutoAssignByRoleAsync         │
   │   5.04  Delete ICUMSSubmissionService simulation OR replace        │
   │   5.12  Delete IcumDataTransferService                             │
   │   6.05  Delete SignalRService                                      │
   │   4.04  Add missing statuses to AnalysisStatusValidator            │
   │ → assignment loop converges; queue is real-time; dead code gone   │
   └────────────────────────────────────────────────────────────────────┘
                                       │
                                       ▼
   ┌────────────────────────────────────────────────────────────────────┐
   │ Group D — ICUMS persistence fix + backfill (M)                    │
   │   5.01  Add 8 dropped columns to UpdateExistingDocumentAsync UPDATE│
   │   5.02  Unify the two BOE ingestion paths                          │
   │   Backfill MasterBlNumber for 5,721 IsConsolidated rows           │
   │   5.03  Order-flip fix on primary submission path                  │
   │ → 2.16.3's MasterBlNumber guard becomes belt-and-braces, not load- │
   │   bearing. Future ingest doesn't recreate the symptom.            │
   └────────────────────────────────────────────────────────────────────┘
                                       │
                                       ▼
   ┌────────────────────────────────────────────────────────────────────┐
   │ Group E — match-correctness gap closure (S each)                  │
   │   3.01  Add Layer 5 — wire ValidatePortMatchAsync into Submit      │
   │   3.02/3.03 CCS Step 2 — flag-write + fyco gate                    │
   │   3.04  ScannerDataId Guid → int hash collision (L; defer or fix)  │
   │   3.05  CascadeCMRUpgradeAsync on upgrade-in-place path            │
   │   3.06  Standardize BOE selection ordering across 4 sites          │
   │   3.07  RegimeDirectionMap.IsExport for upgrade switch             │
   │   3.08  Unify FycoClassifier + IsExportFlag                        │
   └────────────────────────────────────────────────────────────────────┘
                                       │
                                       ▼
   ┌────────────────────────────────────────────────────────────────────┐
   │ Group F — frontend contract de-fragmentation (M)                  │
   │   6.04  Drop !IsConsolidated from declaration query (XS, urgent)   │
   │   6.01  Refactor dialog to single (groupId, isConsolidated) endpoint│
   │   6.02/6.03 Distinguish error states at API contract               │
   │   6.07  Replace reflection-based token retrieval with interface    │
   │   6.13  Implement AuthenticationCircuitHandler                     │
   └────────────────────────────────────────────────────────────────────┘
                                       │
                                       ▼
   ┌────────────────────────────────────────────────────────────────────┐
   │ Group G — schema integrity + monitoring uplift (M+L)              │
   │   7.05  Cleanup duplicate active CBRs + add partial unique index  │
   │   7.04/7.07/7.08 Backfill CCS denorm + drift sweep                 │
   │   7.01  Add in-DB FKs (after orphan cleanup 7.06–7.09)             │
   │   7.02  Tenant-id + RLS on analysisqueueentries                    │
   │   8.10  CorrelationId for background workers                       │
   │   8.13  Per-iteration WorkerHeartbeatLog                           │
   │   8.25  Wire alerts to NickComms email/SMS                         │
   └────────────────────────────────────────────────────────────────────┘
```

Group A is "fix the dark." Group B is "fix the data hiding." Group C is "fix the workflow." Groups D–G are the structural cleanup that prevents the next regression.

---

## 5 · Cross-check: does the audit explain known symptoms?

The audit was framed cold — agents were told NOT to target user-reported symptoms. Validation step (per charter):

| Reported symptom | Independently surfaced by | Verdict |
|---|---|---|
| "Assignments not showing for image analysts" (today) | `2.01` (RLS-blind background services) + `4.01` (orphan-AG fix in dead code) + `4.02` (queue-entry materialization gap) + `7.10` (zero Active assignments) | **Triangulated.** Four agents from four scopes converged on the same picture. |
| "Empty Scanner / ICUMS / Image tabs for `41225848361`" (yesterday) | `5.01` (BOE drops MasterBlNumber on UPDATE) + `5.09` (mass IsConsolidated mis-tagging) + `6.01` (dialog routing assumption) + `7.04` (CCS denorm drift) | **Triangulated and reframed.** 2.16.3's fix is at the wrong layer; root cause is `5.01`. |
| "70326214329 still in queue" (yesterday) | `4.01` + `4.03` (Cancel-on-expire in dead code) + `2.01` (orchestrator can't see the AGs anyway) | **Confirmed.** Manual sweep was the actual fix; running code didn't do it. |
| "Records not getting assigned" (3 days ago) | All of the above, plus `7.09` (7 Ready AGs with no AA at all) | **Layered consequence.** Multi-cause. |

Cold-audit framing held. **No agent surfaced a new mystery symptom we don't already know about.** That's a good sign — the production state is broken in known ways, not in unknown ways.

---

## 6 · Proposed fix-it sprint sequencing

**Recommended:** ~3-week sprint with daily check-ins, in this order:

### Day 1 (today/tomorrow): Group A — Restore observability

All XS-effort, low-risk. After these we can see what we're doing.

1. `8.01` — Configure Serilog PG sink to set `app.tenant_id=1` on connection (Npgsql `Options="-c app.tenant_id=1"`). 1-line config change.
2. `8.02` + `8.03` — Add `SET LOCAL app.tenant_id = '1'` to `LogManagementController.cs:46-83` and `ErrorMonitoringBackgroundService.cs:134-159`. 2-line additions each.
3. `8.04` — Pin `IdentityModel` to a .NET 10-compatible version in NSCIM_API.csproj.
4. `8.05` — Demote `PORT MISMATCH (mapper, cardinal)` from `LogError` to `LogWarning` in `ContainerDataMapperService.cs:213-234`.
5. `8.31` — Change NickHR.API Serilog file path from relative `Logs/nickhr-.log` to absolute `C:\Shared\NSCIM_PRODUCTION\Data\Logs\nickhr-.log`.
6. `6.04` — Drop `!b.IsConsolidated` from `ConsolidatedCargoQueries.GetContainersByDeclarationAsync:317`.

**Validation:** in-app log viewer returns rows; errors log goes from 99% noise to >50% real signal; HealthChecks UI loads.

### Day 2–3: Group B — RLS structural fix (2.01)

The deepest, riskiest single change in the whole sprint. **Recommended approach:**

- Add `Filter.ByExcluding(...)` to the EF retry policy so the interceptor's `ConnectionOpenedAsync` is *guaranteed* to run before the first command (alternative: switch from interceptor to a `MigrationsAssembly`/`AddDbContext` configuration that issues `SET app.tenant_id` as the first command of every command-tree).
- Validate by running a controlled probe: connect, run 5 queries on the same connection, recycle pool, run 5 more. Verify `app.tenant_id` survives every checkout.
- After fix lands, the CCS service will start draining the 1,533 stuck queue items. Monitor closely.

**Validation:** CompletenessService log reports `N pending` matching DB cardinality; queue drains.

### Day 4–6: Group C — Dead code + orphan-AG fix relocation (4.01–4.07, 5.04, 5.12, 6.05)

In one PR if possible:

1. Delete `AssignmentWorker.cs`, `SubmissionWorker.cs`, `ImageAnalysisBootstrapper.cs`, `IcumDataTransferService.cs`, `SignalRService.cs`. Verify no external references.
2. Port the orphan-AG `Cancel`-on-expire from dead code into `ImageAnalysisOrchestratorService.ReclaimExpiredAssignmentsAsync` and the validation cleanup path.
3. Add `UpsertQueueEntryAsync(db, assignment.Id, ct)` after the assignment commit in `AutoAssignByRoleAsync`.
4. Add `AgentProcessing`, `Cancelled`, `Archived` to `AnalysisStatusValidator.ValidTransitions`. Change unknown-from fallback to `return false`.
5. Decide on `5.04` (ICUMS submission simulation): delete the service entirely (orchestrator's Outbox is the real path), OR replace `SimulateICUMSSubmissionAsync` with the real HTTP call. **Strong recommend: delete.**

**Validation:** `OrphanAgSweep.cs` finds 0 candidates and stays at 0 across multiple lease cycles. Queue entries appear immediately on assignment, not after reconcile.

### Day 7–10: Group D — ICUMS persistence (5.01–5.03)

1. Add the 8 dropped columns to the `UPDATE boedocuments` SQL in `IcumDownloadsRepository.UpdateExistingDocumentAsync:256-281`. Sanity-check via INSERT vs UPDATE divergence in EF.
2. Unify the two ingestion paths (`5.02`) — preferably refactor both to call a single `BoeJsonToBOEDocumentMapper` helper.
3. Apply order-flip fix to primary submission path in `ImageAnalysisOrchestratorService.cs:2212-2222`.
4. Run a backfill script: for every row with `IsConsolidated=true` and `MasterBlNumber IS NULL`, parse `RawJsonData` for the master BL and update.

**Validation:** New BOE arrivals carry MasterBlNumber. Backfill returns 5,721 rows updated. 2.16.3's `ReadyGroupsCacheService` guard becomes belt-and-braces.

### Day 11–14: Group E — Match-correctness gap closure (3.01–3.08)

1. **`3.01` is the highest-stakes**: wire `ValidatePortMatchAsync` + `ValidateFycoImportExportAsync` into `SubmitPayloadsToIcumsAsync` before HTTP POST. Each container payload re-validated.
2. Hoist Step 1's fyco rule into a private helper, call from Step 1 + Step 2 + mapper (`3.03`).
3. Add `WriteMatchQualityFlagAsync` calls to Step 2 port + null-DP branches (`3.02`).
4. Replace mapper's `Guid.GetHashCode()` with proper string-based or `(scannertype, scannerid)` composite (`3.04`). **L-effort and risky** — can defer to a separate sprint, but the duplicate active CBR cleanup (7.05) needs to happen first.
5. Move `CascadeCMRUpgradeAsync` call into the `cmrUpgraded=true` path before `continue` (`3.05`). XS fix.
6. Standardize BOE selection ordering — extract `GetCanonicalBOEAsync` helper.
7. Replace regime-blind first-char switch with `RegimeDirectionMap.IsExport()` (`3.07`).
8. Unify `FycoClassifier` and `IsExportFlag` (`3.08`).

**Validation:** Pre-submission audit log shows port re-validation firing. The 5 stuck Export-Hold containers (`MEDU7718311` + siblings) re-cascade. No new orphan-shape CCS rows after deploy.

### Day 15–18: Group F — Frontend contract simplification (6.01–6.13)

1. Build a single server endpoint `GET /api/cargogroup/{groupIdentifier}` that returns the full tuple `{ContainerNumbers[], BoeDocuments[], IsConsolidated, MasterBlNumber}` regardless of consolidation. Migrate the dialog to call this once, dispatch tabs from the result. Eliminates 5 same-shape contract bugs.
2. Distinguish "container not found" (404) from "no data yet" (200+empty marker) at `ContainerDetailsController.GetICUMSData` / `GetScannerData`. Update WebApp to render the distinction.
3. Replace reflection-based token retrieval with `IAuthTokenSource` interface.
4. Implement `AuthenticationCircuitHandler` with `OnConnectionUpAsync` token validation.

**Validation:** Operator gets a clear "container number unknown" message vs a blank tab.

### Day 19–21: Group G — Schema integrity + monitoring uplift

1. Cleanup orphans (`7.06`–`7.09`).
2. Add partial unique index on `containerboerelations(containernumber) WHERE isactive=true` (`7.05`).
3. Add `tenant_id` column + RLS to `analysisqueueentries` (`7.02`).
4. Add the 5 missing in-DB FKs on the cargo-pipeline core (`7.01`).
5. Add per-cycle CorrelationId enricher for background workers (`8.10`).
6. Add `WorkerHeartbeatLog` extension method, wire into all 35 BackgroundServices (`8.13`).
7. Wire alerts to NickComms email/SMS (`8.25`). Persist alerts to a `dashboardalerts` table.
8. Compute dashboard's hardcoded throughput/latency fields from real data (`8.08`/`8.09`/`8.27`).

**Validation:** Every BackgroundService's iteration appears in logs as a structured event. A simulated production incident reaches an on-call address. Schema constraint violations become DB errors instead of silent app-side bugs.

### Defer to a follow-on sprint

- `3.04` (Guid hash collision FK) — entity schema change, EF migration, backfill: M effort, not urgent now that 7.05 dedup is in place.
- `5.06` / `5.07` (manual escape hatches dead): operationally relevant but not blocking.
- `6.09` (CSP `unsafe-eval` regression): Phase 2 security work.
- All P3 noise.

---

## 7 · Open questions for triage (Phase 4)

These are the ambiguities the audit couldn't resolve without operator/owner input:

1. **`5.04` ICUMS submission stub** — Has anyone been relying on the simulation's behavior for testing? Or is it pure dead code? **Strong recommend: delete.** Confirm with operator first.
2. **`5.06` ManualBOERequest broken since 2026-03-25** — was this an operational decision (we stopped using manual requests) or a silent regression? If silent, the 1,747 queued rows need a triage pass.
3. **`7.20` NickHR.WebApp postgres superuser** — known migration debt or oversight?
4. **`6.16` AuditDecisionDialog reachable?** — likely dead, deprecate.
5. **`6.25` /api/imageproxy** — auth status unknown; verify SSRF protection.
6. **`8.18` EF logging set to Fatal** — operator policy or accidental? Restoring to Warning will surface DB issues.
7. **Which IcumContainerData consumers actually need it?** (`5.11`) — if only reporting/stats, replace with a view of BOEDocuments.
8. **The 4,246 cross-scanner containers (FS6000+ASE)** — semantic meaning? (Discussion needed before fixing `3.15`.)

---

## 8 · What the audit didn't cover

For Phase 4 awareness:

- **NickHR / NickFinance / NickComms internals** were out of scope per charter. Boundary calls into the cargo pipeline were surfaced (e.g., NickComms email integration with `8.25`).
- **Performance / capacity planning** beyond index suggestions — not deeply audited.
- **Security audit beyond what surfaced incidentally** (`6.09` CSP, `7.20` NickHR superuser, `5.04` simulation, `6.25` proxy) — not exhaustive.
- **v2 ERP** — explicitly out of scope.
- **Production data quality drift over time** — point-in-time snapshot only.

---

## 9 · Pointers

- `00-charter.md` — audit framing, constraints, severity definitions, agent registry
- `01-topology.md` — service / DB / queue / hosted-services map (5,697 words; reference doc)
- `02-intake.md` — Scanner intake → CCS (27 findings)
- `03-match-correctness.md` — Match-correctness pipeline (22 findings)
- `04-assignment.md` — Assignment pipeline (16 findings)
- `05-icums.md` — ICUMS ingestion + queues + submission (27 findings)
- `06-frontend.md` — Frontend operations (25 findings)
- `07-db-integrity.md` — DB schema + RLS + integrity (29 findings)
- `08-observability.md` — Observability + logging (31 findings)
- Probe scripts under `C:\temp\nscim-probe\` (read-only by default; some have backfill / cleanup paths gated by sanity-check rollback)

**Phase 4 starts when the operator has read this report and is ready to triage.**

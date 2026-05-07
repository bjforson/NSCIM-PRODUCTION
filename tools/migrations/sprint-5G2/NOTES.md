# Sprint 5G2 / Bridge B1 — integration notes

This sprint folder hosts the **v1 hardening bridge** to v2 — see plan
`C:\Users\Administrator\.claude\plans\i-need-an-analysis-abundant-pnueli.md` §B1.

## What's in this scaffold

Three additive artifacts that don't change any existing code path:

1. **`01-create-analysis-group-status-transitions.sql`** — new audit table. Apply via `tools/migration-runner` (psql isn't installed on the prod box; runner uses `NICKHR_DB_PASSWORD` superuser per memory `reference_tools_migration_runner.md`).

2. **`src/NickScanCentralImagingPortal.Core/Entities/Analysis/AnalysisGroupStatusTransition.cs`** — EF entity for the new table.

3. **`src/NickScanCentralImagingPortal.Infrastructure/Data/AnalysisGroupStateMachine.cs`** — the sole-writer facade. Wraps the existing `AnalysisStatusValidator` (in `Core/Helpers/`) with mandatory enforcement + audit-row write. Lives in `Infrastructure` (not `Core`) because it depends on EF Core; `Core.csproj` is EF-free per v1 layering. Additive: as of Sprint 5G2 §B1 only the `ZombieAnalysisGroupSweeperService` pilot calls it; the other 36 sites refactor in subsequent change sets.

## Deferred — needs human review before deploy

The plan calls for **37 call sites** of `g.Status = ...` to be refactored to go through `AnalysisGroupStateMachine.TransitionAsync`. This file lists where each one lives but the refactors are not in this scaffold. Each site needs its own actor + reason context resolved (some live in transactions where validity is already guaranteed, some don't), so the refactor is per-site rather than a global rewrite.

**Step 1 — register the entity.** Add to `ApplicationDbContext`:

```csharp
public DbSet<AnalysisGroupStatusTransition> AnalysisGroupStatusTransitions
    => Set<AnalysisGroupStatusTransition>();
```

No `OnModelCreating` config needed — the `[Table]` + `[Column]` attributes on the entity carry everything. (If your DbContext disables convention-based lowercasing, add explicit `e.Property(...).HasColumnName(...)` calls.)

**Step 2 — apply the SQL migration** via the migration runner:

```powershell
cd C:\Shared\NSCIM_PRODUCTION\tools\migration-runner
dotnet run -- --connection "$env:NICKHR_DB_PASSWORD-shaped-string" `
    --file ..\migrations\sprint-5G2\01-create-analysis-group-status-transitions.sql
```

Verify: `SELECT count(*) FROM analysis_group_status_transitions;` → 0.

**Step 3 — refactor the 37 call sites.** From the plan's inventory:

```
AuditReviewController.cs:1189, 1199                     (2)
ImageAnalysisController.cs:647, 1568, 1631              (3)
ImageAnalysisDecisionController.cs:143, 305, 464, 630, 1620, 1750, 1826  (7)
ImageAnalysisManagementController.cs:279, 893, 1032, 1394   (4)
DecisionAgentWorker.cs:74, 266, 275, 331                (4)
DecisionSideEffectsService.cs:107                       (1)
ImageAnalysisOrchestratorService.cs:1541, 1661, 2678, 2721, 3519  (5)
```

Plus the 3 sites already calling `AnalysisStatusValidator.IsValidTransition()` advisorily — promote those to `TransitionAsync`.

For each site:
- Replace `g.Status = "X"; await db.SaveChangesAsync();` with `await AnalysisGroupStateMachine.TransitionAsync(db, g, "X", triggerName, actor, reason, correlationId, ct);`
- The trigger name should describe the cause (`"AnalystSubmittedFindings"`, `"DecisionAgentAutoApproved"`, `"ZombieAnalysisGroupSweep"`).
- Actor: user id for human flows, service name for background workers.
- Reason: free text; for system actors a short tag like `"lease-expired"` is fine.

**Step 4 — make `AnalysisGroup.Status` setter `internal`** (compile-time enforcement that nothing outside the `Core` assembly can write the field):

```csharp
public string Status { get; internal set; } = "Ready";
```

The facade lives in `Core` so it can write; controllers + workers in other assemblies will fail compile if they try `g.Status = "X"` directly. **This is the gate that prevents the regression returning.**

**Step 5 — track tenancy.** The facade currently stamps `TenantId = 1` on every audit row (matching the DB DEFAULT for legacy AnalysisGroup rows that don't yet implement `ITenantOwned`). When the entity-side ITenantOwned adoption ships (per memory `reference_rls_now_enforces.md`), update `ResolveTenantId` to return `group.TenantId` instead.

## Verification

After §3 ships and a 24h soak:

```sql
-- Every transition observed in production should appear once per record movement.
SELECT from_status, to_status, COUNT(*)
FROM analysis_group_status_transitions
GROUP BY from_status, to_status
ORDER BY 3 DESC;

-- No transition should have null actor or reason.
SELECT COUNT(*) FROM analysis_group_status_transitions
WHERE actor IS NULL OR actor = '' OR reason IS NULL OR reason = '';
-- Expected: 0

-- Watch logs for InvalidOperationException from ValidateTransition —
-- each one is a previously silent illegal transition now surfaced.
Get-Content C:\Shared\NSCIM_PRODUCTION\logs\nickscan-errors-*.txt |
    Select-String "Invalid status transition"
```

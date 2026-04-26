# 2-stage payroll approval — design proposal

**Status:** PROPOSAL — needs sign-off before code  
**Author:** drafted 2026-04-26  
**Supersedes:** the current 1-stage `LockPayrollAsync` flow

## Why

Today a single user can both **process** a payroll run and **lock** (approve) it.
That's a segregation-of-duties (SoD) gap — a payroll preparer can pay
themselves an extra month and self-approve. Standard payroll controls require
the preparer and the approver to be different people, with the approver
typically a Finance Director or HR Manager.

## Today's flow

```
PayrollStatus enum: Draft → Processing → Completed → Locked / Reversed
                                                       ↑
                                                 one user clicks
                                                 POST /runs/{id}/lock
                                                 → ApprovedBy = that user
                                                 → ApprovedAt = now
                                                 → updates loan balances
```

`PayrollProcessingService.LockPayrollAsync(id, approvedBy)` doesn't compare
`approvedBy` against the user who called `RunPayrollAsync(...)`. Same person,
no objection.

## Proposed flow

```
Draft → Processing → Completed → PendingApproval → Locked
                                       │
                                       └──→ Rejected → (preparer amends)
                                                         → resubmit → PendingApproval
```

## Schema changes — `PayrollRun`

Add nullable columns. Existing `Locked` runs stay unaffected.

| Column | Type | Purpose |
|---|---|---|
| `SubmittedBy` | `string?(200)` | Preparer who clicked "Submit for approval" |
| `SubmittedAt` | `DateTime?` | When they submitted |
| `RejectedBy` | `string?(200)` | Approver who rejected (if any) |
| `RejectedAt` | `DateTime?` | When |
| `RejectionReason` | `string?(500)` | Why; surfaced back to preparer |

`ApprovedBy` / `ApprovedAt` keep their meaning — set on the
`PendingApproval → Locked` transition.

## `PayrollStatus` enum additions

```csharp
public enum PayrollStatus
{
    Draft,
    Processing,
    Completed,
    PendingApproval,   // NEW — preparer submitted, waiting on approver
    Rejected,          // NEW — approver rejected; preparer can amend & resubmit
    Locked,
    Reversed
}
```

Order matters for any code that does numeric comparisons (none today, but
guard against it). New values added at end is the safer migration; doc says
otherwise but the code currently uses pattern matches on the enum, so end-
appending is safe and avoids re-numbering existing rows.

## Service-layer changes

```csharp
public interface IPayrollProcessingService
{
    // Existing — unchanged
    Task<PayrollRun> RunPayrollAsync(int month, int year, string processedBy);
    Task<PayrollRun?> GetPayrollRunAsync(int payrollRunId);
    Task<List<PayrollRun>> GetPayrollHistoryAsync();
    Task ReversePayrollAsync(int payrollRunId, string reversedBy);

    // CHANGED behavior — feature-flagged. When the 2-stage flow is on,
    // LockPayrollAsync becomes an internal terminal step that ApprovePayrollAsync
    // calls. When off (legacy behavior preserved during rollout), it works as today.
    Task LockPayrollAsync(int payrollRunId, string approvedBy);

    // NEW
    Task SubmitForApprovalAsync(int payrollRunId, string submittedBy);
    Task ApprovePayrollAsync(int payrollRunId, string approvedBy);
    Task RejectPayrollAsync(int payrollRunId, string rejectedBy, string reason);
}
```

### `SubmitForApprovalAsync`

- Loads run, validates `Status == Completed`.
- Sets `Status = PendingApproval`, stamps `SubmittedBy` + `SubmittedAt`.
- Audit log entry: `payroll.submit` with the run id and submitter.
- No financial side-effect — purely a state move.

### `ApprovePayrollAsync` — **the key SoD gate**

```csharp
if (run.Status != PayrollStatus.PendingApproval)
    throw new InvalidOperationException("...");

// Server-side SoD enforcement. Cannot approve own work.
if (string.Equals(run.SubmittedBy, approvedBy, StringComparison.OrdinalIgnoreCase)
 || string.Equals(run.ProcessedBy, approvedBy, StringComparison.OrdinalIgnoreCase))
    throw new InvalidOperationException(
        "Cannot approve a payroll run you submitted or processed. " +
        "A different user must approve.");

run.Status = PayrollStatus.Locked;
run.ApprovedBy = approvedBy;
run.ApprovedAt = DateTime.UtcNow;
// THEN do the loan-balance updates the existing LockPayrollAsync does.
```

### `RejectPayrollAsync`

- Validates `Status == PendingApproval`, runs the same SoD check.
- Sets `Status = Rejected`, stamps `RejectedBy`, `RejectedAt`, `RejectionReason`.
- Audit log entry: `payroll.reject` with the reason.
- Does not undo any prior `RunPayrollAsync` side-effects — the run is still
  there; the preparer fixes whatever was wrong and re-runs `RunPayrollAsync`
  (which re-creates the run as Draft → Processing → Completed) or
  re-submits if the data didn't need changes.

### Idempotency & race

- All transitions use `Where(r => r.Status == fromStatus)` in the
  `ExecuteUpdateAsync` so two concurrent submits / approves don't both win.
- The DB-level unique-on-`(PayPeriodMonth, PayPeriodYear)` index (already
  there per `RunPayrollAsync`) prevents two preparers running the same period
  twice.

## API surface

| Verb | Path | Auth | Body |
|---|---|---|---|
| POST | `/api/payroll/runs/{id}/submit` | `Roles = "PayrollPreparer,SuperAdmin"` | – |
| POST | `/api/payroll/runs/{id}/approve` | `Roles = "PayrollApprover,SuperAdmin"` | – |
| POST | `/api/payroll/runs/{id}/reject` | `Roles = "PayrollApprover,SuperAdmin"` | `{ "reason": "..." }` |
| POST | `/api/payroll/runs/{id}/lock` | `Roles = "PayrollApprover,SuperAdmin"` | – (kept for legacy; behind feature flag) |

`PayrollPreparer` and `PayrollApprover` are new roles. Existing `HRManager` /
`Finance` users get the appropriate one assigned during the rollout migration.

## Authorization

`SuperAdmin` keeps the ability to do both halves (break-glass for ops), but
the SoD check still applies — even SuperAdmin can't approve their own
submission. This is deliberate: SoD is an integrity control, not an authz
control.

If the deployment is small (3 or fewer payroll-handling staff) and
SuperAdmin has to be on both sides, an `appsettings` flag
`Payroll:SkipSoDCheck = true` lets the org disable the check explicitly with
an audit log entry on every approval recording that the gate was bypassed.
Default off.

## Audit log

Every transition writes one `AuditLog` row:

```
{
  EntityType: "PayrollRun",
  EntityId: 42,
  Action: "payroll.submit" | "payroll.approve" | "payroll.reject" | "payroll.lock",
  Actor: <username>,
  At: <utc>,
  Detail: <json with fromStatus, toStatus, optional reason>
}
```

Same shape as the existing `AuditLogs` table — no new audit infra needed.

## Migration

1. **Schema** — `dotnet ef migrations add AddPayrollApprovalFields`.
   Adds the 5 new nullable columns. Existing rows unaffected. EF migration
   bundle + the same `MigrateAsync()` startup hook used today.

2. **Seed** — add the two roles via `SeedData.InitializeAsync`. Idempotent.

3. **Backfill** — none required for existing `Locked` runs.

4. **Feature flag** — `Payroll:RequireTwoStageApproval = true` (default `true`
   on a fresh deploy). When `false`, the existing single-step `lock`
   endpoint short-circuits the new state machine and behaves as today, so
   in-flight customers can roll out at their own pace.

5. **Deploy** — schema first (auto-applied at API startup), then UI changes
   in the next NickHR.WebApp release. The intermediate state — schema in
   place but UI still using the legacy `lock` button — is fine because
   `LockPayrollAsync` accepts both `Completed` and `PendingApproval` as
   starting states under the feature-flag-off path.

## UI (NickHR.WebApp) — outside this doc

Mockups live separately. High level:

- **Payroll list page** gets a `Status` chip showing `PendingApproval`
  (yellow) and `Rejected` (red) for the new states.
- **Run detail page** for a `Completed` run shows a **Submit for approval**
  button (preparer-visible only).
- **Run detail page** for a `PendingApproval` run shows **Approve** and
  **Reject** buttons (approver-visible only). Reject opens a modal asking
  for the reason.
- **Run detail page** for a `Rejected` run shows the rejection reason and
  the date, with a **Resubmit** button (after the preparer fixes whatever
  was wrong and re-runs).

## Out of scope

- Multi-tier approval (Preparer → Reviewer → Approver). YAGNI for this org.
- Email notifications on submit/approve/reject. Existing `INotificationService`
  + the NickComms outbox already covers this; the hook is a one-line add in
  each transition method but doesn't need design here.
- Date-bound rejections (e.g., "must be approved by month-end"). Add later
  via a scheduled task that surfaces stale `PendingApproval` runs in the
  dashboard.

## Decisions to confirm before code

1. ✅ / ❌ Two new roles (`PayrollPreparer`, `PayrollApprover`) — or reuse
   existing role names?
2. ✅ / ❌ Default `Payroll:RequireTwoStageApproval = true` on fresh
   deploys, opt-out via flag — or leave it off and require explicit opt-in?
3. ✅ / ❌ SuperAdmin still subject to SoD — or full bypass?
4. ✅ / ❌ Reject reason **required** (current proposal) — or optional?
5. Names of the roles (`PayrollPreparer` / `PayrollApprover`) — fine, or
   match a customer-specific convention?

Once these are answered, code is roughly:
- 1 EF migration
- ~80 LOC service additions
- ~40 LOC API additions
- ~150 LOC NickHR.WebApp UI changes
- Tests for each transition + the SoD check

≈ 1–2 days of focused work after design sign-off.

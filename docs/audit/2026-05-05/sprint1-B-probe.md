# Sprint 1 — Agent B-Probe report on audit finding 2.01

**Date:** 2026-05-05  
**Agent:** Sprint 1 Agent B-Probe (read-only diagnostic)  
**Scope:** Establish controlled reproduction of finding `2.01` — `TenantConnectionInterceptor` does not re-fire on Npgsql pooled-connection retries under `EnableRetryOnFailure`, causing background services to see RLS as fail-closed `'0'` and read 0 rows.  
**DB user used:** `nscim_app` (non-superuser, `bypassrls=false`) with `NICKSCAN_DB_PASSWORD`. **No writes performed.**  
**Probe code:** `C:\temp\nscim-probe\Sprint1B.cs` (compiled into existing `probe.csproj`; entry `dotnet run --no-build sprint1b`).

---

## 1. Where the interceptor lives

### Class
`C:\Shared\NSCIM_PRODUCTION\platform\NickERP.Platform.Tenancy\TenantConnectionInterceptor.cs:35-118` — a `DbConnectionInterceptor` that overrides `ConnectionOpened` and `ConnectionOpenedAsync`. Both call `ConfigureCommand`, which builds a parameterised `SELECT set_config(@name, @value, false)` and executes it on the just-opened `DbConnection`.

Verbatim, the load-bearing 30 lines (50–95):

```csharp
public override void ConnectionOpened(
    DbConnection connection,
    ConnectionEndEventData eventData)
{ ApplyTenantSetting(connection); }

public override async Task ConnectionOpenedAsync(
    DbConnection connection,
    ConnectionEndEventData eventData,
    CancellationToken cancellationToken = default)
{ await ApplyTenantSettingAsync(connection, cancellationToken).ConfigureAwait(false); }

private void ApplyTenantSetting(DbConnection connection)
{
    try {
        using var cmd = connection.CreateCommand();
        ConfigureCommand(cmd);
        cmd.ExecuteNonQuery();
    } catch (Exception ex) {
        _logger.LogWarning(ex,
            "Failed to set app.tenant_id={TenantId} on opened connection — RLS will fall back to the COALESCE default.",
            _tenantContext.TenantId);
    }
}
```

The class doc (lines 8-34) explicitly claims pool checkout re-fires the interceptor: *"Returning the connection to the pool resets `app.tenant_id` via Npgsql's `RESET ALL` on close, so the next pool consumer always re-sets it through this interceptor before its first query runs."*

### Registration
- DI: `platform\NickERP.Platform.Tenancy\TenancyServiceCollectionExtensions.cs:24` — registered **scoped** via `AddNickERPTenancy()`.
- DbContext wiring: `src\NickScanCentralImagingPortal.Services\ServiceConfiguration.cs:139-141, 176-178, 210-212` — three DbContexts (`ApplicationDbContext`, `IcumDbContext`, `IcumDownloadsDbContext`) all do:
  ```csharp
  var tci = serviceProvider.GetService<TenantConnectionInterceptor>();
  if (tci != null) options.AddInterceptors(tci);
  ```
- Each DbContext also calls `npgOptions.EnableRetryOnFailure(3, 5s, null)` (lines 152-155, 188-191, 222-225) — the audit's suspect.
- Program.cs ordering (`src\NickScanCentralImagingPortal.API\Program.cs`): `AddStandardizedServices` at line 134 (runs DbContext factory **registration**, not factory **execution**), then `AddNickERPTenancy()` at line 190. Order is harmless because the factory only runs at first DbContext resolve.

---

## 2. The bug as the probe sees it

### Confirmed mechanism (raw Npgsql)
Part 1 of the probe ran two Npgsql opens against the same pool, **without** an interceptor:

```
Open A → set app.tenant_id='1' → query → close (pool return)
  setting='1' pending=1566
Open B (pool reuse) → no SET → query
  setting='' pending=0
```

This proves the **pool DOES clear `app.tenant_id`** between checkouts. So *something* must put the GUC back, or RLS fail-closes to `'0'`.

### Refuted mechanism (EF + the production interceptor)
Parts 2–4 wired up the exact production interceptor pattern (mimic class) into a fresh EF DbContext with `EnableRetryOnFailure(3, 5s)`. Across three patterns — flat scopes, nested scopes, even `_serviceProvider.CreateScope()` from a scoped service that mimics `ContainerCompletenessService`:

```
Scope 0..4: app.tenant_id='1' pending=1566 (interceptor async-fires=1..5)
nested-scope iter 0..2: s='1' pending=1566 (fires=1..3)
Pattern 4 (CCS-shape): setting='1' pending=1566 fires=1..3
```

**The interceptor fires reliably and the GUC is set on the connection EF actually queries on.** The audit's headline hypothesis ("interceptor doesn't re-fire under retry") **does not reproduce** in this controlled probe environment.

### What live production says (Part 6 + service logs)
The bug is nonetheless real. From `Data\Logs\nickscan-20260505.txt` post-deploy at 11:46 UTC:

```
[INF] ContainerCompletenessOrchestratorService:
        [COMPLETENESS-POLLING] Work count: 1566, Should execute: true
[INF] ContainerCompletenessService:
        [CONTAINER-COMPLETENESS] ⚠️ No items retrieved from queue.
        Stats: 0 pending, 0 stuck in Processing, 0 exceeded max retries
```

**Same DbContext type, same scope-creation pattern** (both use `_serviceProvider.CreateScope()` → `GetRequiredService<ApplicationDbContext>()` → `.ContainerScanQueues.CountAsync(...)`). The orchestrator's `GetCompletenessWorkCountAsync` (`ContainerCompletenessOrchestratorService.cs:524-555`) sees **1566**. The inner `ContainerCompletenessService.CheckContainerCompletenessAsync` (`ContainerCompletenessService.cs:155-218`) calls the queue repository with the same DI shape and sees **0**, then logs the stats line above with `totalPending=0`. There is no logical reason the two should differ — yet they do, every cycle, since the 11:46 deploy and back through the 14-hour incident the audit started from.

So the symptom is real. But the proximate mechanism is **not** EF retry + interceptor in isolation. It is something in the orchestrator → inner-CCS call chain that doesn't re-establish the GUC inside the inner scope. The probe cannot reproduce it cold against the same DB; the difference must lie in DI lifetime / ordering / interceptor-instance state inside the actual deployed binary that the probe doesn't replicate.

### Schema-side facts (Parts 7–8)
- `containerscanqueues`: `rls_enabled=true`, `force_rls=true`. Policy `tenant_isolation_containerscanqueues` qual: `(tenant_id = (COALESCE(NULLIF(current_setting('app.tenant_id'::text, true), ''::text), '0'::text))::bigint)` — fail-closes to `'0'`.
- `nscim_app`: `super=false`, `bypassrls=false`. RLS applies fully.
- 1566 pending rows all carry `tenant_id=1`. All become invisible the moment `app.tenant_id` is unset on the session.

---

## 3. Recommended fix shape

The probe definitively validates one candidate fix (Part 5):

```
Open 0..3 (Options=-c app.tenant_id=1 in connection string):
  setting='1' pending=1566
```

Setting the connection-string `Options=-c app.tenant_id=1` makes Postgres apply the GUC on **physical connection establishment**, *before* any application code runs. Npgsql's `RESET ALL` on close still clears it, but the next checkout re-runs the startup options *and* re-sets the GUC. This already shipped for 8.01 (Serilog sink) on `b63e5b9` / `3f984f3` and is proven to survive pool reuse.

**Recommended fix (combined):**

1. **Append `Options=-c app.tenant_id=1` to the three NSCIM connection strings** (`NS_CIS_Connection`, `ICUMS_Connection`, `ICUMS_Downloads_Connection`) in `appsettings.json` — same pattern as the 8.01 fix in `Program.cs:95-99`. This is the floor: every connection in every pool starts with the right GUC, regardless of whether any interceptor fires.
2. **Keep the `TenantConnectionInterceptor` for the multi-tenant phase-2 future** — when tenants other than `1` exist, the interceptor must override the connection-string default per-request. The combination is belt-and-braces: the connection-string handles single-tenant (`1`) baseline, the interceptor specialises for non-1 tenants.
3. **Optional hardening** — switch the interceptor from `DbConnectionInterceptor.ConnectionOpenedAsync` to a `DbCommandInterceptor.ReaderExecutingAsync`/`NonQueryExecutingAsync` that prepends `SET LOCAL app.tenant_id = '<tid>';` to every command. This is robust to *any* connection bypass (pool reset, retry, batch), at the cost of one extra round-trip per command-tree execution. Defer unless step 1+2 are still flaky in production.

The audit's #1 recommendation ("`SELECT set_config(...)` as the first command of every scope's first DbContext use") is functionally what the interceptor already does — adding it again won't help. The audit's #3 ("wrap the BackgroundService loop in an explicit transaction with `SET LOCAL app.tenant_id='1'`") works but bloats every long-loop service.

---

## 4. Risk surface for the implementation agent

**Files the fix will likely touch:**
- `src\NickScanCentralImagingPortal.API\appsettings.json` (and `appsettings.Production.json` if it overrides) — three connection strings.
- `src\NickScanCentralImagingPortal.API\Program.cs:95-99` already has the pattern; consider extracting to a helper and calling it for the three EF connection strings as well as the Serilog sink.
- Possibly `src\NickScanCentralImagingPortal.Services\ServiceConfiguration.cs:144-156, 180-192, 214-226` if the connection-string normalisation belongs there instead.
- The other deployed services (`NickComms.Gateway`, `NickHR.API`, `NSCIM_Portal`, `NickHR.WebApp`) — check for the same `EnableRetryOnFailure` + interceptor pattern; if present, append the same `Options=` fragment.

**Blast radius if the fix is wrong:**
- **Tight** — the `Options=-c app.tenant_id=1` fragment is harmless at the Postgres level. If the GUC is already `'1'` when the interceptor or any other code subsequently SETs it, that's a no-op overwrite. The only failure mode is if the connection-string format is mis-pasted (Npgsql will throw on connect with a clear error). No data risk.
- A **wider** risk is that this fix masks a *deeper* DI/ordering bug in NSCIM that the probe could not reproduce. After the 8.01-style fix lands, leave the interceptor in place and add an INFO-level log on each `ConnectionOpenedAsync` invocation for one cycle to verify the interceptor is firing in production for both the orchestrator scope and the inner CCS scope. If they diverge the underlying bug is still there, just papered over by the connection-string default.

**Validation steps for the implementation agent:**
1. Pre-deploy: re-run `Sprint1B.cs` Part 5 (already passing) against the modified connection string — confirms the `Options=` syntax is honoured.
2. Post-deploy: tail `nickscan-<date>.txt` for the next CCS cycle and verify the inner service logs `Retrieved N items from queue` with N matching the orchestrator's `Work count`. The diverging-`0` cycle is the canary; once it stops, the fix is working.
3. Probe re-run with the new connection string forcibly stripped of `Options=` (set a sandbox env): confirm the `0 pending` symptom returns. Confirms the connection-string default is what's holding the line.
4. Crosscheck `applicationlogs`: after deploy, confirm Serilog INFO entries from the CCS service appear (the 8.01 fix already enables the sink for tenant `1`; if both fixes work the table fills up again).

---

## 5. Confirmation of read-only behaviour

- Probe code under `C:\temp\nscim-probe\Sprint1B.cs` issues only `SELECT`, `SELECT set_config(...)`, and `SELECT current_setting(...)`. No INSERT/UPDATE/DELETE/DDL.
- The DB user is `nscim_app` (non-superuser). All RLS policies apply.
- No code changes were made to either v1 (`C:\Shared\NSCIM_PRODUCTION\`) or v2 (`C:\Shared\ERP V2\`) trees. Only the probe project at `C:\temp\nscim-probe\` was extended (`Sprint1B.cs` added; `Program.cs` got a new `sprint1b` arg branch; `probe.csproj` got EF + DI package references). All of that is read-only at the database layer.
- No deploys, no service restarts, no `nssm` changes.

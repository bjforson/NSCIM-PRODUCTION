# NickERP Migration — Quick Start

> **Update 2026-04-17:** migration is now **single-script delta-sync** via [`Deploy-ERP-Target.ps1`](./Deploy-ERP-Target.ps1). The old numbered scripts (01-09) are kept for reference but not needed for the normal flow.

## Single-command deploy (new flow)

On **target** server (10.0.1.254) as Administrator, with `Y:\` or `C:\NICK ERP\` mapped to source share:

```powershell
powershell -ExecutionPolicy Bypass -File "Y:\Deploy-ERP-Target.ps1"
```

One idempotent script:
- Checks disk, .NET 8, PostgreSQL 18, Python, NSSM, firewall, Defender
- Sync binaries from source (robocopy delta)
- Restores 7 databases (skips if already populated)
- Registers 7 Windows services (skips if already registered with correct path)
- Starts services in dependency order
- Reports summary of actual changes

Safe to re-run anytime. Will only do what's needed.

## Secrets sync (separate one-time step per target)

On **source** (NSPORTAL), generate the secrets export to Y:\:
```powershell
powershell -ExecutionPolicy Bypass -File "C:\Shared\NSCIM_PRODUCTION\docs\migration\regenerate-secrets-export.ps1"
```

On **target**, import:
```powershell
powershell -ExecutionPolicy Bypass -File "C:\NICK ERP\set-secrets-on-target.ps1"
Restart-Service NSCIM_API, NSCIM_WebApp, NSCIM_Mobile, NSCIM_NickComms, NickHR_API, NickHR_WebApp, NSCIM_ImageSplitter -Force
```

`set-secrets-on-target.ps1` is gitignored (contains plaintext secrets) — regenerated from env vars each migration.

---

## Legacy numbered scripts

Full runbook is in [`RUNBOOK.md`](./RUNBOOK.md). This is the TL;DR with command order.

## What's happening

- **Source:** NSPORTAL (current prod, R320) — stays running, no changes
- **Target:** TEST-SERVER @ 10.0.1.254 (R630, 12× CPU, 2× RAM) — receives fresh 3.0.0 build on .NET 10
- **Mount:** `Y:\` on source = `\\10.0.1.254\nick erp` = target's `C:\Shared\NSCIM_PRODUCTION\`
- **Images:** Stay on `Z:\` = `\\172.16.1.1\image` (target mounts same share)

## Order of operations

| # | Script | Where | Purpose |
|---|---|---|---|
| 01 | `01-verify-source.ps1` | current box | Confirm source is healthy & reachable to target |
| 02 | `02-verify-target.ps1` | **target box** (RDP) | Confirm .NET 10, Postgres 18, directories, ports, firewall, Defender |
| — | (manual) | target | Install missing prerequisites flagged by 02 |
| — | `git checkout -b upgrade/net10-3.0` | current box | Create upgrade branch |
| — | (manual dev work) | current box | Bump TFM to net10, packages to 10.x, MudBlazor 9.x. Fix all build errors. |
| 03 | `03-copy-files.ps1` | current box | Copy Data\, services\, NickHR\ to Y:\ |
| 04 | `04-dump-databases.ps1` | current box | pg_dump all 7 NSCIM DBs to Y:\db-dumps\ |
| 05 | `05-restore-databases.ps1 -DumpDir <path>` | **target box** (RDP) | pg_restore on target |
| 06 | `06-publish-to-target.ps1` | current box | Build 3.0.0 and publish directly to Y:\publish\{API,WebApp,Mobile}\ |
| 07 | `07-register-services.ps1` | **target box** (RDP) | Register 4 Windows services |
| — | Start services on target | target | `Start-Service NSCIM_API; sleep 10; Start-Service NSCIM_WebApp, NSCIM_Mobile, NSCIM_ImageSplitter` |
| — | Verify 3.0.0 running | anywhere | `curl -k https://10.0.1.254:5300/api/server/version` |
| — | **P5 shadow validation** | target | Test every feature on target while source still serves users. 24h soak minimum. |
| — | Schedule maintenance window | — | 48h user notice |
| 08 | `08-cutover-stop-source.ps1` | current box | T-0: Stop source services |
| 04 | `04-dump-databases.ps1` (again) | current box | Final delta dump |
| 05 | `05-restore-databases.ps1 -DropExisting` | **target box** | Final restore (destroys target DB, replaces with source final) |
| — | Start target services | target | `Start-Service NSCIM_API; ...` |
| — | DNS/reverse proxy switch | — | Route traffic to 10.0.1.254 |
| — | Smoke test | — | Login, search, image, audit |
| 09 | `09-cutover-rollback.ps1` | current box | **Only if cutover fails** — restart source, revert DNS |

## Who does what

- **Ops**: phase 0 (RDP to target, run 02), phase 2 (install prereqs), phase 6 (DNS switch)
- **Me / engineering**: phase 1 (code upgrade), phase 4 (publish), phase 5 (functional testing)
- **Both**: phase 3 (data migration, scripted), phase 6 (cutover window)

## Key env vars needed

Set on current box before running scripts 04, 06:
```powershell
[Environment]::SetEnvironmentVariable("NICKSCAN_DB_PASSWORD", "<pwd>", "User")
```

Set on target server before running scripts 05, 07:
```powershell
[Environment]::SetEnvironmentVariable("NICKSCAN_DB_PASSWORD", "<target-pwd>", "Machine")
[Environment]::SetEnvironmentVariable("NICKSCAN_ASE_PASSWORD", "<ase-pwd>", "Machine")
```

## Gates (do not proceed past if failing)

1. **After 02:** target has .NET 10, Postgres 18, free disk, ports open. Red items in the output script = stop.
2. **After phase 1 code upgrade:** `dotnet build` clean, tests green.
3. **After 05 restore:** row counts match source. Query `AnalysisRecords`, `BOEDocuments`, `ImageAnalysisDecisions`, `AuditDecisions` on both — exact match required.
4. **After target services start:** `/api/server/version` returns `3.0.0`. Key pages render without 500s.
5. **Before cutover:** 24h shadow soak on target, zero regressions.

## Timeline

- Days 1–5: code upgrade (parallel with target prep)
- Days 3–7: target prep
- Day 7: file + DB transfer
- Days 8–10: publish + service setup on target
- Days 10–13: shadow validation
- Day 14: cutover window
- Days 14–44: warm standby on old box, then decommission

## Rollback TL;DR

1. `09-cutover-rollback.ps1` on current box
2. Revert DNS/reverse proxy
3. Done — users back on 2.8.0 within 5 min

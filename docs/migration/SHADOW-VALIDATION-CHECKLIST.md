# Shadow Validation Checklist — Target 10.0.1.254

> **Retirement notice (2026-04-22):** `NSCIM_Mobile` retired. Drop `NSCIM_Mobile` from any service array below when running commands; `NSCIM_WebApp` now handles mobile viewports.

**Goal:** Validate the full ERP is healthy on the new server before cutting over DNS.
**Duration:** 24h minimum. Longer is better.
**Source prod keeps running throughout.** Users see no change until DNS flip.

## Phase 0 — Import secrets (do this first, before anything else)

On **target** as Administrator:

```powershell
# 1. Set all env vars from source
powershell -ExecutionPolicy Bypass -File "C:\NICK ERP\set-secrets-on-target.ps1"

# 2. Restart all services to pick up env vars
Restart-Service NSCIM_API, NSCIM_WebApp, NSCIM_Mobile, NSCIM_NickComms, NickHR_API, NickHR_WebApp, NSCIM_ImageSplitter -Force

# 3. Wait 30s, then verify
Start-Sleep -Seconds 30
Get-Service NSCIM_*, NickHR_*
```

Expected: all 7 services Running.

---

## Phase 1 — Smoke tests (5 min, do immediately after secrets import)

### 1.1 Endpoint probes from target

```powershell
curl.exe -sk https://localhost:5206/api/server/version   # Expect: {"version":"2.8.0",...}
curl.exe -sk https://localhost:5300/api/server/version   # Expect: {"version":"2.8.0",...}
curl.exe -sk http://localhost:5215/                      # Expect: any 200/302/404 (live response)
curl.exe -sk http://localhost:5220/                      # Expect: 200 or health JSON
curl.exe -sk https://localhost:5311/                     # Expect: 302 redirect to login
```

If any return `timeout` / `connection refused` → check: `Get-Service` status, `Get-Content 'C:\Shared\NSCIM_PRODUCTION\Data\Logs\nickscan-errors-*.txt' -Tail 50`.

### 1.2 Endpoint probes from your laptop (real network access)

Replace `TEST-SERVER` with actual hostname or IP `10.0.1.254`:

- `https://10.0.1.254:5300/api/server/version` — JSON with version
- `https://10.0.1.254:5300` — NSCIM login page loads
- `https://10.0.1.254:5311` — NickHR login page loads

### 1.3 Login test

Open `https://10.0.1.254:5300` in browser:
- [ ] Login page renders
- [ ] Log in as superadmin (password from source `NICKSCAN_SUPERADMIN_PASSWORD` env var)
- [ ] Dashboard loads after login
- [ ] No red errors in browser DevTools console
- [ ] No 401/500 errors in Network tab

If login fails with "Invalid username or password" but works on source → check `NICKSCAN_JWT_SECRET_KEY` matches exactly.

---

## Phase 2 — Functional tests (1h, right after smoke tests pass)

### 2.1 NSCIM core flows

Click through each and note anything weird:

- [ ] **Dashboard** (`/`) — 4 stat cards show numbers, not errors
- [ ] **Containers** (`/containers`) — list loads with pagination
- [ ] **Container details** — click any container, view images, ICUMS data tab, manifest
- [ ] **Global search** (`/search?q=<container>`) — returns results grouped by type
- [ ] **Audit queue** (`/audit-review`) — list of audit assignments loads
- [ ] **Loose cargo** (`/customs/icums/loose-cargo`) — table populated
- [ ] **Vehicles** (`/vehicles`) — VIN list loads
- [ ] **ICUMS dashboard** (`/customs/icums`) — stats render, no crash
- [ ] **Record completeness** (`/validation/record-completeness`) — grid loads
- [ ] **X-ray inspector** (`/validation/xray-inspector`) — tool opens
- [ ] **Reports** (`/reports`) — shows "Coming Soon" cards (deferred feature)
- [ ] **Notifications bell** — no infinite spinner (pool leak regression check)

### 2.2 Audit workflow smoke test

- [ ] Pick an audit assignment, open it
- [ ] Approve one container, verify it advances to next container OR completes
- [ ] Check progress indicator shows `X/Y audited` correctly
- [ ] Verify no "5/1" style broken progress

### 2.3 ICUMS submission flow

- [ ] In `Data\ICUMS\Outbox\`, check that recently-audited records produced JSON payloads
- [ ] No `FATAL` or `ICUMS submission failed` entries in `nickscan-errors-*.txt`

### 2.4 NickHR

- [ ] Open `https://10.0.1.254:5311`
- [ ] Log in (admin account from `nickhr` database)
- [ ] Navigate to Users page — user list loads
- [ ] Navigate to Resend Invitation feature — button shows only for never-logged-in users

### 2.5 ImageSplitter (Python service)

- [ ] `curl http://10.0.1.254:5320/health` — returns 200 JSON
- [ ] Check `Data\Logs\splitter.log` — no Python tracebacks in last hour

---

## Phase 3 — Soak period (24h minimum)

Let it run overnight. Tomorrow morning, check:

### 3.1 Error log health

On target:

```powershell
$today = Get-Date -Format 'yyyyMMdd'
# Lines with ERR] — exclude the two pre-existing FS6000 + ICUMS-archive patterns
Get-Content "C:\Shared\NSCIM_PRODUCTION\Data\Logs\nickscan-$today.txt" |
    Select-String 'ERR\]' |
    Select-String -NotMatch 'FileSyncService|IngestionService.*Error moving|IngestionService.*Failed to read|IngestionService.*Error processing .img|IcumFileArchiveService.*archive indexes' |
    Measure-Object | Select-Object -ExpandProperty Count
```

Expected: `0` (any non-zero → investigate pattern).

### 3.2 Postgres connection count

Should stay stable, not climb:

```powershell
& "C:\Program Files\PostgreSQL\18\bin\psql.exe" -h localhost -U postgres -c `
  "SELECT datname, state, count(*) FROM pg_stat_activity WHERE datname LIKE 'nick%' GROUP BY datname, state ORDER BY datname, state;"
```

Expected: total `< 40` after 24h. If climbing above 60, pool leak — do NOT cut over.

### 3.3 Service uptime

```powershell
Get-Service NSCIM_*, NickHR_* | Format-Table Name, Status
Get-WmiObject Win32_Service | Where-Object { $_.Name -like 'NSCIM*' -or $_.Name -like 'NickHR*' } | Select-Object Name, State, StartMode
```

All 7 should still be Running. No crashes, no auto-restarts in Event Viewer.

### 3.4 Functional recheck

Re-run Phase 2 tests. If anything regressed, investigate before cutover.

---

## Phase 4 — Go/no-go decision

**GO (proceed to cutover):**
- All Phase 1 smoke tests passed
- All Phase 2 functional tests passed
- 24h+ soak, Phase 3 error count = 0
- Postgres connections stable
- No user-visible regressions

**NO-GO (investigate before cutover):**
- Any service failed to stay running
- Login broken
- DB connection count climbing
- Any Phase 2 feature regressed
- New error patterns in log

---

## Cutover procedure (when GO)

### Pre-cutover (T-2h)

1. Schedule maintenance window, notify users
2. Re-run `Deploy-ERP-Target.ps1` to catch any drift (should report 0 changes)
3. Verify target's `nickscan_production` row counts match source:
   ```sql
   SELECT 'AnalysisRecords', count(*) FROM analysisrecords
   UNION ALL SELECT 'BOEDocuments', count(*) FROM boedocuments
   UNION ALL SELECT 'ImageAnalysisDecisions', count(*) FROM imageanalysisdecisions
   UNION ALL SELECT 'AuditDecisions', count(*) FROM auditdecisions;
   ```
   Compare source vs target. Any divergence > "a few hundred new audits" = fresh dump needed.

### Cutover (T-0)

1. **Stop source services:**
   ```powershell
   # On SOURCE (NSPORTAL)
   sc.exe failure NSCIM_API reset= 0 actions= ""
   sc.exe failure NSCIM_WebApp reset= 0 actions= ""
   sc.exe failure NSCIM_Mobile reset= 0 actions= ""
   sc.exe failure NSCIM_NickComms reset= 0 actions= ""
   sc.exe failure NSCIM_ImageSplitter reset= 0 actions= ""
   sc.exe failure NickHR_API reset= 0 actions= ""
   sc.exe failure NickHR_WebApp reset= 0 actions= ""
   Stop-Service NSCIM_*, NickHR_* -Force
   ```

2. **Final DB delta dump + restore:**
   On source:
   ```powershell
   $ts = Get-Date -Format 'yyyyMMdd-HHmm'
   $dir = "Y:\db-dumps\$ts"
   mkdir $dir
   $env:PGPASSWORD = $env:NICKSCAN_DB_PASSWORD
   @('nickscan_production','nickscan_downloads','nickscan_icums') | ForEach-Object {
       & "C:\Program Files\PostgreSQL\18\bin\pg_dump.exe" -h localhost -U postgres -d $_ -F c -b --no-owner --no-privileges -f "$dir\$_.dump"
   }
   ```
   On target, update `$DumpTimestamp` in `Deploy-ERP-Target.ps1`, drop existing DBs, re-run script to restore fresh.

3. **DNS / reverse proxy switch:**
   - Update `A` record or reverse proxy config to point to `10.0.1.254`
   - Flush DNS on client boxes if using internal DNS

4. **Smoke test on new prod** — repeat Phase 1 checks

### Rollback (if cutover fails)

```powershell
# On SOURCE
sc.exe failure NSCIM_API reset= 86400 actions= "restart/5000/restart/5000/none/0"
sc.exe failure NSCIM_WebApp reset= 86400 actions= "restart/5000/restart/5000/none/0"
sc.exe failure NSCIM_Mobile reset= 86400 actions= "restart/5000/restart/5000/none/0"
sc.exe failure NSCIM_NickComms reset= 86400 actions= "restart/5000/restart/5000/none/0"
sc.exe failure NSCIM_ImageSplitter reset= 86400 actions= "restart/5000/restart/5000/none/0"
sc.exe failure NickHR_API reset= 86400 actions= "restart/5000/restart/5000/none/0"
sc.exe failure NickHR_WebApp reset= 86400 actions= "restart/5000/restart/5000/none/0"
Start-Service NSCIM_API, NickHR_API
Start-Sleep -Seconds 10
Start-Service NSCIM_WebApp, NSCIM_Mobile, NSCIM_NickComms, NSCIM_ImageSplitter, NickHR_WebApp
# Revert DNS
```

Source data is unchanged — users back on old prod within 5 min.

---

## Post-cutover (first 30 days)

- Keep source box powered on with services stopped (warm standby)
- Daily: check target `Data\Logs\nickscan-errors-*.txt`
- Weekly: DB growth vs expected
- After 30 days clean: decommission source

# NSCIM Server Migration Runbook

> **Retirement notice (2026-04-22):** `NSCIM_Mobile` service and `NickScanWebApp.Mobile` project are **retired**. `NSCIM_WebApp` (NickScanWebApp.New) now serves mobile viewports responsively. Any snippet below that lists `NSCIM_Mobile` or `publish\Mobile\` is historical — drop `NSCIM_Mobile` from service arrays and skip Mobile publish steps when actually running these commands.

**Source:** `NSPORTAL` (Dell R320, 4 threads, 64 GB) — `C:\Shared\NSCIM_PRODUCTION\`
**Target:** `TEST-SERVER` @ `10.0.1.254` (Dell R630, 48 threads, 128 GB) — `C:\Shared\NSCIM_PRODUCTION\` (accessed from here via `Y:\` = `\\10.0.1.254\nick erp`)
**Target .NET:** 10.0 LTS (currently running 8.0)
**Target Postgres:** 18 (must match source)
**Current prod version:** 2.8.0 (stays running on NSPORTAL throughout; only stops during final cutover)

---

## Migration strategy

Green-field deploy to target server. Old box stays untouched on 2.8.0 until cutover. Rollback = DNS switch.

**Data classification:**
| What | Size | Where it lives now | Strategy |
|---|---|---|---|
| Source code | 1.4 GB | `C:\Shared\NSCIM_PRODUCTION\src\` | Clone from git on target (don't copy) |
| Publish output | 0.41 GB | `publish\API\` + `publish\WebApp\` | Rebuild on target from upgraded code |
| Runtime data | 4.3 GB | `Data\` (ICUMS outbox, logs) | **Copy via Y:\\** |
| Python ImageSplitter | 0.49 GB | `services\image-splitter\` | **Copy via Y:\\** + recreate venv |
| NickHR | 0.88 GB | `NickHR\` | **Copy via Y:\\** |
| PostgreSQL databases | ~19 GB | Postgres 18 local | **pg_dump → Y:\\ → restore on target** |
| Image blobs | 391 GB | `Z:\\` = `\\172.16.1.1\image` | **Don't copy** — target mounts same share |
| Backups | 231 GB | `D:\Backups\` | Stays on source box |
| Worktrees / cruft | 17 GB | `.claude\worktrees\` | **Don't copy** |

**Transfer budget:** ~27 GB total via Y:\\ at 1 Gbps ≈ 5–10 min.

---

## Phase 0 — Verification (DO FIRST, 1 hour)

### P0.1 — Network checks (from current box)

```powershell
# Y: share reachable
Test-Path Y:\
# Expected: True

# Y: writable
"test" | Out-File Y:\migration-test.txt; Remove-Item Y:\migration-test.txt
# Expected: no error

# Route to image share from target must work — RDP to target and run:
# Test-Path "\\172.16.1.1\image"

# DB port reachable from current? (not required, DB work happens on target)
Test-NetConnection 10.0.1.254 -Port 5432
# Currently: False (firewall-blocked). That's fine — we won't connect over network.
```

### P0.2 — Inventory target (via RDP to 10.0.1.254)

On target server, open PowerShell as admin:

```powershell
# What's installed?
Get-WmiObject Win32_Product | Where-Object { $_.Name -match 'PostgreSQL|\.NET|Python|Node' } | Select-Object Name, Version
Get-Service postgresql-* | Format-Table Name, Status, DisplayName
dotnet --list-sdks
dotnet --list-runtimes

# Postgres version currently on target?
& "C:\Program Files\PostgreSQL\16\bin\psql.exe" --version 2>$null
& "C:\Program Files\PostgreSQL\18\bin\psql.exe" --version 2>$null

# Disk free?
Get-PSDrive C | Select-Object Name, @{N='Free(GB)';E={[math]::Round($_.Free/1GB,1)}}

# Existing services that might collide?
Get-NetTCPConnection -State Listen | Where-Object { $_.LocalPort -in 80,443,5205,5299,5300,5320,5432 } | Select-Object LocalPort, OwningProcess
```

### P0.3 — Document results

Save P0.2 output to `docs/migration/target-state-before.txt` before proceeding.

**Gate:** If target has Postgres 16 (not 18), plan for upgrade before Phase 3. If port 5432 is held by another Postgres, decide: in-place upgrade, or run NSCIM DBs on a different port.

---

## Phase 1 — Code upgrade (days 1–5, on branch, NO deploy)

### P1.1 — Create upgrade branch

```bash
cd /c/Shared/NSCIM_PRODUCTION
git checkout -b upgrade/net10-3.0
```

### P1.2 — Bump target framework (all 13 csproj files)

```powershell
Get-ChildItem -Path src -Filter *.csproj -Recurse | ForEach-Object {
    (Get-Content $_.FullName) -replace '<TargetFramework>net8.0</TargetFramework>', '<TargetFramework>net10.0</TargetFramework>' | Set-Content $_.FullName
}
```

### P1.3 — Bump Microsoft.* packages to 10.0.x

Across all projects:
- `Microsoft.EntityFrameworkCore` 9.0.9 → 10.0.x
- `Microsoft.EntityFrameworkCore.Tools` 9.0.9 → 10.0.x
- `Npgsql.EntityFrameworkCore.PostgreSQL` 9.0.4 → 10.x (wait for Postgres 18 compat release)
- `Npgsql` 9.0.3 → 10.x
- `Microsoft.AspNetCore.*` 8.0.x → 10.0.x
- `Microsoft.Extensions.*` 8.0/9.0/10.0 mix → all 10.0.x (unify)
- `MudBlazor` 8.13.0 → 9.x
- `System.Drawing.Common` 8.0.0 → 10.0.x

### P1.4 — Bump version

Edit `src/Directory.Build.props`:

```xml
<Version>3.0.0</Version>
<AssemblyVersion>3.0.0.0</AssemblyVersion>
<FileVersion>3.0.0.0</FileVersion>
<InformationalVersion>3.0.0</InformationalVersion>
```

### P1.5 — Install .NET 10 SDK locally (build env)

```powershell
winget install Microsoft.DotNet.SDK.10
# Verify
dotnet --list-sdks | Select-String "^10\."
```

### P1.6 — Build and fix errors

```bash
cd /c/Shared/NSCIM_PRODUCTION
dotnet restore
dotnet build 2>&1 | tee build-net10.log
# Expect: hundreds of errors. Work through them.
```

**Expected breakage categories:**
1. **MUD0002 warnings → errors** (282 instances). Fix attribute casing, removed attributes.
2. **Npgsql 10 JSONB mapping changes.** Check `AnalysisRecord.MatchQualityFlags`, AI training exports, any `JsonDocument` columns.
3. **EF Core 10 projection errors.** Any `Select(x => new { ... })` with complex expressions may break.
4. **System.Drawing.Common** → add `[SupportedOSPlatform("windows")]` attributes or suppress CA1416.
5. **Async interface changes** — hosted services may need updated signatures.

### P1.7 — Run tests

```bash
dotnet test src/NickScanCentralImagingPortal.Tests/NickScanCentralImagingPortal.Tests.csproj --no-build 2>&1 | tee test-net10.log
```

### P1.8 — Smoke test locally

```bash
cd src/NickScanCentralImagingPortal.API && dotnet run --no-build
# In another terminal:
cd src/NickScanWebApp.New && dotnet run --no-build
```

Open https://localhost:5300/, click through:
- `/audit-review`
- `/containers`
- `/operations/container-details/<any>`
- `/customs/icums`
- `/customs/icums/loose-cargo`
- `/vehicles`
- `/search`

**Gate:** All pages render, no 500s, no JS errors in console. Commit branch.

---

## Phase 2 — Target server prep (days 3–7, parallel with Phase 1)

Done via RDP to 10.0.1.254.

### P2.1 — Install .NET 10 on target

```powershell
# On TARGET server (10.0.1.254), as admin
winget install Microsoft.DotNet.SDK.10 --silent
winget install Microsoft.DotNet.AspNetCore.10 --silent
winget install Microsoft.DotNet.HostingBundle.10 --silent  # required for Kestrel-as-service

# Verify
dotnet --list-runtimes | Select-String "AspNetCore.App 10\."
```

### P2.2 — Install/upgrade PostgreSQL to 18 on target

**If target has Postgres 16:**
Option A (clean install, recommended for greenfield):
```powershell
# Stop existing 16
Stop-Service postgresql-x64-16 -Force

# Install 18 to separate directory (port 5433 to avoid collision)
# Download from https://www.postgresql.org/download/windows/
# Install with: Port=5432, DataDir=C:\PostgreSQL\18\data (not default C:\Program Files to keep free space)
# Then uninstall 16 after migration complete
```

Option B (pg_upgrade): Complex, skip unless Postgres 16 data is needed.

**Data directory placement:** DO NOT put on Y:\ (that's the same drive). Use local C: root directly (`C:\PostgreSQL\18\data`). Exclude from Defender.

### P2.3 — Install Python + ImageSplitter deps

```powershell
# On target
winget install Python.Python.3.12
python --version  # confirm 3.12+

# Will create venv in P3 after files copied
```

### P2.4 — Create directory structure on target

```powershell
# On target
New-Item -ItemType Directory -Force -Path "C:\Shared\NSCIM_PRODUCTION\publish\API"
New-Item -ItemType Directory -Force -Path "C:\Shared\NSCIM_PRODUCTION\publish\WebApp"
New-Item -ItemType Directory -Force -Path "C:\Shared\NSCIM_PRODUCTION\Data"
New-Item -ItemType Directory -Force -Path "C:\Shared\NSCIM_PRODUCTION\services"
New-Item -ItemType Directory -Force -Path "C:\Shared\NSCIM_PRODUCTION\Data\Logs"
New-Item -ItemType Directory -Force -Path "C:\Shared\NSCIM_PRODUCTION\Data\ICUMS\Outbox"
New-Item -ItemType Directory -Force -Path "C:\Shared\NSCIM_PRODUCTION\Data\ICUMS\Inbox"
```

### P2.5 — Mount image share

```powershell
# On target — persistent mount
New-SmbMapping -LocalPath 'Z:' -RemotePath '\\172.16.1.1\image' -Persistent $true -UserName '<svc-user>' -Password '<pwd>'
Test-Path Z:\
```

### P2.6 — Windows Defender exclusions

```powershell
# On target
Add-MpPreference -ExclusionPath "C:\Shared\NSCIM_PRODUCTION"
Add-MpPreference -ExclusionPath "C:\PostgreSQL\18\data"
Add-MpPreference -ExclusionProcess "NickScanCentralImagingPortal.API.exe"
Add-MpPreference -ExclusionProcess "NickScanWebApp.New.exe"
```

### P2.7 — Firewall rules

```powershell
# On target — allow inbound for API (5205/5206) and WebApp (5299/5300)
New-NetFirewallRule -DisplayName "NSCIM API HTTP" -Direction Inbound -LocalPort 5205 -Protocol TCP -Action Allow
New-NetFirewallRule -DisplayName "NSCIM API HTTPS" -Direction Inbound -LocalPort 5206 -Protocol TCP -Action Allow
New-NetFirewallRule -DisplayName "NSCIM WebApp HTTP" -Direction Inbound -LocalPort 5299 -Protocol TCP -Action Allow
New-NetFirewallRule -DisplayName "NSCIM WebApp HTTPS" -Direction Inbound -LocalPort 5300 -Protocol TCP -Action Allow
New-NetFirewallRule -DisplayName "NSCIM ImageSplitter" -Direction Inbound -LocalPort 5320 -Protocol TCP -Action Allow
```

### P2.8 — Hostname rename (if target keeps name after cutover)

```powershell
# On target — rename to production hostname, reboot required
Rename-Computer -NewName "NSPORTAL2" -Restart
```

**Gate:** All P2 steps green. `docs/migration/target-state-ready.txt` captured.

---

## Phase 3 — Data transfer (day 7, after Phase 1 branch stable)

### P3.1 — Copy static files to Y:\ (from current box)

```powershell
# From current box — ~5-10 min on 1 Gbps
robocopy "C:\Shared\NSCIM_PRODUCTION\Data" "Y:\Data" /E /R:2 /W:5 /XD "Logs\archives" /MT:16
robocopy "C:\Shared\NSCIM_PRODUCTION\services" "Y:\services" /E /R:2 /W:5 /MT:16
robocopy "C:\Shared\NSCIM_PRODUCTION\NickHR" "Y:\NickHR" /E /R:2 /W:5 /XD "bin" "obj" ".vs" /MT:16

# Critical config files (appsettings etc are repo-tracked; these are any local overrides)
# None expected, but double-check:
Get-ChildItem "C:\Shared\NSCIM_PRODUCTION" -Include "appsettings.Production.json","appsettings.Secrets.json" -Recurse -Force
```

### P3.2 — Database dumps (from current box)

```powershell
# From current box — preserves custom types, sequences, indexes
$env:PGPASSWORD = $env:NICKSCAN_DB_PASSWORD
$pgDump = "C:\Program Files\PostgreSQL\18\bin\pg_dump.exe"
$dumpDir = "Y:\db-dumps\$(Get-Date -Format 'yyyyMMdd-HHmm')"
New-Item -ItemType Directory -Force -Path $dumpDir | Out-Null

@('nickscan_production','nickscan_downloads','nickscan_icums','nickscan_icums_staging','nickhr','nick_comms','nick_platform') | ForEach-Object {
    Write-Host "Dumping $_..."
    & $pgDump -h localhost -U postgres -d $_ -F c -b -v -f "$dumpDir\$_.dump" 2>&1 | Out-File "$dumpDir\$_.log"
}

# Verify dumps
Get-ChildItem $dumpDir -Filter *.dump | Select-Object Name, @{N='SizeMB';E={[math]::Round($_.Length/1MB,1)}}
```

Expected output (approx):
- nickscan_production.dump: ~2-3 GB
- nickscan_icums_staging.dump: ~4 GB
- Others: smaller

### P3.3 — Restore on target (run ON TARGET via RDP)

```powershell
# On target, as admin
$env:PGPASSWORD = "<postgres password>"
$pgRestore = "C:\Program Files\PostgreSQL\18\bin\pg_restore.exe"
$psql = "C:\Program Files\PostgreSQL\18\bin\psql.exe"
$dumpDir = "C:\Shared\NSCIM_PRODUCTION\db-dumps\<timestamp>"  # copy from Y:\ first

# Create empty DBs
@('nickscan_production','nickscan_downloads','nickscan_icums','nickscan_icums_staging','nickhr','nick_comms','nick_platform') | ForEach-Object {
    & $psql -h localhost -U postgres -c "CREATE DATABASE $_ OWNER postgres ENCODING 'UTF8'"
}

# Restore each
Get-ChildItem $dumpDir -Filter *.dump | ForEach-Object {
    $dbName = $_.BaseName
    Write-Host "Restoring $dbName..."
    & $pgRestore -h localhost -U postgres -d $dbName -v --no-owner --no-privileges $_.FullName 2>&1 | Out-File "$dumpDir\restore-$dbName.log"
}

# Verify
& $psql -h localhost -U postgres -c "SELECT datname, pg_size_pretty(pg_database_size(datname)) FROM pg_database WHERE datname LIKE 'nick%' ORDER BY datname"
```

### P3.4 — Sanity row counts (compare current vs target)

```powershell
# On CURRENT
$psql = "C:\Program Files\PostgreSQL\18\bin\psql.exe"
& $psql -h localhost -U postgres -d nickscan_production -c "SELECT 'AnalysisRecords' AS tbl, count(*) FROM analysisrecords UNION ALL SELECT 'BOEDocuments', count(*) FROM boedocuments UNION ALL SELECT 'ImageAnalysisDecisions', count(*) FROM imageanalysisdecisions UNION ALL SELECT 'AuditDecisions', count(*) FROM auditdecisions UNION ALL SELECT 'ManifestItems', count(*) FROM manifestitems"

# Repeat on TARGET after restore. Counts must match exactly.
```

**Gate:** Row counts match for top 5 tables. Any mismatch → investigate before proceeding.

### P3.5 — Update target appsettings if DB password differs

```powershell
# If target Postgres password differs from current, set env var:
[Environment]::SetEnvironmentVariable("NICKSCAN_DB_PASSWORD", "<target-pwd>", "Machine")

# Also required:
[Environment]::SetEnvironmentVariable("NICKSCAN_ASE_PASSWORD", "<ase-pwd>", "Machine")
# Other secrets: check appsettings for ***USE_ENV_VAR_*** placeholders
Select-String -Path "Y:\publish\API\appsettings.json" -Pattern "USE_ENV_VAR" | ForEach-Object { $_.Line }
```

---

## Phase 4 — Deploy to target (day 8–10)

### P4.1 — Publish from current box to Y:\

```powershell
# From current box, on upgrade/net10-3.0 branch, fully built
cd C:\Shared\NSCIM_PRODUCTION
git checkout upgrade/net10-3.0
dotnet publish src/NickScanCentralImagingPortal.API/NickScanCentralImagingPortal.API.csproj -c Release -o "Y:\publish\API"
dotnet publish src/NickScanWebApp.New/NickScanWebApp.New.csproj -c Release -o "Y:\publish\WebApp"
dotnet publish src/NickScanWebApp.Mobile/NickScanWebApp.Mobile.csproj -c Release -o "Y:\publish\Mobile"
```

### P4.2 — Python ImageSplitter venv (on target)

```powershell
# On target
cd C:\Shared\NSCIM_PRODUCTION\services\image-splitter
python -m venv venv
.\venv\Scripts\activate
pip install -r requirements.txt
deactivate
```

### P4.3 — Register Windows services (on target)

```powershell
# On target — use same service names/paths as current box
$apiExe = "C:\Shared\NSCIM_PRODUCTION\publish\API\NickScanCentralImagingPortal.API.exe"
$webExe = "C:\Shared\NSCIM_PRODUCTION\publish\WebApp\NickScanWebApp.New.exe"
$mobileExe = "C:\Shared\NSCIM_PRODUCTION\publish\Mobile\NickScanWebApp.Mobile.exe"
$splitterExe = "C:\Shared\NSCIM_PRODUCTION\services\image-splitter\run-service.bat"

New-Service -Name "NSCIM_API" -BinaryPathName $apiExe -DisplayName "NSCIM Production API" -StartupType Automatic
New-Service -Name "NSCIM_WebApp" -BinaryPathName $webExe -DisplayName "NSCIM Production WebApp" -StartupType Automatic
New-Service -Name "NSCIM_Mobile" -BinaryPathName $mobileExe -DisplayName "NSCIM Production Mobile" -StartupType Automatic
New-Service -Name "NSCIM_ImageSplitter" -BinaryPathName $splitterExe -DisplayName "NSCIM Image Splitting Service" -StartupType Automatic

# Configure failure-recovery (auto-restart)
sc.exe failure NSCIM_API reset= 86400 actions= "restart/5000/restart/5000/none/0"
sc.exe failure NSCIM_WebApp reset= 86400 actions= "restart/5000/restart/5000/none/0"
sc.exe failure NSCIM_Mobile reset= 86400 actions= "restart/5000/restart/5000/none/0"
sc.exe failure NSCIM_ImageSplitter reset= 86400 actions= "restart/5000/restart/5000/none/0"

# Set dependency: ImageSplitter depends on API
sc.exe config NSCIM_ImageSplitter depend= NSCIM_API
```

### P4.4 — Start services and verify (on target)

```powershell
Start-Service NSCIM_API
Start-Sleep -Seconds 10
Start-Service NSCIM_WebApp, NSCIM_Mobile, NSCIM_ImageSplitter

Get-Service NSCIM_* | Format-Table Name, Status

# Version check
Invoke-RestMethod -Uri "https://localhost:5300/api/server/version" -SkipCertificateCheck
# Expected: {"version":"3.0.0",...}
```

---

## Phase 5 — Shadow validation (days 10–14)

Run both servers in parallel. Test on new server before cutover.

### P5.1 — Functional tests on target

Connect to `http://10.0.1.254:5299` (or new hostname) and validate:

| Feature | Test | Expected |
|---|---|---|
| Login | SSO/local auth | Success, correct role |
| Container search | Search by container # | Results identical to prod |
| ASE image decode | Open container detail → view image | Image renders, log shows `ASE decoded ... ldt=2` |
| FS6000 pipeline | Recent FS6000 scan | Images display |
| Audit queue | `/audit-review` | Shows assignments, progress correct |
| Audit completion | Approve a test record | Transitions to Completed, ICUMS payload written to `Data\ICUMS\Outbox\` |
| Loose cargo | `/customs/icums/loose-cargo` | Records display (was empty before 2.2.0) |
| Vehicles | `/vehicles` | VIN list displays |
| ICUMS download | Trigger manual download | New files in `Data\ICUMS\Inbox\` |
| Background workers | Check logs | No exceptions, adaptive polling firing |
| Reports | Any report page | Loads |

### P5.2 — Compare decoded images (sample of 10 containers)

```powershell
# From current box
$containers = @('HLBU3195739','TGBU5324522','EITU1223609','MRKU4325944','MRKU3472452')
foreach ($c in $containers) {
    $oldImg = Invoke-WebRequest -Uri "https://NSPORTAL:5300/api/ImageProcessing/container/$c/complete/image" -SkipCertificateCheck -OutFile "Y:\compare\old-$c.jpg"
    $newImg = Invoke-WebRequest -Uri "https://10.0.1.254:5300/api/ImageProcessing/container/$c/complete/image" -SkipCertificateCheck -OutFile "Y:\compare\new-$c.jpg"
}
# Visual diff or checksum compare — identical pixel output expected since same C# decoder
```

### P5.3 — Load test (optional, recommended)

```powershell
# Simulate load against new server to confirm CPU scales as expected
# Use any HTTP load tool (k6, wrk, Artillery)
# Target: 100 concurrent image-detail page loads, measure p95 latency
# Expected: < 500ms (vs current ~2s at load)
```

**Gate:** All P5 tests pass. 24h of shadow operation with zero regressions.

---

## Phase 6 — Cutover (day 14)

### P6.1 — Pre-cutover checklist

- [ ] Schedule maintenance window (off-peak, e.g., Sunday 02:00 local)
- [ ] Announce to users 48h in advance
- [ ] Verify backup of current prod DB dumps still accessible
- [ ] Confirm DNS/reverse proxy config prepared

### P6.2 — T-30 minutes: Final data sync

```powershell
# From current box
# Delta dump (production only, others change rarely)
$env:PGPASSWORD = $env:NICKSCAN_DB_PASSWORD
$pgDump = "C:\Program Files\PostgreSQL\18\bin\pg_dump.exe"
$finalDir = "Y:\db-dumps\final-$(Get-Date -Format 'yyyyMMdd-HHmm')"
New-Item -ItemType Directory -Force -Path $finalDir | Out-Null

# Do hot dumps while services still running
@('nickscan_production','nickscan_downloads','nickscan_icums') | ForEach-Object {
    & $pgDump -h localhost -U postgres -d $_ -F c -b -f "$finalDir\$_.dump"
}
```

### P6.3 — T-0: Stop current services

```powershell
# On current box
sc.exe failure NSCIM_API reset= 0 actions= ""
sc.exe failure NSCIM_WebApp reset= 0 actions= ""
sc.exe failure NSCIM_Mobile reset= 0 actions= ""
sc.exe failure NSCIM_ImageSplitter reset= 0 actions= ""
Stop-Service NSCIM_API, NSCIM_WebApp, NSCIM_Mobile, NSCIM_ImageSplitter -Force
```

### P6.4 — T+5 min: Restore final DBs on target

```powershell
# On target — drop and recreate production DBs, restore from final dump
$env:PGPASSWORD = "<target-pwd>"
$psql = "C:\Program Files\PostgreSQL\18\bin\psql.exe"
$pgRestore = "C:\Program Files\PostgreSQL\18\bin\pg_restore.exe"

# First, stop NSCIM services on target (they'll auto-start when DB is back)
Stop-Service NSCIM_API, NSCIM_WebApp, NSCIM_Mobile, NSCIM_ImageSplitter -Force

# Drop and recreate
@('nickscan_production','nickscan_downloads','nickscan_icums') | ForEach-Object {
    & $psql -h localhost -U postgres -c "DROP DATABASE IF EXISTS $_ WITH (FORCE)"
    & $psql -h localhost -U postgres -c "CREATE DATABASE $_ OWNER postgres"
    & $pgRestore -h localhost -U postgres -d $_ --no-owner --no-privileges "C:\Shared\NSCIM_PRODUCTION\db-dumps\final-<timestamp>\$_.dump"
}
```

### P6.5 — T+15 min: Start target services + verify

```powershell
# On target
Start-Service NSCIM_API
Start-Sleep -Seconds 10
Start-Service NSCIM_WebApp, NSCIM_Mobile, NSCIM_ImageSplitter

Invoke-RestMethod -Uri "https://localhost:5300/api/server/version" -SkipCertificateCheck
# Expected: {"version":"3.0.0",...}

# Check logs for errors
Get-Content "C:\Shared\NSCIM_PRODUCTION\Data\Logs\nickscan-*.txt" -Tail 50
```

### P6.6 — T+20 min: DNS / reverse proxy switch

Depends on your routing setup. Options:
- **DNS CNAME**: Update A/CNAME record pointing `nscim.internal` → new IP
- **Reverse proxy (nginx/HAProxy)**: Update upstream to new server
- **Windows hosts file** (if used): Push updated hosts

### P6.7 — T+30 min: GO/NO-GO decision

Smoke test from outside (real user perspective):
- [ ] Home page loads
- [ ] Login works
- [ ] Container search returns results
- [ ] One image displays correctly
- [ ] Audit queue loads

**GO:** Announce done. Leave current box services stopped (data intact for 30 days).
**NO-GO:** Rollback per P7.

---

## Phase 7 — Rollback (if needed during P6)

### P7.1 — Switch DNS back

Revert DNS/reverse proxy change.

### P7.2 — Restart current services

```powershell
# On current box
sc.exe failure NSCIM_API reset= 86400 actions= "restart/5000/restart/5000/none/0"
sc.exe failure NSCIM_WebApp reset= 86400 actions= "restart/5000/restart/5000/none/0"
sc.exe failure NSCIM_Mobile reset= 86400 actions= "restart/5000/restart/5000/none/0"
sc.exe failure NSCIM_ImageSplitter reset= 86400 actions= "restart/5000/restart/5000/none/0"

Start-Service NSCIM_API
Start-Sleep -Seconds 10
Start-Service NSCIM_WebApp, NSCIM_Mobile, NSCIM_ImageSplitter

Invoke-RestMethod -Uri "https://localhost:5300/api/server/version" -SkipCertificateCheck
# Expected: {"version":"2.8.0",...}
```

### P7.3 — Users back on old prod within 5 minutes

Data may have diverged if any user action happened on target after P6.6 cutover — tolerate (usually minimal, most actions re-do-able) or do reverse DB dump from target if critical.

---

## Phase 8 — Post-cutover (days 14–44, 30-day warm standby)

### P8.1 — Monitoring window

- Keep current box powered on with services stopped
- Daily: check target server's `Data\Logs\nickscan-errors-*.txt`
- Weekly: DB size growth on target
- Monitor: CPU should sit around 5-10% (vs current 60-88%) — confirms performance gains

### P8.2 — Decommission plan

After 14 days with zero critical incidents:
- [ ] Final backup of current box to separate storage
- [ ] Uninstall NSCIM services on current (keep data)
- [ ] After 30 days: wipe or repurpose current box

---

## Appendix A — Known risks

| Risk | Mitigation |
|---|---|
| .NET 10 not yet GA or unstable | Check release notes before day 0; fallback to .NET 9 (STS, EOL May 2026) if needed |
| Npgsql 10 not yet released for Postgres 18 | Pin to latest compatible version; may need Postgres 17 as fallback |
| MudBlazor v9 breaking changes beyond attributes | Page-by-page visual regression check in P5.1 |
| Z:\ image share unreachable from target | Pre-flight in P2.5; involve network team if firewall issue |
| Target Postgres data dir fills C: (247 GB free, ~19 GB DB) | Monitor; plan secondary drive if image history grows |
| Background services fail to re-ingest after cutover | Check `ICUMSDownloadQueueService` logs; may need manual re-queue |
| Clock skew between servers causing auth issues | Verify NTP sync on both boxes before P5 |

## Appendix B — Commands quick reference (on current box)

```powershell
# Watch Y: transfer progress
while ($true) { (Get-PSDrive Y).Used / 1GB; Start-Sleep 5 }

# Verify target services from here (if firewall allows)
Test-NetConnection 10.0.1.254 -Port 5300

# Tail current logs
Get-Content "C:\Shared\NSCIM_PRODUCTION\Data\Logs\nickscan-$(Get-Date -Format yyyyMMdd).txt" -Tail 50 -Wait
```

## Appendix C — Timeline summary

| Day | Phase | Duration | Who | Blocker if skipped |
|---|---|---|---|---|
| 1 | P0 verification | 1h | Ops + Me | Everything |
| 1-5 | P1 code upgrade | 5 days | Me | Target binary not ready |
| 3-7 | P2 target prep | 5 days | Ops | Target can't run binary |
| 7 | P3 data transfer | 1 day | Me + Ops | Target has no data |
| 8-10 | P4 deploy | 3 days | Me | Target not running |
| 10-13 | P5 shadow validation | 3 days | QA + Me | Unknown regressions at cutover |
| 14 | P6 cutover | 1h window | All | — |
| 14-44 | P8 warm standby | 30 days | Ops monitoring | — |

**Total: 14 days from start to cutover. 44 days to full decommission of old box.**

---

## Sign-offs required before P6 cutover

- [ ] All P1 compile errors fixed, tests green
- [ ] P2 target server checklist complete
- [ ] P3 row counts match (current vs target)
- [ ] P4 target running 3.0.0, version endpoint confirmed
- [ ] P5 functional tests all green (24h minimum soak)
- [ ] P6 maintenance window scheduled, users notified
- [ ] P7 rollback path validated (dry-run once)
- [ ] Backup of current DBs in separate location (not just Y:\)

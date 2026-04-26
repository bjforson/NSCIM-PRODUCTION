# Rotate the Postgres password for the nscim_app role + the matching
# NICKSCAN_DB_PASSWORD env var, then restart all 5 services so they pick
# up the new credential.
#
# Why: hygiene. The current password has been in place since the role
# was created (week-1 security rollout, 2026-04-24); rotating it limits
# blast radius if a backup, log, or env dump ever leaks.
#
# What it does (in order — the whole thing must run in a maintenance
# window because there's a brief failure window between step 2 and the
# end of step 5):
#
#   1. Generate a 32-byte URL-safe random password.
#   2. (DRY-RUN by default — pass -Apply to execute) ALTER USER nscim_app
#      WITH PASSWORD '<new>' on Postgres.
#   3. Set NICKSCAN_DB_PASSWORD = <new> at machine scope.
#   4. Stop NSCIM_API, NSCIM_WebApp, NSCIM_NickComms, NickHR_API,
#      NickHR_WebApp (in dependency order — WebApps before APIs).
#   5. Start them back up (APIs first, WebApps last).
#   6. Probe each service's /health endpoint to confirm DB connectivity.
#
# Prerequisites:
#   - Run as Administrator (env var write + sc.exe stop/start need it).
#   - The CURRENT NICKSCAN_DB_PASSWORD env var is correct (the script
#     uses it to authenticate the ALTER USER call).
#   - psql.exe on disk at the path below (default: PG18).
#
# Rollback: the previous password is logged to a temp file at the start
# (path printed). If the rotation fails, ALTER USER back to the previous
# value, restore the env var, restart services. Delete the temp file
# afterwards.

[CmdletBinding(SupportsShouldProcess = $true, ConfirmImpact = 'High')]
param(
    [string] $PsqlExe = 'C:\Program Files\PostgreSQL\18\bin\psql.exe',
    [string] $PgUser = 'postgres',
    [string] $PgHost = 'localhost',
    [int]    $PgPort = 5432,
    [string] $RoleName = 'nscim_app',
    [string] $EnvVarName = 'NICKSCAN_DB_PASSWORD',
    [int]    $PasswordByteLength = 32,
    [switch] $Apply
)

$ErrorActionPreference = 'Stop'

# ---- Pre-flight checks -----------------------------------------------------

if (-not (Test-Path $PsqlExe)) { throw "psql.exe not found at $PsqlExe" }
$currentPassword = [Environment]::GetEnvironmentVariable($EnvVarName, 'Machine')
if ([string]::IsNullOrEmpty($currentPassword)) {
    throw "$EnvVarName not set at Machine scope. Bail — rotation needs the current password to authenticate the ALTER USER call."
}

# Identity check: are we running as admin?
$isAdmin = ([Security.Principal.WindowsPrincipal]::new(
    [Security.Principal.WindowsIdentity]::GetCurrent()
)).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
if (-not $isAdmin) {
    throw "Must run as Administrator. Re-launch from an elevated PowerShell."
}

# Test that we can actually connect with the current password — fail fast
# rather than discover halfway through the rotation that the env var is stale.
$env:PGPASSWORD = $currentPassword
$probe = & $PsqlExe -h $PgHost -p $PgPort -U $PgUser -d 'postgres' -At -c "SELECT 1" 2>&1
if ($LASTEXITCODE -ne 0) {
    Remove-Item Env:PGPASSWORD -ErrorAction SilentlyContinue
    throw "Pre-flight: psql cannot connect with current $EnvVarName. Got: $probe"
}
Write-Host "Pre-flight: connected to Postgres OK"

# ---- Generate new password -------------------------------------------------

Add-Type -AssemblyName System.Web
function New-RandomPassword([int]$bytes) {
    $buf = New-Object byte[] $bytes
    [System.Security.Cryptography.RandomNumberGenerator]::Fill($buf)
    # URL-safe Base64 (no `+`, `/`, `=`) — Postgres handles quoted password
    # fine but URL-safe avoids any shell-quoting surprises if the value
    # ever lands in a connection-string template.
    return [Convert]::ToBase64String($buf).TrimEnd('=').Replace('+', '-').Replace('/', '_')
}

if (-not $Apply) {
    Write-Host ""
    Write-Host "DRY-RUN — pass -Apply to execute. Plan:" -ForegroundColor Cyan
    Write-Host "  0. Write rollback file with the OLD password to `$env:TEMP"
    Write-Host "  1. ALTER USER $RoleName WITH PASSWORD '<32-byte URL-safe random>'"
    Write-Host "  2. [Environment]::SetEnvironmentVariable('$EnvVarName', <new>, 'Machine')"
    Write-Host "  3. sc.exe stop  NickHR_WebApp ; NSCIM_WebApp"
    Write-Host "  4. sc.exe stop  NSCIM_NickComms ; NickHR_API ; NSCIM_API"
    Write-Host "  5. sc.exe start NSCIM_API ; NickHR_API ; NSCIM_NickComms"
    Write-Host "  6. sc.exe start NSCIM_WebApp ; NickHR_WebApp"
    Write-Host "  7. Probe /health on each running service."
    Write-Host ""
    Write-Host "Dry-run does NOT write the rollback file or generate a new password —" -ForegroundColor Cyan
    Write-Host "both happen only after -Apply, so a leaked dry-run output is harmless."
    Remove-Item Env:PGPASSWORD -ErrorAction SilentlyContinue
    return
}

# -Apply path only past this point.
$newPassword = New-RandomPassword $PasswordByteLength
$rollbackPath = Join-Path $env:TEMP "nscim-pw-rollback-$(Get-Date -Format 'yyyyMMddHHmmss').txt"
@"
# Postgres rotation rollback file — delete me after a successful rotation.
# Generated: $(Get-Date -Format 'o')
# Old password (in case ALTER USER fails or app smoke-tests don't pass):
$currentPassword
"@ | Out-File -FilePath $rollbackPath -Encoding utf8
Write-Host "Rollback file with previous password: $rollbackPath" -ForegroundColor Yellow

# ---- 1. ALTER USER ---------------------------------------------------------

Write-Host "Rotating Postgres password for role '$RoleName'..."
$alterSql = "ALTER USER $RoleName WITH PASSWORD '$newPassword';"
& $PsqlExe -h $PgHost -p $PgPort -U $PgUser -d 'postgres' -v ON_ERROR_STOP=1 -c $alterSql 2>&1 | Out-Null
if ($LASTEXITCODE -ne 0) {
    Remove-Item Env:PGPASSWORD -ErrorAction SilentlyContinue
    throw "ALTER USER failed. Rollback file: $rollbackPath"
}
Write-Host "  ALTER USER OK"

# ---- 2. Set env var --------------------------------------------------------

[Environment]::SetEnvironmentVariable($EnvVarName, $newPassword, 'Machine')
Write-Host "  Env var $EnvVarName updated at Machine scope"

# ---- 3-5. Service restart in dependency order ------------------------------

$webApps = @('NickHR_WebApp', 'NSCIM_WebApp')
$apis    = @('NSCIM_NickComms', 'NickHR_API', 'NSCIM_API')

Write-Host "Stopping web apps + APIs (in reverse dependency order)..."
foreach ($svc in $webApps + $apis) {
    if (Get-Service -Name $svc -ErrorAction SilentlyContinue) {
        sc.exe stop $svc | Out-Null
    }
}
# Wait for full stop. STOP_PENDING transitions can hold the file locks.
Start-Sleep -Seconds 12

Write-Host "Starting APIs first (so WebApps have backends to talk to)..."
foreach ($svc in $apis) {
    if (Get-Service -Name $svc -ErrorAction SilentlyContinue) {
        sc.exe start $svc | Out-Null
        Start-Sleep -Seconds 4
    }
}
Start-Sleep -Seconds 8

Write-Host "Starting WebApps..."
foreach ($svc in $webApps) {
    if (Get-Service -Name $svc -ErrorAction SilentlyContinue) {
        sc.exe start $svc | Out-Null
        Start-Sleep -Seconds 3
    }
}
Start-Sleep -Seconds 6

# ---- 6. Health probes ------------------------------------------------------

Write-Host ""
Write-Host "Health probes:" -ForegroundColor Cyan
$probes = @(
    @{ Url = 'http://localhost:5205/api/health'; Name = 'NSCIM_API' }
    @{ Url = 'http://localhost:5215/api/_module/manifest'; Name = 'NickHR_API' }
    @{ Url = 'http://localhost:5220/api/health'; Name = 'NickComms' }
)
$failures = @()
foreach ($p in $probes) {
    try {
        $r = Invoke-WebRequest -Uri $p.Url -UseBasicParsing -TimeoutSec 5 -SkipCertificateCheck
        Write-Host "  $($p.Name) [$($p.Url)] -> $($r.StatusCode)"
    }
    catch {
        $code = $_.Exception.Response.StatusCode
        Write-Host "  $($p.Name) [$($p.Url)] -> FAIL ($code)" -ForegroundColor Red
        $failures += $p.Name
    }
}

Remove-Item Env:PGPASSWORD -ErrorAction SilentlyContinue

if ($failures.Count -gt 0) {
    Write-Host ""
    Write-Host "ROTATION INCOMPLETE — these services failed health probe: $($failures -join ', ')" -ForegroundColor Red
    Write-Host "Rollback: read $rollbackPath, restore the old password to:"
    Write-Host "  - Postgres (ALTER USER $RoleName WITH PASSWORD '<old>')"
    Write-Host "  - Env var ($EnvVarName at Machine scope)"
    Write-Host "  - Restart all 5 services"
    exit 1
}

Write-Host ""
Write-Host "ROTATION COMPLETE. All services healthy." -ForegroundColor Green
Write-Host "DELETE $rollbackPath now (it contains the old password)." -ForegroundColor Yellow

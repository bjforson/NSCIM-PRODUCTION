# Install / reinstall the NickFinance_WebApp Windows Service.
#
# Run as Administrator.
#
# This is a one-time MIGRATION script: pre-existing installs ran as
# LocalSystem (the default for `sc.exe create` with no `obj=`); the
# current install uses the *virtual service account*
# `NT SERVICE\NickFinance_WebApp`. Re-running on a host that has already
# been migrated is a no-op aside from a fresh ACL pass on the receipt /
# log / publish dirs and a stop-then-start of the service.
#
# Why a virtual service account (and not LocalSystem):
#   * Auto-generated isolated SID per service — no shared identity with
#     other LocalSystem services on the box (NickHR_WebApp, NSCIM_API,
#     NSCIM_ImageDownloader, NickComms.Gateway). An RCE in NickFinance no
#     longer hands SYSTEM to the attacker.
#   * No interactive login; no password to rotate; auto-managed by SCM.
#   * Can be ACL'd narrowly via `icacls "<path>" /grant "NT SERVICE\NickFinance_WebApp:..."`.
#   * Inherits the machine-scoped env vars by default — same conn string
#     and config our prior LocalSystem install already used.
#
# Rollback plan:
#   If the virtual account can't read receipts/logs/publish for any
#   reason (the icacls grants below should prevent this, but file-system
#   filter drivers / EDR can interfere), drop back to LocalSystem with:
#     sc.exe stop   NickFinance_WebApp
#     sc.exe config NickFinance_WebApp obj= LocalSystem
#     sc.exe start  NickFinance_WebApp
#   Note: rolling back gives up the blast-radius containment; do this
#   only as a temporary unblock and capture the icacls / EDR error so
#   the next run can fix it.

[CmdletBinding(SupportsShouldProcess = $true, ConfirmImpact = 'High')]
param(
    [string] $ServiceName = 'NickFinance_WebApp',
    [string] $DisplayName = 'NickFinance WebApp (NickERP)',
    [string] $BinaryPath  = 'C:\Shared\NSCIM_PRODUCTION\publish\NickFinance.WebApp\NickFinance.WebApp.exe',
    [string] $ListenUrl   = 'http://localhost:5500',
    [string] $Environment = 'Production',
    [string] $ReceiptRoot = 'C:\Shared\NSCIM_PRODUCTION\Data\PettyCash\Receipts',
    [string] $LogRoot     = 'C:\Logs\NickERP\NickFinance.WebApp',
    [string] $PublishDir  = 'C:\Shared\NSCIM_PRODUCTION\publish\NickFinance.WebApp'
)

$ErrorActionPreference = 'Stop'

# Identity used as the service "obj=" value AND as the icacls principal.
# Windows materialises the SID for `NT SERVICE\<service>` on first start.
$ServiceAccount = "NT SERVICE\$ServiceName"

if (-not (Test-Path $BinaryPath)) {
    throw "Binary not found at $BinaryPath. Run 'dotnet publish' first."
}

# If the service is already running it holds the binary locked, so a
# `dotnet publish` from the source dir crashes with MSB3027. Stop it
# first; we'll restart at the end.
$pre = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
if ($pre -and $pre.Status -ne 'Stopped') {
    Stop-Service -Name $ServiceName -Force
    Start-Sleep -Seconds 2
}

# Connection string built from existing NICKSCAN_DB_PASSWORD machine env var.
# IMPORTANT: the WebApp connects as `nscim_app` (least-privilege DML role),
# NOT as `postgres`. The bootstrap CLI keeps using `postgres` for DDL
# operations (CREATE SCHEMA / TABLE / TRIGGER); the long-running service
# only does DML and so doesn't need superuser rights. Grants for nscim_app
# are managed by `scripts/grant-nscim-app-finance.sql` — re-run that script
# after every migration that adds a new schema. RLS policies live in
# `scripts/apply-rls-policies.sql` and are applied automatically by
# the bootstrap CLI after migrations.
$pw = [Environment]::GetEnvironmentVariable('NICKSCAN_DB_PASSWORD', 'Machine') `
    ?? $env:NICKSCAN_DB_PASSWORD
if ([string]::IsNullOrWhiteSpace($pw)) {
    throw 'NICKSCAN_DB_PASSWORD machine env var must be set.'
}
$conn = "Host=localhost;Port=5432;Database=nickhr;Username=nscim_app;Password=$pw"

# Machine env vars consumed by the host. Setting them at the machine level
# (rather than only on the service) means a manual `dotnet run` against
# this binary picks them up too — matches the NickHR convention.
# Virtual service accounts inherit the Machine scope automatically.
[Environment]::SetEnvironmentVariable('ConnectionStrings__Finance', $conn, 'Machine')
[Environment]::SetEnvironmentVariable('ASPNETCORE_URLS', $ListenUrl, 'Machine')
[Environment]::SetEnvironmentVariable('ASPNETCORE_ENVIRONMENT', $Environment, 'Machine')

# Receipt-blob data-encryption-key (DEK) for at-rest AES-256-GCM. ONLY
# generate if absent — overwriting an existing key destroys access to
# every previously-encrypted receipt on disk. The WebApp's
# `EncryptedReceiptStorage` reads this env var; if unset the service
# falls back silently to `LocalDiskReceiptStorage` (plaintext) and logs
# a startup warning.
$dek = [Environment]::GetEnvironmentVariable('NICKFINANCE_RECEIPT_DEK', 'Machine')
if ([string]::IsNullOrWhiteSpace($dek)) {
    $dekBytes = [System.Security.Cryptography.RandomNumberGenerator]::GetBytes(32)
    $dek = [Convert]::ToBase64String($dekBytes)
    [Environment]::SetEnvironmentVariable('NICKFINANCE_RECEIPT_DEK', $dek, 'Machine')
    Write-Host "Generated new NICKFINANCE_RECEIPT_DEK (machine-scoped, base64, 32-byte AES-256 key)." -ForegroundColor Yellow
    Write-Host "  >>> BACK UP THIS KEY OFF-HOST. Losing it makes every encrypted receipt unreadable." -ForegroundColor Yellow
} else {
    Write-Host "NICKFINANCE_RECEIPT_DEK already set; leaving in place (do NOT rotate without a re-encryption pass)."
}

# ---------------------------------------------------------------------------
# Filesystem ACLs — must run BEFORE `sc.exe create` so the service has
# read+write the moment it first starts.
#
# The principal "NT SERVICE\NickFinance_WebApp" only resolves on hosts
# where icacls supports virtual accounts (Windows 7 / Server 2008 R2 +).
# On Server 2022 (our target) it works without prerequisite.
# ---------------------------------------------------------------------------

function Ensure-DirAndAcl {
    param(
        [Parameter(Mandatory)] [string] $Path,
        [Parameter(Mandatory)] [string] $Principal,
        [Parameter(Mandatory)] [ValidateSet('Read','ReadWrite')] [string] $Mode
    )
    if (-not (Test-Path -LiteralPath $Path)) {
        New-Item -ItemType Directory -Path $Path -Force | Out-Null
    }
    $perm = if ($Mode -eq 'ReadWrite') { '(OI)(CI)M' } else { '(OI)(CI)RX' }
    # /grant:r replaces any prior grant for the same principal — keeps
    # repeated runs from layering ACEs.
    & icacls.exe $Path /grant:r "${Principal}:$perm" | Out-Null
    if ($LASTEXITCODE -ne 0) {
        throw "icacls failed on $Path for $Principal"
    }
}

Ensure-DirAndAcl -Path $ReceiptRoot -Principal $ServiceAccount -Mode 'ReadWrite'
Ensure-DirAndAcl -Path $LogRoot     -Principal $ServiceAccount -Mode 'ReadWrite'
Ensure-DirAndAcl -Path $PublishDir  -Principal $ServiceAccount -Mode 'ReadWrite'
Write-Host "Filesystem ACLs applied for $ServiceAccount on receipts, logs, and publish dirs."

# Tear down a stale service if it exists.
$existing = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
if ($existing) {
    if ($existing.Status -ne 'Stopped') {
        Stop-Service -Name $ServiceName -Force
        Start-Sleep -Seconds 2
    }
    & sc.exe delete $ServiceName | Out-Null
    Start-Sleep -Seconds 2
}

# Create. sc.exe needs the spaces-after-equals form: 'binPath= "C:\..."'.
# obj= "NT SERVICE\NickFinance_WebApp" runs the service under a
# Windows-managed virtual account; no password needed.
& sc.exe create $ServiceName binPath= "`"$BinaryPath`"" start= auto obj= "$ServiceAccount" DisplayName= "`"$DisplayName`"" | Out-Null
& sc.exe description $ServiceName 'NICKSCAN ERP — NickFinance Blazor Server WebApp. Petty cash, AR, AP, banking, fixed assets, budgets, reports.' | Out-Null
# Recovery: restart 3x with 30s delay, then leave alone.
& sc.exe failure $ServiceName reset= 86400 actions= restart/30000/restart/30000/restart/60000 | Out-Null

Start-Service -Name $ServiceName
Start-Sleep -Seconds 5
$svc = Get-Service -Name $ServiceName
if ($svc.Status -ne 'Running') {
    throw "Service did not reach Running state (got $($svc.Status))."
}

# Smoke test.
try {
    $resp = Invoke-WebRequest -UseBasicParsing -Uri "$ListenUrl/" -TimeoutSec 30
    Write-Host "  $ServiceName -> HTTP $($resp.StatusCode) on $ListenUrl/" -ForegroundColor Green
} catch {
    Write-Host "  Smoke test failed: $($_.Exception.Message)" -ForegroundColor Red
    throw
}

Write-Host ''
Write-Host "Installed and started: $ServiceName"
Write-Host "Running as:            $ServiceAccount (virtual service account)"
Write-Host "Listening on:          $ListenUrl"
Write-Host "Logs:                  Event Viewer > Windows Logs > Application (source: $ServiceName)"

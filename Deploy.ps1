# ============================================================
# NSCIM Production Deployment Script
# ============================================================
# Publishes API and WebApp to the canonical publish directory,
# restarts the Windows services, and verifies the deployment.
#
# CANONICAL PATHS (never change these without updating the
# Windows service binPath via nssm set <service> Application):
#   - API:    C:\Shared\NSCIM_PRODUCTION\publish\API
#   - WebApp: C:\Shared\NSCIM_PRODUCTION\publish\WebApp
#
# Usage:
#   .\Deploy.ps1                    # Full deploy
#   .\Deploy.ps1 -WebAppOnly        # Only WebApp
#   .\Deploy.ps1 -ApiOnly           # Only API
#   .\Deploy.ps1 -SkipBuild         # Just restart services
#   .\Deploy.ps1 -DryRun            # Show plan without doing it
#
# Run from repo root: C:\Shared\NSCIM_PRODUCTION
# ============================================================

param(
    [switch]$WebAppOnly,
    [switch]$ApiOnly,
    [switch]$SkipBuild,
    [switch]$DryRun
)

$ErrorActionPreference = "Stop"
$ScriptRoot = $PSScriptRoot

# --- Canonical paths (match the Windows service binPaths) ---
$PUBLISH_ROOT = "C:\Shared\NSCIM_PRODUCTION\publish"
$API_PUBLISH = Join-Path $PUBLISH_ROOT "API"
$WEBAPP_PUBLISH = Join-Path $PUBLISH_ROOT "WebApp"

$API_CSPROJ = Join-Path $ScriptRoot "src\NickScanCentralImagingPortal.API\NickScanCentralImagingPortal.API.csproj"
$WEBAPP_CSPROJ = Join-Path $ScriptRoot "src\NickScanWebApp.New\NickScanWebApp.New.csproj"

# --- Windows services (in dependency order) ---
$SERVICE_API = "NSCIM_API"
$SERVICE_WEBAPP = "NSCIM_WebApp"
$SERVICE_ENGINE = "NSCIM_ImageSplitter"

function Write-Header($text) {
    Write-Host ""
    Write-Host "----------------------------------------------------" -ForegroundColor Cyan
    Write-Host " $text" -ForegroundColor Cyan
    Write-Host "----------------------------------------------------" -ForegroundColor Cyan
}

function Write-Step($text) {
    Write-Host ">>> $text" -ForegroundColor Yellow
}

function Write-OK($text) {
    Write-Host "    [OK] $text" -ForegroundColor Green
}

function Write-Fail($text) {
    Write-Host "    [FAIL] $text" -ForegroundColor Red
}

function Stop-SvcIfRunning($name) {
    $svc = Get-Service -Name $name -ErrorAction SilentlyContinue
    if ($null -eq $svc) {
        Write-Host "    Service $name not installed, skipping" -ForegroundColor Gray
        return
    }
    if ($svc.Status -eq 'Running') {
        if ($DryRun) {
            Write-Host "    [DryRun] Would stop $name" -ForegroundColor Magenta
        } else {
            Write-Step "Stopping $name..."
            Stop-Service -Name $name -Force
            Start-Sleep -Seconds 2
            Write-OK "Stopped $name"
        }
    } else {
        Write-Host "    $name already stopped" -ForegroundColor Gray
    }
}

function Start-Svc($name) {
    $svc = Get-Service -Name $name -ErrorAction SilentlyContinue
    if ($null -eq $svc) {
        Write-Host "    Service $name not installed, skipping" -ForegroundColor Gray
        return
    }
    if ($DryRun) {
        Write-Host "    [DryRun] Would start $name" -ForegroundColor Magenta
        return
    }
    Write-Step "Starting $name..."
    Start-Service -Name $name
    Start-Sleep -Seconds 3
    $svc.Refresh()
    if ($svc.Status -eq 'Running') {
        Write-OK "Started $name"
    } else {
        Write-Fail "$name failed to start (status: $($svc.Status))"
        throw "Service start failed: $name"
    }
}

function Publish-Project($name, $csproj, $target) {
    if ($SkipBuild) {
        Write-Host "    [SkipBuild] Skipping publish of $name" -ForegroundColor Gray
        return
    }
    if ($DryRun) {
        Write-Host "    [DryRun] Would publish $name to $target" -ForegroundColor Magenta
        return
    }
    Write-Step "Publishing $name to $target..."
    $output = & dotnet publish $csproj -c Release -o $target 2>&1
    if ($LASTEXITCODE -ne 0) {
        $output | ForEach-Object { Write-Host "    $_" -ForegroundColor Red }
        throw "Publish failed for $name"
    }
    Write-OK "Published $name"
}

function Test-DeploymentBinary($name, $dllPath) {
    if ($DryRun) { return }
    if (-not (Test-Path $dllPath)) {
        Write-Fail "$name DLL missing at $dllPath"
        throw "Deployment verification failed"
    }
    $dll = Get-Item $dllPath
    $age = (Get-Date) - $dll.LastWriteTime
    if ($age.TotalMinutes -gt 10) {
        Write-Fail "$name DLL is $([int]$age.TotalMinutes) minutes old - did publish go to the wrong location?"
        throw "Deployment verification failed"
    }
    Write-OK "$name DLL verified ($([int]$age.TotalSeconds)s old)"
}

function Test-ProcessPath($serviceName, $expectedPath) {
    if ($DryRun) { return }
    $svc = Get-WmiObject -Class Win32_Service -Filter "Name='$serviceName'" -ErrorAction SilentlyContinue
    if ($null -eq $svc) { return }
    $exeName = $svc.PathName.Trim('"').Split()[0]
    $procName = [System.IO.Path]::GetFileNameWithoutExtension($exeName)
    $proc = Get-Process -Name $procName -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($null -ne $proc -and $proc.Path) {
        $procDir = Split-Path $proc.Path -Parent
        if ($procDir -ne $expectedPath) {
            Write-Fail "$serviceName is running from $procDir but expected $expectedPath"
            throw "Service running from unexpected location"
        }
        Write-OK "$serviceName process path verified"
    }
}

# --- MAIN ---

Write-Header "NSCIM Production Deployment"
Write-Host "Repo root:     $ScriptRoot"
Write-Host "Publish root:  $PUBLISH_ROOT"
$mode = "Full deploy"
if ($DryRun) { $mode = "DRY RUN" }
elseif ($SkipBuild) { $mode = "Skip build" }
elseif ($WebAppOnly) { $mode = "WebApp only" }
elseif ($ApiOnly) { $mode = "API only" }
Write-Host "Mode:          $mode"
Write-Host ""

if (-not (Test-Path $API_CSPROJ)) {
    Write-Fail "Cannot find $API_CSPROJ - are you running from the repo root?"
    exit 1
}

# --- Phase 1: Stop services ---
Write-Header "Phase 1: Stop services"
if ($WebAppOnly) {
    Stop-SvcIfRunning $SERVICE_WEBAPP
} else {
    Stop-SvcIfRunning $SERVICE_ENGINE
    Stop-SvcIfRunning $SERVICE_WEBAPP
    Stop-SvcIfRunning $SERVICE_API
}

# --- Phase 2: Publish ---
Write-Header "Phase 2: Publish"
if ($ApiOnly -or (-not $WebAppOnly)) {
    Publish-Project "API" $API_CSPROJ $API_PUBLISH
}
if ($WebAppOnly -or (-not $ApiOnly)) {
    Publish-Project "WebApp" $WEBAPP_CSPROJ $WEBAPP_PUBLISH
}

# --- Phase 3: Verify binaries ---
Write-Header "Phase 3: Verify binaries"
if ($ApiOnly -or (-not $WebAppOnly)) {
    Test-DeploymentBinary "API" (Join-Path $API_PUBLISH "NickScanCentralImagingPortal.API.dll")
}
if ($WebAppOnly -or (-not $ApiOnly)) {
    Test-DeploymentBinary "WebApp" (Join-Path $WEBAPP_PUBLISH "NickScanWebApp.New.dll")
}

# --- Phase 4: Start services ---
Write-Header "Phase 4: Start services"
if ($WebAppOnly) {
    Start-Svc $SERVICE_WEBAPP
} else {
    Start-Svc $SERVICE_API
    Start-Svc $SERVICE_WEBAPP
    Start-Svc $SERVICE_ENGINE
}

# --- Phase 5: Verify running process paths ---
Write-Header "Phase 5: Verify running processes"
Start-Sleep -Seconds 2
if ($ApiOnly -or (-not $WebAppOnly)) {
    Test-ProcessPath $SERVICE_API $API_PUBLISH
}
if ($WebAppOnly -or (-not $ApiOnly)) {
    Test-ProcessPath $SERVICE_WEBAPP $WEBAPP_PUBLISH
}

Write-Header "Deployment complete"
Write-Host " Reload the browser to see UI changes." -ForegroundColor Green
Write-Host ""

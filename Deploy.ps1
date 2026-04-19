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

function Set-RuntimeConfigRollForward($target) {
    # Patch every *.runtimeconfig.json under $target to include
    #   "rollForward": "latestMajor"
    # so assemblies built against net8.0 keep running under net10+. `dotnet publish`
    # rewrites these files on every publish, so we re-apply here as a post-publish step.
    # Originally lived as Deploy-ERP-Target.ps1 step 8.5 on the deploy target; baked in
    # here 2026-04-19 so a cold source-driven deploy does not regress.
    if ($DryRun) {
        Write-Host "    [DryRun] Would set rollForward=latestMajor on runtimeconfig.json files under $target" -ForegroundColor Magenta
        return
    }
    if (-not (Test-Path $target)) { return }
    $configs = Get-ChildItem -Path $target -Filter "*.runtimeconfig.json" -Recurse -ErrorAction SilentlyContinue
    if (-not $configs) {
        Write-Host "    No runtimeconfig.json files under $target - skipping rollForward patch" -ForegroundColor Gray
        return
    }
    $patched = 0
    foreach ($cfg in $configs) {
        try {
            $raw = Get-Content -LiteralPath $cfg.FullName -Raw
            $json = $raw | ConvertFrom-Json
            if (-not $json.runtimeOptions) { continue }
            if ($json.runtimeOptions.rollForward -eq 'latestMajor') { continue }
            $json.runtimeOptions | Add-Member -NotePropertyName rollForward -NotePropertyValue 'latestMajor' -Force
            ($json | ConvertTo-Json -Depth 20) | Set-Content -LiteralPath $cfg.FullName -Encoding UTF8
            $patched++
        } catch {
            Write-Host "    Warn: could not patch $($cfg.FullName): $_" -ForegroundColor DarkYellow
        }
    }
    if ($patched -gt 0) { Write-OK "Patched rollForward=latestMajor in $patched runtimeconfig.json file(s)" }
}

function Set-NssmEnvPair($service, $name, $value) {
    # Idempotently set an env var on an NSSM-hosted service via AppEnvironmentExtra.
    # Why this exists: LocalSystem services cannot read user-mapped drives (e.g. Z:\),
    # so the FS6000 UNC path has to be handed to the ImageSplitter through an env var
    # rather than a drive letter. Baked into Deploy.ps1 2026-04-19.
    $svc = Get-Service -Name $service -ErrorAction SilentlyContinue
    if ($null -eq $svc) {
        Write-Host "    Service $service not installed, skipping NSSM env $name" -ForegroundColor Gray
        return
    }
    $nssm = Join-Path $ScriptRoot "tools\nssm-2.24\win64\nssm.exe"
    if (-not (Test-Path $nssm)) {
        Write-Host "    NSSM not found at $nssm, skipping NSSM env $name on $service" -ForegroundColor Gray
        return
    }
    if ($DryRun) {
        Write-Host "    [DryRun] Would set NSSM env ${name}=${value} on $service" -ForegroundColor Magenta
        return
    }
    try {
        $existing = & $nssm get $service AppEnvironmentExtra 2>$null
    } catch {
        $existing = ""
    }
    $pair = "${name}=${value}"
    # If the exact pair is already set, nothing to do
    if ($existing -and ($existing -split "[\r\n]") -contains $pair) {
        Write-Host "    NSSM env $name already set on $service" -ForegroundColor Gray
        return
    }
    # Preserve other entries, replace any existing entry for the same name
    $lines = @()
    if ($existing) {
        $lines = $existing -split "[\r\n]" | Where-Object { $_ -and ($_ -notmatch "^$([regex]::Escape($name))=") }
    }
    $lines += $pair
    & $nssm set $service AppEnvironmentExtra ($lines -join "`n") | Out-Null
    Write-OK "Set NSSM env $name on $service"
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

# --- Phase 3.5: Post-publish config (baked in 2026-04-19 from operator hot-patches) ---
#   * rollForward=latestMajor so net8-built assemblies run on net10 host runtime
#   * NSSM env var hands the ImageSplitter the FS6000 UNC path (LocalSystem cannot
#     use the user-mapped Z:\ drive that the operator sees)
# Runs on every deploy — including -SkipBuild — because the helpers are idempotent
# and the publish/ dir may have been refreshed by a hand-copy before the operator
# ran Deploy.ps1 -SkipBuild to cycle services.
Write-Header "Phase 3.5: Post-publish config"
if ($ApiOnly -or (-not $WebAppOnly)) {
    Set-RuntimeConfigRollForward $API_PUBLISH
}
if ($WebAppOnly -or (-not $ApiOnly)) {
    Set-RuntimeConfigRollForward $WEBAPP_PUBLISH
}
if (-not $WebAppOnly) {
    Set-NssmEnvPair $SERVICE_ENGINE "NICKSCAN_FS6000_SHARE" "\\172.16.1.1\Image\23301FS01"
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

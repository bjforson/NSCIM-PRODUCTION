# ============================================================
# NSCIM Production Deployment Script
# ============================================================
# Publishes one or more NickERP services to the canonical publish
# directory, restarts the Windows services, and verifies the
# deployment.
#
# CANONICAL PATHS (never change these without updating the
# Windows service binPath via nssm set <service> Application):
#   - NSCIM_API:         C:\Shared\NSCIM_PRODUCTION\publish\API
#   - NSCIM_WebApp:      C:\Shared\NSCIM_PRODUCTION\publish\WebApp
#   - NSCIM_NickComms:   C:\Shared\NSCIM_PRODUCTION\publish\NickComms
#   - NSCIM_Portal:      C:\Shared\NSCIM_PRODUCTION\publish\Portal
#   - NickHR_API:        C:\Shared\NSCIM_PRODUCTION\NickHR\deploy\api
#   - NickHR_WebApp:     C:\Shared\NSCIM_PRODUCTION\NickHR\deploy\webapp
#   - NickFinance_WebApp: C:\Shared\NSCIM_PRODUCTION\publish\NickFinance.WebApp
#
# Usage:
#   .\Deploy.ps1                    # Default: API + WebApp (back-compat)
#   .\Deploy.ps1 -Full              # All 6 (NSCIM_*) + NickHR + NickFinance
#   .\Deploy.ps1 -ApiOnly           # Only NSCIM_API
#   .\Deploy.ps1 -WebAppOnly        # Only NSCIM_WebApp
#   .\Deploy.ps1 -NickCommsOnly     # Only NSCIM_NickComms
#   .\Deploy.ps1 -PortalOnly        # Only NSCIM_Portal
#   .\Deploy.ps1 -NickHROnly        # NickHR_API + NickHR_WebApp
#   .\Deploy.ps1 -NickFinanceOnly   # Only NickFinance_WebApp
#   .\Deploy.ps1 -SkipBuild         # Just restart services
#   .\Deploy.ps1 -DryRun            # Show plan without doing it
#
# Flags can combine, e.g.:
#   .\Deploy.ps1 -Full -DryRun
#   .\Deploy.ps1 -Full -SkipBuild
#
# Run from repo root: C:\Shared\NSCIM_PRODUCTION
# ============================================================

param(
    [switch]$WebAppOnly,
    [switch]$ApiOnly,
    [switch]$NickCommsOnly,
    [switch]$PortalOnly,
    [switch]$NickHROnly,
    [switch]$NickFinanceOnly,
    [switch]$Full,
    [switch]$SkipBuild,
    [switch]$DryRun
)

$ErrorActionPreference = "Stop"
$ScriptRoot = $PSScriptRoot

# --- Canonical paths (match the Windows service binPaths) ---
$PUBLISH_ROOT = "C:\Shared\NSCIM_PRODUCTION\publish"
$NICKHR_DEPLOY_ROOT = "C:\Shared\NSCIM_PRODUCTION\NickHR\deploy"

# --- Service catalogue -------------------------------------------------------
# A single source of truth for every supervised .NET service this script can
# deploy. Each entry is a hashtable consumed by the Phase 1..5 helpers below.
#   Key          Service name (Get-Service / nssm)
#   Csproj       Absolute path to the project to `dotnet publish`
#   Publish      Absolute target directory (must match the service binPath)
#   Dll          Filename of the published entry-point DLL (for verification)
#   Tag          Short label used in console output / phase headers
#   Group        Logical group used by the per-service flags ("nickhr"
#                groups api+webapp under -NickHROnly, etc.)
# ----------------------------------------------------------------------------
$SERVICES = @(
    @{
        Key     = "NSCIM_API"
        Csproj  = Join-Path $ScriptRoot "src\NickScanCentralImagingPortal.API\NickScanCentralImagingPortal.API.csproj"
        Publish = Join-Path $PUBLISH_ROOT "API"
        Dll     = "NickScanCentralImagingPortal.API.dll"
        Tag     = "API"
        Group   = "api"
    },
    @{
        Key     = "NSCIM_WebApp"
        Csproj  = Join-Path $ScriptRoot "src\NickScanWebApp.New\NickScanWebApp.New.csproj"
        Publish = Join-Path $PUBLISH_ROOT "WebApp"
        Dll     = "NickScanWebApp.New.dll"
        Tag     = "WebApp"
        Group   = "webapp"
    },
    @{
        Key     = "NSCIM_NickComms"
        Csproj  = Join-Path $ScriptRoot "services\NickComms.Gateway\NickComms.Gateway.csproj"
        Publish = Join-Path $PUBLISH_ROOT "NickComms"
        Dll     = "NickComms.Gateway.dll"
        Tag     = "NickComms"
        Group   = "nickcomms"
    },
    @{
        Key     = "NSCIM_Portal"
        Csproj  = Join-Path $ScriptRoot "platform\NickERP.Portal\NickERP.Portal.csproj"
        Publish = Join-Path $PUBLISH_ROOT "Portal"
        Dll     = "NickERP.Portal.dll"
        Tag     = "Portal"
        Group   = "portal"
    },
    @{
        Key     = "NickHR_API"
        Csproj  = Join-Path $ScriptRoot "NickHR\src\NickHR.API\NickHR.API.csproj"
        Publish = Join-Path $NICKHR_DEPLOY_ROOT "api"
        Dll     = "NickHR.API.dll"
        Tag     = "NickHR.API"
        Group   = "nickhr"
    },
    @{
        Key     = "NickHR_WebApp"
        Csproj  = Join-Path $ScriptRoot "NickHR\src\NickHR.WebApp\NickHR.WebApp.csproj"
        Publish = Join-Path $NICKHR_DEPLOY_ROOT "webapp"
        Dll     = "NickHR.WebApp.dll"
        Tag     = "NickHR.WebApp"
        Group   = "nickhr"
    },
    @{
        Key     = "NickFinance_WebApp"
        Csproj  = Join-Path $ScriptRoot "finance\NickFinance.WebApp\NickFinance.WebApp.csproj"
        Publish = Join-Path $PUBLISH_ROOT "NickFinance.WebApp"
        Dll     = "NickFinance.WebApp.dll"
        Tag     = "NickFinance.WebApp"
        Group   = "nickfinance"
    }
)

# Convenience lookup - existing references like $SERVICE_API still work.
$SERVICE_API = "NSCIM_API"
$SERVICE_WEBAPP = "NSCIM_WebApp"
# 2.15.3: the Python image-splitter is now supervised as a child of NSCIM_API
# (see ImageSplitterSupervisorService). No separate Windows service to manage.

# --- Selection logic ---------------------------------------------------------
# Resolve which $SERVICES entries to deploy this run, based on the flags.
# Default (no flags): API + WebApp - matches pre-2026-04-27 behavior.
# -Full: every entry in $SERVICES.
# Any *Only flag: just that group; multiple *Only flags can combine.
# ----------------------------------------------------------------------------
function Get-SelectedServices {
    $onlyFlags = @{
        "api"         = $ApiOnly
        "webapp"      = $WebAppOnly
        "nickcomms"   = $NickCommsOnly
        "portal"      = $PortalOnly
        "nickhr"      = $NickHROnly
        "nickfinance" = $NickFinanceOnly
    }
    $anyOnly = $false
    foreach ($v in $onlyFlags.Values) { if ($v) { $anyOnly = $true; break } }

    if ($Full) {
        return $SERVICES
    }
    if ($anyOnly) {
        return $SERVICES | Where-Object { $onlyFlags[$_.Group] }
    }
    # Default: legacy API + WebApp pair.
    return $SERVICES | Where-Object { $_.Group -in @("api", "webapp") }
}

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
    if (-not (Test-Path $csproj)) {
        Write-Fail "$name csproj missing at $csproj"
        throw "Publish failed: csproj not found for $name"
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
    # here 2026-04-19 so a cold source-driven deploy does not regress. Applied uniformly
    # to every selected service (2026-04-27) since they all share the same net8/net10
    # mismatch story.
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
    if ($SkipBuild) { return }
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

$selected = @(Get-SelectedServices)
if ($selected.Count -eq 0) {
    Write-Fail "No services selected. Check your flags."
    exit 1
}

# Compose human-readable mode label from the active flags.
$modeParts = @()
if ($DryRun) { $modeParts += "DRY RUN" }
if ($SkipBuild) { $modeParts += "Skip build" }
if ($Full) {
    $modeParts += "Full deploy ($($selected.Count) services)"
} else {
    $modeParts += "Selected: " + (($selected | ForEach-Object { $_.Tag }) -join ", ")
}
Write-Host "Mode:          $($modeParts -join ' | ')"
Write-Host "Services:      $(($selected | ForEach-Object { $_.Key }) -join ', ')"
Write-Host ""

# Sanity-check every selected csproj exists *before* we touch any service.
# Cheap pre-flight - catches typos and missing repos without leaving services
# stopped mid-deploy.
foreach ($s in $selected) {
    if (-not (Test-Path $s.Csproj)) {
        Write-Fail "Cannot find $($s.Csproj) for $($s.Key) - are you running from the repo root?"
        exit 1
    }
}

# --- Phase 1: Stop services ---
# Stop in reverse selection order. Within the default API+WebApp pair this
# preserves the historical sequence (WebApp before API) so the API's
# ImageSplitter supervisor isn't yanked out from under a busy WebApp request.
Write-Header "Phase 1: Stop services"
$stopOrder = @($selected) ; [array]::Reverse($stopOrder)
foreach ($s in $stopOrder) {
    Stop-SvcIfRunning $s.Key
}

# --- Phase 2: Publish ---
Write-Header "Phase 2: Publish"
foreach ($s in $selected) {
    Publish-Project $s.Tag $s.Csproj $s.Publish
}

# --- Phase 3: Verify binaries ---
Write-Header "Phase 3: Verify binaries"
foreach ($s in $selected) {
    Test-DeploymentBinary $s.Tag (Join-Path $s.Publish $s.Dll)
}

# --- Phase 3.5: Post-publish config (baked in 2026-04-19 from operator hot-patches) ---
#   * rollForward=latestMajor so net8-built assemblies run on net10 host runtime
#   * NSSM env var hands the ImageSplitter the FS6000 UNC path (LocalSystem cannot
#     use the user-mapped Z:\ drive that the operator sees)
# Runs on every deploy - including -SkipBuild - because the helpers are idempotent
# and the publish/ dir may have been refreshed by a hand-copy before the operator
# ran Deploy.ps1 -SkipBuild to cycle services.
Write-Header "Phase 3.5: Post-publish config"
foreach ($s in $selected) {
    Set-RuntimeConfigRollForward $s.Publish
}
# The FS6000 UNC env var is API-specific (only the ImageSplitter supervisor
# under NSCIM_API needs it); apply only when the API service is in the
# selection set.
if ($selected | Where-Object { $_.Key -eq $SERVICE_API }) {
    # 2.15.3: env var now applies to NSCIM_API (supervisor inherits it to the
    # Python child). Previously set on the standalone NSCIM_ImageSplitter NSSM
    # service which has been removed.
    Set-NssmEnvPair $SERVICE_API "NICKSCAN_FS6000_SHARE" "\\172.16.1.1\Image\23301FS01"
}

# --- Phase 4: Start services ---
# Start in original (forward) selection order; with default API+WebApp this
# means API comes up first so its ImageSplitterSupervisorService is ready
# before WebApp starts servicing requests.
Write-Header "Phase 4: Start services"
foreach ($s in $selected) {
    Start-Svc $s.Key
}

# --- Phase 5: Verify running process paths ---
Write-Header "Phase 5: Verify running processes"
Start-Sleep -Seconds 2
foreach ($s in $selected) {
    Test-ProcessPath $s.Key $s.Publish
}

Write-Header "Deployment complete"
Write-Host " Reload the browser to see UI changes." -ForegroundColor Green
Write-Host ""

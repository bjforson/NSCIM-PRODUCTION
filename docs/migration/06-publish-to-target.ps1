# Phase 4.1 — Publish upgraded build directly to Y:\ (which is target's C:\Shared\NSCIM_PRODUCTION)
# Run from current box, on upgrade/net10-3.0 branch, after build passes
# This skips the intermediate publish\ step and writes straight to target's install path

$ErrorActionPreference = 'Stop'
$startTime = Get-Date

Write-Host "=== Publish 3.0 to Target (via Y:\) ===" -ForegroundColor Cyan

# Safety: must be on upgrade branch, not main
Push-Location C:\Shared\NSCIM_PRODUCTION
$branch = git rev-parse --abbrev-ref HEAD
if ($branch -ne 'upgrade/net10-3.0') {
    Pop-Location
    throw "Must be on branch upgrade/net10-3.0, currently on: $branch"
}

# Safety: no uncommitted WIP
$wip = git status --short | Where-Object { $_ -match '\.(cs|razor|csproj|props|json)$' }
if ($wip) {
    Pop-Location
    throw "WIP files detected — commit or stash before publishing:`n$($wip -join "`n")"
}

# Safety: must be on net10
$tfm = Select-String -Path "src\Directory.Build.props" -Pattern "TargetFramework" -SimpleMatch
if (-not (Select-String -Path "src\NickScanCentralImagingPortal.API\*.csproj" -Pattern "net10.0")) {
    Pop-Location
    throw "TargetFramework isn't net10.0 — wrong branch or upgrade not done"
}

Pop-Location

# Target paths (via Y:\)
# NSCIM_Mobile retired 2026-04-22 — WebApp now serves mobile viewports responsively.
$apiOut = "Y:\publish\API"
$webOut = "Y:\publish\WebApp"

# Clean first (remove old files to catch orphaned DLLs)
Write-Host "[1/3] Cleaning target publish dirs..."
@($apiOut, $webOut) | ForEach-Object {
    if (Test-Path $_) { Remove-Item "$_\*" -Recurse -Force -ErrorAction SilentlyContinue }
    New-Item -ItemType Directory -Force -Path $_ | Out-Null
}

# Sweep retired Mobile publish dir if it still exists on target (safe no-op otherwise)
if (Test-Path "Y:\publish\Mobile") {
    Write-Host "[cleanup] Removing retired Y:\publish\Mobile..." -ForegroundColor Yellow
    Remove-Item "Y:\publish\Mobile" -Recurse -Force -ErrorAction SilentlyContinue
}

# Publish API
Write-Host "[2/3] Publishing API to $apiOut..." -ForegroundColor Yellow
Push-Location C:\Shared\NSCIM_PRODUCTION
dotnet publish src/NickScanCentralImagingPortal.API/NickScanCentralImagingPortal.API.csproj -c Release -o $apiOut --no-self-contained 2>&1 | Tee-Object -FilePath (Join-Path $apiOut 'publish.log')
if ($LASTEXITCODE -ne 0) { Pop-Location; throw "API publish failed" }

# Publish WebApp (serves desktop + mobile)
Write-Host "[3/3] Publishing WebApp to $webOut..." -ForegroundColor Yellow
dotnet publish src/NickScanWebApp.New/NickScanWebApp.New.csproj -c Release -o $webOut --no-self-contained 2>&1 | Tee-Object -FilePath (Join-Path $webOut 'publish.log')
if ($LASTEXITCODE -ne 0) { Pop-Location; throw "WebApp publish failed" }

Pop-Location

$elapsed = [math]::Round(((Get-Date) - $startTime).TotalMinutes, 1)
Write-Host ""
Write-Host "=== Publish complete in $elapsed min ===" -ForegroundColor Green
Write-Host ""
Write-Host "Verify .exe present in each:"
@($apiOut, $webOut) | ForEach-Object {
    $exe = Get-ChildItem $_ -Filter *.exe -File | Select-Object -First 1
    if ($exe) {
        $v = $exe.VersionInfo.FileVersion
        Write-Host "  $($exe.Name) — v$v" -ForegroundColor Green
    } else {
        Write-Host "  NO .exe in $_" -ForegroundColor Red
    }
}

Write-Host ""
Write-Host "Next steps on target server:"
Write-Host "  1. Create Python venv: cd C:\Shared\NSCIM_PRODUCTION\services\image-splitter; python -m venv venv; .\venv\Scripts\activate; pip install -r requirements.txt"
Write-Host "  2. Register services: run 07-register-services.ps1 on target"
Write-Host "  3. Start services: Start-Service NSCIM_API, NSCIM_WebApp, NSCIM_ImageSplitter"
Write-Host "  4. Verify version: [Net.ServicePointManager]::ServerCertificateValidationCallback={`$true}; Invoke-RestMethod https://localhost:5300/api/server/version"

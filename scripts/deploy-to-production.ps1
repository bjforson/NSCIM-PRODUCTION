# Deploy NickScan Central Imaging Portal v1 to production
# Usage: .\scripts\deploy-to-production.ps1
# Target: W:\NSCIM_PRODUCTION

param(
    [string]$ProductionPath = "W:\NSCIM_PRODUCTION",
    [switch]$SkipBuild,
    [switch]$WhatIf
)

$ErrorActionPreference = "Stop"
$scriptDir = $PSScriptRoot
$repoRoot = Split-Path -Parent $scriptDir

Write-Host "========================================" -ForegroundColor Cyan
Write-Host " NickScan v1 - Production Deployment" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host "Source:  $repoRoot"
Write-Host "Target:  $ProductionPath"
Write-Host ""

# Read version
$versionFile = Join-Path $repoRoot "VERSION"
$version = "1.0.0"
if (Test-Path $versionFile) {
    $version = (Get-Content $versionFile -Raw).Trim()
}
Write-Host "Version: $version" -ForegroundColor Green
Write-Host ""

if ($WhatIf) {
    Write-Host "[WhatIf] Would build and deploy. Exiting." -ForegroundColor Yellow
    exit 0
}

# Ensure production directory exists
if (-not (Test-Path $ProductionPath)) {
    Write-Host "Creating production directory: $ProductionPath" -ForegroundColor Yellow
    New-Item -ItemType Directory -Path $ProductionPath -Force | Out-Null
}

# Build and publish API
if (-not $SkipBuild) {
    Write-Host "[1/4] Building API..." -ForegroundColor Cyan
    $apiPath = Join-Path $repoRoot "src\NickScanCentralImagingPortal.API"
    Push-Location $apiPath
    try {
        dotnet publish -c Release -o "$ProductionPath\API" 2>&1 | ForEach-Object { Write-Host "  $_" }
        if ($LASTEXITCODE -ne 0) { throw "API build failed" }
    } finally {
        Pop-Location
    }
    Write-Host "  API published to $ProductionPath\API" -ForegroundColor Green
} else {
    Write-Host "[1/4] Skipping API build (SkipBuild)" -ForegroundColor Yellow
}

# Build and publish WebApp
if (-not $SkipBuild) {
    Write-Host "[2/4] Building WebApp..." -ForegroundColor Cyan
    $webPath = Join-Path $repoRoot "src\NickScanWebApp.New"
    Push-Location $webPath
    try {
        dotnet publish -c Release -o "$ProductionPath\WebApp" 2>&1 | ForEach-Object { Write-Host "  $_" }
        if ($LASTEXITCODE -ne 0) { throw "WebApp build failed" }
    } finally {
        Pop-Location
    }
    Write-Host "  WebApp published to $ProductionPath\WebApp" -ForegroundColor Green
} else {
    Write-Host "[2/4] Skipping WebApp build (SkipBuild)" -ForegroundColor Yellow
}

# Copy version and changelog
Write-Host "[3/4] Copying version metadata..." -ForegroundColor Cyan
Copy-Item $versionFile "$ProductionPath\VERSION" -Force
if (Test-Path (Join-Path $repoRoot "CHANGELOG.md")) {
    Copy-Item (Join-Path $repoRoot "CHANGELOG.md") "$ProductionPath\CHANGELOG.md" -Force
}
Write-Host "  VERSION and CHANGELOG copied" -ForegroundColor Green

# Write deployment manifest
Write-Host "[4/4] Writing deployment manifest..." -ForegroundColor Cyan
$manifest = @"
NickScan Central Imaging Portal - Production Deployment
======================================================

Version:    $version
Deployed:   $(Get-Date -Format "yyyy-MM-dd HH:mm:ss")
Source:     $repoRoot

Components:
- API:      $ProductionPath\API
- WebApp:   $ProductionPath\WebApp

Post-deployment:
1. Stop existing API and WebApp services (if running)
2. Restart API service
3. Restart WebApp/IIS application pool (if applicable)
4. Verify ApiSettings:BaseUrl points to API in WebApp appsettings
"@
$manifest | Out-File "$ProductionPath\DEPLOYMENT.txt" -Encoding UTF8
Write-Host "  DEPLOYMENT.txt created" -ForegroundColor Green

Write-Host ""
Write-Host "========================================" -ForegroundColor Green
Write-Host " Deployment complete: v$version" -ForegroundColor Green
Write-Host "========================================" -ForegroundColor Green
Write-Host "Remember to restart API and WebApp services." -ForegroundColor Yellow

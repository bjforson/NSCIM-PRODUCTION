# Setup script for PerfView Memory Profiler
# PerfView is a free, powerful memory profiler from Microsoft

param(
    [switch]$Download,
    [switch]$Install,
    [string]$InstallPath = "$env:USERPROFILE\Tools\PerfView"
)

# Continues past errors intentionally: setup helper runs optional download/install/probe steps and prints guidance regardless of which step succeeds.
$ErrorActionPreference = "Continue"

function Write-Step {
    param([string]$Message, [string]$Color = "Cyan")
    $timestamp = Get-Date -Format "HH:mm:ss"
    Write-Host "[$timestamp] $Message" -ForegroundColor $Color
}

Write-Step "=== PerfView Memory Profiler Setup ===" "Cyan"
Write-Host ""

# Step 1: Check if PerfView is already installed
Write-Step "Step 1: Checking for existing PerfView installation..." "Cyan"
$perfViewPath = Get-Command "PerfView.exe" -ErrorAction SilentlyContinue | Select-Object -ExpandProperty Source
if ($perfViewPath) {
    Write-Host "  ✅ PerfView found at: $perfViewPath" -ForegroundColor Green
    Write-Host ""
    Write-Step "PerfView is already installed. You can skip download/install steps." "Green"
    Write-Host ""
    Write-Host "To start memory profiling:" -ForegroundColor Yellow
    Write-Host "  1. Run PerfView.exe (may require elevation)" -ForegroundColor White
    Write-Host "  2. Click 'Collect' button" -ForegroundColor White
    Write-Host "  3. Select 'Memory' checkbox" -ForegroundColor White
    Write-Host "  4. Set 'Data File' (e.g., C:\Temp\API_Memory.etl)" -ForegroundColor White
    Write-Host "  5. Click 'Start Collection'" -ForegroundColor White
    Write-Host "  6. Let it run for 2-5 minutes while API is running" -ForegroundColor White
    Write-Host "  7. Click 'Stop Collection'" -ForegroundColor White
    Write-Host "  8. Double-click the .etl file to analyze" -ForegroundColor White
    Write-Host ""
    exit 0
} else {
    Write-Host "  ⚠️  PerfView not found in PATH" -ForegroundColor Yellow
}

Write-Host ""

# Step 2: Download PerfView (if requested)
if ($Download) {
    Write-Step "Step 2: Downloading PerfView..." "Cyan"
    $perfViewUrl = "https://github.com/Microsoft/perfview/releases/latest/download/PerfView.exe"
    $downloadPath = Join-Path $env:TEMP "PerfView.exe"
    
    try {
        Write-Host "  Downloading from: $perfViewUrl" -ForegroundColor Gray
        Invoke-WebRequest -Uri $perfViewUrl -OutFile $downloadPath -UseBasicParsing
        Write-Host "  ✅ Downloaded to: $downloadPath" -ForegroundColor Green
        Write-Host ""
        Write-Host "Next steps:" -ForegroundColor Yellow
        Write-Host "  1. Run this script with -Install to install PerfView" -ForegroundColor White
        Write-Host "  2. Or manually copy PerfView.exe to a permanent location" -ForegroundColor White
    } catch {
        Write-Host "  ❌ Download failed: $($_.Exception.Message)" -ForegroundColor Red
        Write-Host ""
        Write-Host "Manual download:" -ForegroundColor Yellow
        Write-Host "  1. Visit: https://github.com/Microsoft/perfview/releases" -ForegroundColor White
        Write-Host "  2. Download PerfView.exe" -ForegroundColor White
        Write-Host "  3. Extract to a folder (e.g., $InstallPath)" -ForegroundColor White
    }
}

# Step 3: Install PerfView (if requested)
if ($Install) {
    Write-Step "Step 3: Installing PerfView..." "Cyan"
    
    if (-not (Test-Path $downloadPath)) {
        Write-Host "  ⚠️  PerfView.exe not found. Run with -Download first." -ForegroundColor Yellow
        exit 1
    }
    
    if (-not (Test-Path $InstallPath)) {
        New-Item -ItemType Directory -Path $InstallPath -Force | Out-Null
        Write-Host "  Created directory: $InstallPath" -ForegroundColor Gray
    }
    
    try {
        Copy-Item -Path $downloadPath -Destination (Join-Path $InstallPath "PerfView.exe") -Force
        Write-Host "  ✅ Installed to: $InstallPath" -ForegroundColor Green
        Write-Host ""
        Write-Host "To use PerfView:" -ForegroundColor Yellow
        Write-Host "  1. Navigate to: $InstallPath" -ForegroundColor White
        Write-Host "  2. Run PerfView.exe (may require elevation)" -ForegroundColor White
        Write-Host ""
        Write-Host "Note: Add $InstallPath to PATH for easier access" -ForegroundColor Gray
    } catch {
        Write-Host "  ❌ Installation failed: $($_.Exception.Message)" -ForegroundColor Red
    }
}

Write-Host ""
Write-Step "=== Setup Complete ===" "Green"
Write-Host ""
Write-Host "Usage Instructions:" -ForegroundColor Cyan
Write-Host "  1. Run: .\scripts\Setup-PerfView-MemoryProfiler.ps1 -Download -Install" -ForegroundColor White
Write-Host "  2. Or manually download from: https://github.com/Microsoft/perfview/releases" -ForegroundColor White
Write-Host "  3. See scripts/Setup-MemoryProfiler.md for detailed usage instructions" -ForegroundColor White
Write-Host ""


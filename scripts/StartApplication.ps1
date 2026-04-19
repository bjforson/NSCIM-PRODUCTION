# Start Backend API and Frontend
# Usage: .\scripts\StartApplication.ps1

$ErrorActionPreference = "Stop"

# Function to find dotnet executable
function Find-DotNet {
    # Check if dotnet is in PATH
    $dotnetInPath = Get-Command dotnet -ErrorAction SilentlyContinue
    if ($dotnetInPath) {
        return $dotnetInPath.Source
    }
    
    # Check common installation locations
    $commonPaths = @(
        "C:\Program Files\dotnet\dotnet.exe",
        "C:\Program Files (x86)\dotnet\dotnet.exe",
        "$env:ProgramFiles\dotnet\dotnet.exe",
        "${env:ProgramFiles(x86)}\dotnet\dotnet.exe"
    )
    
    foreach ($path in $commonPaths) {
        if (Test-Path $path) {
            return $path
        }
    }
    
    return $null
}

# Find dotnet executable
$dotnetPath = Find-DotNet
if (-not $dotnetPath) {
    Write-Host "❌ .NET SDK not found!" -ForegroundColor Red
    Write-Host ""
    Write-Host "Please install .NET 8.0 SDK from:" -ForegroundColor Yellow
    Write-Host "   https://dotnet.microsoft.com/download/dotnet/8.0" -ForegroundColor Cyan
    Write-Host ""
    Write-Host "Or add dotnet to your PATH environment variable." -ForegroundColor Yellow
    exit 1
}

Write-Host "✅ Found .NET SDK: $dotnetPath" -ForegroundColor Green
Write-Host ""

Write-Host "==================================================" -ForegroundColor Cyan
Write-Host "  Starting NickScan Central Imaging Portal" -ForegroundColor Cyan
Write-Host "==================================================" -ForegroundColor Cyan
Write-Host ""

# Get script directory and navigate to project root
$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$projectRoot = Split-Path -Parent $scriptDir
Set-Location $projectRoot

# Project paths
$apiPath = Join-Path $projectRoot "src\NickScanCentralImagingPortal.API"
$apiProject = Join-Path $apiPath "NickScanCentralImagingPortal.API.csproj"
$frontendPath = Join-Path $projectRoot "src\NickScanWebApp.New"
$frontendProject = Join-Path $frontendPath "NickScanWebApp.New.csproj"

# Verify projects exist
if (-not (Test-Path $apiProject)) {
    Write-Host "❌ API project not found at: $apiProject" -ForegroundColor Red
    exit 1
}

if (-not (Test-Path $frontendProject)) {
    Write-Host "❌ Frontend project not found at: $frontendProject" -ForegroundColor Red
    exit 1
}

Write-Host "✅ Projects found" -ForegroundColor Green
Write-Host ""

# Check for existing processes
Write-Host "Checking for existing processes..." -ForegroundColor Yellow
$apiProcesses = Get-Process -Name "NickScanCentralImagingPortal.API" -ErrorAction SilentlyContinue
$frontendProcesses = Get-Process -Name "NickScanWebApp.New" -ErrorAction SilentlyContinue

if ($apiProcesses) {
    Write-Host "⚠️  API is already running (PID: $($apiProcesses.Id))" -ForegroundColor Yellow
    Write-Host "   Stopping existing API process..." -ForegroundColor Gray
    Stop-Process -Id $apiProcesses.Id -Force -ErrorAction SilentlyContinue
    Start-Sleep -Seconds 2
}

if ($frontendProcesses) {
    Write-Host "⚠️  Frontend is already running (PID: $($frontendProcesses.Id))" -ForegroundColor Yellow
    Write-Host "   Stopping existing Frontend process..." -ForegroundColor Gray
    Stop-Process -Id $frontendProcesses.Id -Force -ErrorAction SilentlyContinue
    Start-Sleep -Seconds 2
}

Write-Host ""

# Start Backend API
Write-Host "Step 1: Starting Backend API..." -ForegroundColor Yellow
Write-Host "   Path: $apiPath" -ForegroundColor Gray
Write-Host "   Port: 5205" -ForegroundColor Gray
Write-Host ""

$apiCommand = "cd '$apiPath'; Write-Host '==================================================' -ForegroundColor Cyan; Write-Host '  BACKEND API - Port 5205' -ForegroundColor Cyan; Write-Host '==================================================' -ForegroundColor Cyan; Write-Host ''; & '$dotnetPath' run --project '$apiProject'"
$apiProcess = Start-Process powershell -ArgumentList "-NoExit", "-Command", $apiCommand -PassThru

Write-Host "   ✅ API Process ID: $($apiProcess.Id)" -ForegroundColor Green
Write-Host "   Waiting for API to initialize..." -ForegroundColor Gray
Start-Sleep -Seconds 8

Write-Host ""

# Start Frontend
Write-Host "Step 2: Starting Frontend..." -ForegroundColor Yellow
Write-Host "   Path: $frontendPath" -ForegroundColor Gray
Write-Host "   Port: 5299" -ForegroundColor Gray
Write-Host ""

$frontendCommand = "cd '$frontendPath'; Write-Host '==================================================' -ForegroundColor Magenta; Write-Host '  FRONTEND - Port 5299' -ForegroundColor Magenta; Write-Host '==================================================' -ForegroundColor Magenta; Write-Host ''; & '$dotnetPath' run --project '$frontendProject'"
$frontendProcess = Start-Process powershell -ArgumentList "-NoExit", "-Command", $frontendCommand -PassThru

Write-Host "   ✅ Frontend Process ID: $($frontendProcess.Id)" -ForegroundColor Green
Write-Host "   Waiting for Frontend to initialize..." -ForegroundColor Gray
Start-Sleep -Seconds 8

Write-Host ""

# Summary
Write-Host "==================================================" -ForegroundColor Cyan
Write-Host "  ✅ Application Started Successfully!" -ForegroundColor Green
Write-Host "==================================================" -ForegroundColor Cyan
Write-Host ""
Write-Host "Service URLs:" -ForegroundColor Yellow
Write-Host "   📡 Backend API:     http://localhost:5205" -ForegroundColor Cyan
Write-Host "   📡 API Swagger:      http://localhost:5205/swagger" -ForegroundColor Cyan
Write-Host "   🌐 Frontend:         http://localhost:5299" -ForegroundColor Cyan
Write-Host ""
Write-Host "Process Information:" -ForegroundColor Yellow
Write-Host "   API Process ID:     $($apiProcess.Id)" -ForegroundColor Gray
Write-Host "   Frontend Process ID: $($frontendProcess.Id)" -ForegroundColor Gray
Write-Host ""
Write-Host "To Stop Services:" -ForegroundColor Yellow
Write-Host "   • Close the PowerShell windows, or" -ForegroundColor Gray
Write-Host "   • Press Ctrl+C in each window, or" -ForegroundColor Gray
Write-Host "   • Run: Stop-Process -Id $($apiProcess.Id),$($frontendProcess.Id) -Force" -ForegroundColor Gray
Write-Host ""
Write-Host "Tip: Each service runs in its own window." -ForegroundColor Cyan
Write-Host "     Watch the windows for startup messages and any errors." -ForegroundColor Cyan
Write-Host ""


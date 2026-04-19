# Build Production Application
# Usage: .\scripts\Build-Production.ps1

$ErrorActionPreference = "Stop"

Write-Host "==================================================" -ForegroundColor Cyan
Write-Host "  Production Build Script" -ForegroundColor Cyan
Write-Host "==================================================" -ForegroundColor Cyan
Write-Host ""

# Get production path
$productionPath = "\\10.0.0.79\Shared\NSCIM_PRODUCTION"
if (-not (Test-Path $productionPath)) {
    Write-Host "❌ Production path not found: $productionPath" -ForegroundColor Red
    exit 1
}

Set-Location $productionPath

Write-Host "Production Path: $productionPath" -ForegroundColor White
Write-Host ""

# Check .NET SDK
Write-Host "Step 1: Checking .NET SDK..." -ForegroundColor Yellow
$dotnetPath = Get-Command dotnet -ErrorAction SilentlyContinue
if (-not $dotnetPath) {
    Write-Host "   ❌ .NET SDK not found!" -ForegroundColor Red
    Write-Host "   Please run Install-DotNetSDK.ps1 first" -ForegroundColor Yellow
    exit 1
}

$version = dotnet --version 2>$null
Write-Host "   ✅ .NET SDK: $version" -ForegroundColor Green
Write-Host ""

# Stop running processes
Write-Host "Step 2: Checking for running processes..." -ForegroundColor Yellow
$apiProcesses = Get-Process -Name "NickScanCentralImagingPortal.API" -ErrorAction SilentlyContinue
$frontendProcesses = Get-Process -Name "NickScanWebApp.New" -ErrorAction SilentlyContinue

if ($apiProcesses -or $frontendProcesses) {
    Write-Host "   ⚠️  Found running processes. Stopping..." -ForegroundColor Yellow
    if ($apiProcesses) {
        Stop-Process -Id $apiProcesses.Id -Force -ErrorAction SilentlyContinue
        Write-Host "      ✅ Stopped API process" -ForegroundColor Green
    }
    if ($frontendProcesses) {
        Stop-Process -Id $frontendProcesses.Id -Force -ErrorAction SilentlyContinue
        Write-Host "      ✅ Stopped Frontend process" -ForegroundColor Green
    }
    Start-Sleep -Seconds 2
} else {
    Write-Host "   ✅ No running processes found" -ForegroundColor Green
}
Write-Host ""

# Clean build
Write-Host "Step 3: Cleaning previous build..." -ForegroundColor Yellow
try {
    dotnet clean --verbosity quiet 2>&1 | Out-Null
    Write-Host "   ✅ Clean completed" -ForegroundColor Green
} catch {
    Write-Host "   ⚠️  Clean warning: $($_.Exception.Message)" -ForegroundColor Yellow
}
Write-Host ""

# Restore packages
Write-Host "Step 4: Restoring NuGet packages..." -ForegroundColor Yellow
try {
    dotnet restore --verbosity quiet
    if ($LASTEXITCODE -eq 0) {
        Write-Host "   ✅ Packages restored" -ForegroundColor Green
    } else {
        Write-Host "   ❌ Package restore failed (exit code: $LASTEXITCODE)" -ForegroundColor Red
        exit 1
    }
} catch {
    Write-Host "   ❌ Package restore error: $($_.Exception.Message)" -ForegroundColor Red
    exit 1
}
Write-Host ""

# Build API
Write-Host "Step 5: Building API project..." -ForegroundColor Yellow
$apiProject = Join-Path $productionPath "src\NickScanCentralImagingPortal.API\NickScanCentralImagingPortal.API.csproj"

if (-not (Test-Path $apiProject)) {
    Write-Host "   ❌ API project not found: $apiProject" -ForegroundColor Red
    exit 1
}

try {
    $apiBuildOutput = dotnet build $apiProject --no-incremental 2>&1 | Out-String
    $apiBuildLines = $apiBuildOutput -split "`n"
    
    $apiErrors = $apiBuildLines | Where-Object { $_ -match "error\s+(CS|MSB|NETSDK)" }
    $apiSuccess = $apiBuildLines | Where-Object { $_ -match "Build succeeded" }
    
    if ($apiSuccess -and -not $apiErrors) {
        Write-Host "   ✅ API build succeeded!" -ForegroundColor Green
    } else {
        Write-Host "   ❌ API build failed!" -ForegroundColor Red
        if ($apiErrors) {
            Write-Host ""
            Write-Host "   Errors:" -ForegroundColor Red
            $apiErrors | Select-Object -First 10 | ForEach-Object {
                Write-Host "      $_" -ForegroundColor Red
            }
        }
        exit 1
    }
} catch {
    Write-Host "   ❌ API build error: $($_.Exception.Message)" -ForegroundColor Red
    exit 1
}
Write-Host ""

# Build Frontend
Write-Host "Step 6: Building Frontend project..." -ForegroundColor Yellow
$frontendProject = Join-Path $productionPath "src\NickScanWebApp.New\NickScanWebApp.New.csproj"

if (-not (Test-Path $frontendProject)) {
    Write-Host "   ⚠️  Frontend project not found: $frontendProject" -ForegroundColor Yellow
    Write-Host "   Skipping frontend build..." -ForegroundColor Gray
} else {
    try {
        $frontendBuildOutput = dotnet build $frontendProject --no-incremental 2>&1 | Out-String
        $frontendBuildLines = $frontendBuildOutput -split "`n"
        
        $frontendErrors = $frontendBuildLines | Where-Object { $_ -match "error\s+(CS|MSB|NETSDK)" }
        $frontendSuccess = $frontendBuildLines | Where-Object { $_ -match "Build succeeded" }
        
        if ($frontendSuccess -and -not $frontendErrors) {
            Write-Host "   ✅ Frontend build succeeded!" -ForegroundColor Green
        } else {
            Write-Host "   ❌ Frontend build failed!" -ForegroundColor Red
            if ($frontendErrors) {
                Write-Host ""
                Write-Host "   Errors:" -ForegroundColor Red
                $frontendErrors | Select-Object -First 10 | ForEach-Object {
                    Write-Host "      $_" -ForegroundColor Red
                }
            }
            exit 1
        }
    } catch {
        Write-Host "   ❌ Frontend build error: $($_.Exception.Message)" -ForegroundColor Red
        exit 1
    }
}
Write-Host ""

# Summary
Write-Host "==================================================" -ForegroundColor Cyan
Write-Host "  ✅ Build Completed Successfully!" -ForegroundColor Green
Write-Host "==================================================" -ForegroundColor Cyan
Write-Host ""
Write-Host "Build Output Locations:" -ForegroundColor Yellow
Write-Host "   API:     src\NickScanCentralImagingPortal.API\bin\Debug\net8.0\" -ForegroundColor Gray
Write-Host "   Frontend: src\NickScanWebApp.New\bin\Debug\net8.0\" -ForegroundColor Gray
Write-Host ""
Write-Host "Next Steps:" -ForegroundColor Yellow
Write-Host "   1. Run StartApplication.ps1 to start the services" -ForegroundColor White
Write-Host "   2. Or run the applications manually with 'dotnet run'" -ForegroundColor White
Write-Host ""


# Diagnose Build Issues on Production Server
# Usage: .\scripts\Diagnose-BuildIssues.ps1

$ErrorActionPreference = "Continue"

Write-Host "==================================================" -ForegroundColor Cyan
Write-Host "  Production Build Diagnostics" -ForegroundColor Cyan
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

# Step 1: Check .NET SDK
Write-Host "Step 1: Checking .NET SDK..." -ForegroundColor Yellow
$dotnetPath = Get-Command dotnet -ErrorAction SilentlyContinue
if ($dotnetPath) {
    $version = dotnet --version 2>$null
    Write-Host "   ✅ .NET SDK found: $version" -ForegroundColor Green
    Write-Host "   Location: $($dotnetPath.Source)" -ForegroundColor Gray
    
    # Check if it's .NET 8.x
    if ($version -notlike "8.*") {
        Write-Host "   ⚠️  Warning: Need .NET 8.0, found $version" -ForegroundColor Yellow
    }
} else {
    Write-Host "   ❌ .NET SDK not found!" -ForegroundColor Red
    Write-Host "   Please run Install-DotNetSDK.ps1 first" -ForegroundColor Yellow
    exit 1
}
Write-Host ""

# Step 2: Check solution/project files
Write-Host "Step 2: Checking project structure..." -ForegroundColor Yellow
$slnFiles = Get-ChildItem -Filter "*.sln" -ErrorAction SilentlyContinue
$apiProject = Join-Path $productionPath "src\NickScanCentralImagingPortal.API\NickScanCentralImagingPortal.API.csproj"
$frontendProject = Join-Path $productionPath "src\NickScanWebApp.New\NickScanWebApp.New.csproj"

if ($slnFiles) {
    Write-Host "   ✅ Solution file found: $($slnFiles[0].Name)" -ForegroundColor Green
} else {
    Write-Host "   ⚠️  No solution file found" -ForegroundColor Yellow
}

if (Test-Path $apiProject) {
    Write-Host "   ✅ API project found" -ForegroundColor Green
} else {
    Write-Host "   ❌ API project not found: $apiProject" -ForegroundColor Red
}

if (Test-Path $frontendProject) {
    Write-Host "   ✅ Frontend project found" -ForegroundColor Green
} else {
    Write-Host "   ❌ Frontend project not found: $frontendProject" -ForegroundColor Red
}
Write-Host ""

# Step 3: Check for locked files
Write-Host "Step 3: Checking for locked files (running processes)..." -ForegroundColor Yellow
$apiProcesses = Get-Process -Name "NickScanCentralImagingPortal.API" -ErrorAction SilentlyContinue
$frontendProcesses = Get-Process -Name "NickScanWebApp.New" -ErrorAction SilentlyContinue

if ($apiProcesses) {
    Write-Host "   ⚠️  API is running (PID: $($apiProcesses.Id))" -ForegroundColor Yellow
    Write-Host "      This may lock DLL files during build" -ForegroundColor Gray
} else {
    Write-Host "   ✅ API is not running" -ForegroundColor Green
}

if ($frontendProcesses) {
    Write-Host "   ⚠️  Frontend is running (PID: $($frontendProcesses.Id))" -ForegroundColor Yellow
    Write-Host "      This may lock DLL files during build" -ForegroundColor Gray
} else {
    Write-Host "   ✅ Frontend is not running" -ForegroundColor Green
}
Write-Host ""

# Step 4: Try building API
Write-Host "Step 4: Attempting to build API project..." -ForegroundColor Yellow
if (Test-Path $apiProject) {
    try {
        $buildOutput = dotnet build $apiProject --no-incremental 2>&1 | Out-String
        $buildOutputLines = $buildOutput -split "`n"
        
        $errors = $buildOutputLines | Where-Object { $_ -match "error\s+(CS|MSB|NETSDK)" }
        $warnings = $buildOutputLines | Where-Object { $_ -match "warning\s+(CS|MSB|NETSDK)" }
        $success = $buildOutputLines | Where-Object { $_ -match "Build succeeded" }
        
        if ($success) {
            Write-Host "   ✅ API build succeeded!" -ForegroundColor Green
            if ($warnings) {
                Write-Host "   ⚠️  Warnings: $($warnings.Count)" -ForegroundColor Yellow
            }
        } else {
            Write-Host "   ❌ API build failed!" -ForegroundColor Red
        }
        
        if ($errors) {
            Write-Host ""
            Write-Host "   Build Errors:" -ForegroundColor Red
            $errors | Select-Object -First 10 | ForEach-Object {
                Write-Host "      $_" -ForegroundColor Red
            }
        }
        
        # Show last few lines of output
        Write-Host ""
        Write-Host "   Last build output:" -ForegroundColor Gray
        $buildOutputLines | Select-Object -Last 5 | ForEach-Object {
            Write-Host "      $_" -ForegroundColor Gray
        }
    } catch {
        Write-Host "   ❌ Build error: $($_.Exception.Message)" -ForegroundColor Red
    }
} else {
    Write-Host "   ⚠️  Skipping - API project not found" -ForegroundColor Yellow
}
Write-Host ""

# Step 5: Try building Frontend
Write-Host "Step 5: Attempting to build Frontend project..." -ForegroundColor Yellow
if (Test-Path $frontendProject) {
    try {
        $buildOutput = dotnet build $frontendProject --no-incremental 2>&1 | Out-String
        $buildOutputLines = $buildOutput -split "`n"
        
        $errors = $buildOutputLines | Where-Object { $_ -match "error\s+(CS|MSB|NETSDK)" }
        $warnings = $buildOutputLines | Where-Object { $_ -match "warning\s+(CS|MSB|NETSDK)" }
        $success = $buildOutputLines | Where-Object { $_ -match "Build succeeded" }
        
        if ($success) {
            Write-Host "   ✅ Frontend build succeeded!" -ForegroundColor Green
            if ($warnings) {
                Write-Host "   ⚠️  Warnings: $($warnings.Count)" -ForegroundColor Yellow
            }
        } else {
            Write-Host "   ❌ Frontend build failed!" -ForegroundColor Red
        }
        
        if ($errors) {
            Write-Host ""
            Write-Host "   Build Errors:" -ForegroundColor Red
            $errors | Select-Object -First 10 | ForEach-Object {
                Write-Host "      $_" -ForegroundColor Red
            }
        }
        
        # Show last few lines of output
        Write-Host ""
        Write-Host "   Last build output:" -ForegroundColor Gray
        $buildOutputLines | Select-Object -Last 5 | ForEach-Object {
            Write-Host "      $_" -ForegroundColor Gray
        }
    } catch {
        Write-Host "   ❌ Build error: $($_.Exception.Message)" -ForegroundColor Red
    }
} else {
    Write-Host "   ⚠️  Skipping - Frontend project not found" -ForegroundColor Yellow
}
Write-Host ""

# Step 6: Check for common issues
Write-Host "Step 6: Checking for common build issues..." -ForegroundColor Yellow

# Check for missing packages
$packagesPath = Join-Path $env:USERPROFILE ".nuget\packages"
if (-not (Test-Path $packagesPath)) {
    Write-Host "   ⚠️  NuGet packages folder not found" -ForegroundColor Yellow
} else {
    Write-Host "   ✅ NuGet packages folder exists" -ForegroundColor Green
}

# Check for obj/bin folders (might need cleanup)
$apiObjPath = Join-Path (Split-Path $apiProject -Parent) "obj"
$apiBinPath = Join-Path (Split-Path $apiProject -Parent) "bin"
if (Test-Path $apiObjPath) {
    Write-Host "   ✅ API obj folder exists" -ForegroundColor Green
}
if (Test-Path $apiBinPath) {
    Write-Host "   ✅ API bin folder exists" -ForegroundColor Green
}

Write-Host ""

# Summary
Write-Host "==================================================" -ForegroundColor Cyan
Write-Host "  Diagnostic Summary" -ForegroundColor Cyan
Write-Host "==================================================" -ForegroundColor Cyan
Write-Host ""

Write-Host "Recommendations:" -ForegroundColor Yellow
if ($apiProcesses -or $frontendProcesses) {
    Write-Host "   1. Stop running processes before building:" -ForegroundColor White
    if ($apiProcesses) {
        Write-Host "      Stop-Process -Id $($apiProcesses.Id) -Force" -ForegroundColor Gray
    }
    if ($frontendProcesses) {
        Write-Host "      Stop-Process -Id $($frontendProcesses.Id) -Force" -ForegroundColor Gray
    }
}

Write-Host "   2. Try a clean build:" -ForegroundColor White
Write-Host "      dotnet clean" -ForegroundColor Gray
Write-Host "      dotnet restore" -ForegroundColor Gray
Write-Host "      dotnet build --no-incremental" -ForegroundColor Gray

Write-Host "   3. If issues persist, check:" -ForegroundColor White
Write-Host "      - Network connectivity to NuGet feeds" -ForegroundColor Gray
Write-Host "      - Disk space availability" -ForegroundColor Gray
Write-Host "      - File permissions on production share" -ForegroundColor Gray
Write-Host ""


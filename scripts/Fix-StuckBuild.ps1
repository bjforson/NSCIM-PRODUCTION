# Fix Stuck Build Issues
# Usage: .\scripts\Fix-StuckBuild.ps1

$ErrorActionPreference = "Stop"

Write-Host "==================================================" -ForegroundColor Cyan
Write-Host "  Fixing Stuck Build Issues" -ForegroundColor Cyan
Write-Host "==================================================" -ForegroundColor Cyan
Write-Host ""

$productionPath = "\\10.0.0.79\Shared\NSCIM_PRODUCTION"

# Step 1: Stop all processes
Write-Host "Step 1: Stopping all running processes..." -ForegroundColor Yellow
$apiProcesses = Get-Process -Name "NickScanCentralImagingPortal.API" -ErrorAction SilentlyContinue
$frontendProcesses = Get-Process -Name "NickScanWebApp.New" -ErrorAction SilentlyContinue
$dotnetProcesses = Get-Process -Name "dotnet" -ErrorAction SilentlyContinue | Where-Object { 
    $_.Path -like "*NICKSCAN*" -or $_.MainWindowTitle -like "*NickScan*"
}

$stoppedCount = 0
if ($apiProcesses) {
    Stop-Process -Id $apiProcesses.Id -Force -ErrorAction SilentlyContinue
    $stoppedCount++
    Write-Host "   ✅ Stopped API process" -ForegroundColor Green
}
if ($frontendProcesses) {
    Stop-Process -Id $frontendProcesses.Id -Force -ErrorAction SilentlyContinue
    $stoppedCount++
    Write-Host "   ✅ Stopped Frontend process" -ForegroundColor Green
}
if ($dotnetProcesses) {
    $dotnetProcesses | ForEach-Object {
        Stop-Process -Id $_.Id -Force -ErrorAction SilentlyContinue
        $stoppedCount++
    }
    Write-Host "   ✅ Stopped $($dotnetProcesses.Count) .NET process(es)" -ForegroundColor Green
}

if ($stoppedCount -eq 0) {
    Write-Host "   ✅ No processes to stop" -ForegroundColor Green
} else {
    Write-Host "   ✅ Stopped $stoppedCount process(es)" -ForegroundColor Green
    Start-Sleep -Seconds 3
}
Write-Host ""

# Step 2: Change to production directory
Write-Host "Step 2: Navigating to production directory..." -ForegroundColor Yellow
if (-not (Test-Path $productionPath)) {
    Write-Host "   ❌ Production path not found: $productionPath" -ForegroundColor Red
    exit 1
}

Set-Location $productionPath
Write-Host "   ✅ Current directory: $(Get-Location)" -ForegroundColor Green
Write-Host ""

# Step 3: Clean build artifacts
Write-Host "Step 3: Cleaning build artifacts..." -ForegroundColor Yellow
try {
    dotnet clean --verbosity quiet 2>&1 | Out-Null
    Write-Host "   ✅ Clean completed" -ForegroundColor Green
} catch {
    Write-Host "   ⚠️  Clean warning: $($_.Exception.Message)" -ForegroundColor Yellow
}
Write-Host ""

# Step 4: Clear NuGet cache
Write-Host "Step 4: Clearing NuGet cache..." -ForegroundColor Yellow
try {
    dotnet nuget locals all --clear 2>&1 | Out-Null
    Write-Host "   ✅ NuGet cache cleared" -ForegroundColor Green
} catch {
    Write-Host "   ⚠️  Cache clear warning: $($_.Exception.Message)" -ForegroundColor Yellow
}
Write-Host ""

# Step 5: Restore packages with timeout
Write-Host "Step 5: Restoring NuGet packages (with 60s timeout)..." -ForegroundColor Yellow
Write-Host "   This may take a few minutes..." -ForegroundColor Gray

$restoreJob = Start-Job -ScriptBlock {
    param($path)
    Set-Location $path
    dotnet restore --no-cache --verbosity minimal 2>&1
} -ArgumentList $productionPath

$timeout = 60
$elapsed = 0
$completed = $false

while ($elapsed -lt $timeout -and -not $completed) {
    Start-Sleep -Seconds 3
    $elapsed += 3
    
    if ($restoreJob.State -eq "Completed") {
        $completed = $true
        $output = Receive-Job $restoreJob
        Remove-Job $restoreJob
        
        if ($output -match "error" -or $output -match "failed") {
            Write-Host "   ❌ Restore failed!" -ForegroundColor Red
            $output | Select-Object -Last 10 | ForEach-Object {
                Write-Host "      $_" -ForegroundColor Red
            }
            exit 1
        } else {
            Write-Host "   ✅ Restore completed in $elapsed seconds" -ForegroundColor Green
        }
    } else {
        Write-Host "   Restoring... ($elapsed seconds)" -ForegroundColor Gray
    }
}

if (-not $completed) {
    Write-Host "   ❌ Restore timed out after $timeout seconds!" -ForegroundColor Red
    Write-Host "   This suggests network or NuGet server issues." -ForegroundColor Yellow
    Stop-Job $restoreJob -ErrorAction SilentlyContinue
    Remove-Job $restoreJob -ErrorAction SilentlyContinue
    exit 1
}
Write-Host ""

# Step 6: Build with verbose output
Write-Host "Step 6: Building API project (with 120s timeout)..." -ForegroundColor Yellow
$apiProject = Join-Path $productionPath "src\NickScanCentralImagingPortal.API\NickScanCentralImagingPortal.API.csproj"

if (-not (Test-Path $apiProject)) {
    Write-Host "   ❌ API project not found: $apiProject" -ForegroundColor Red
    exit 1
}

$buildJob = Start-Job -ScriptBlock {
    param($projectPath)
    Set-Location (Split-Path $projectPath -Parent)
    dotnet build $projectPath --no-incremental --verbosity minimal 2>&1
} -ArgumentList $apiProject

$timeout = 120
$elapsed = 0
$completed = $false

Write-Host "   Building... (this may take 1-2 minutes)" -ForegroundColor Gray

while ($elapsed -lt $timeout -and -not $completed) {
    Start-Sleep -Seconds 5
    $elapsed += 5
    
    if ($buildJob.State -eq "Completed") {
        $completed = $true
        $output = Receive-Job $buildJob
        Remove-Job $buildJob
        
        if ($output -match "Build succeeded") {
            Write-Host "   ✅ Build completed successfully in $elapsed seconds!" -ForegroundColor Green
        } elseif ($output -match "error") {
            Write-Host "   ❌ Build failed!" -ForegroundColor Red
            $output | Select-Object -Last 20 | ForEach-Object {
                Write-Host "      $_" -ForegroundColor Red
            }
            exit 1
        } else {
            Write-Host "   ⚠️  Build completed but status unclear" -ForegroundColor Yellow
            $output | Select-Object -Last 10 | ForEach-Object {
                Write-Host "      $_" -ForegroundColor Gray
            }
        }
    } else {
        if ($elapsed % 15 -eq 0) {
            Write-Host "   Still building... ($elapsed seconds)" -ForegroundColor Yellow
        }
    }
}

if (-not $completed) {
    Write-Host "   ❌ Build timed out after $timeout seconds!" -ForegroundColor Red
    Write-Host "   Build is stuck. Check:" -ForegroundColor Yellow
    Write-Host "      - Network connectivity" -ForegroundColor White
    Write-Host "      - Disk space" -ForegroundColor White
    Write-Host "      - .NET SDK installation" -ForegroundColor White
    Stop-Job $buildJob -ErrorAction SilentlyContinue
    Remove-Job $buildJob -ErrorAction SilentlyContinue
    exit 1
}

Write-Host ""

# Summary
Write-Host "==================================================" -ForegroundColor Cyan
Write-Host "  ✅ Build Fix Complete!" -ForegroundColor Green
Write-Host "==================================================" -ForegroundColor Cyan
Write-Host ""
Write-Host "If build still fails, try:" -ForegroundColor Yellow
Write-Host "   1. Check .NET SDK: dotnet --version" -ForegroundColor White
Write-Host "   2. Build locally first, then copy to production" -ForegroundColor White
Write-Host "   3. Check network share permissions" -ForegroundColor White
Write-Host ""


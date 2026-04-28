# Diagnose Stuck Build Issues
# Usage: .\scripts\Diagnose-StuckBuild.ps1

# Continues past errors intentionally: diagnostic script runs many independent build-environment checks and reports each — failing fast on the first check defeats the diagnostic purpose.
$ErrorActionPreference = "Continue"

Write-Host "==================================================" -ForegroundColor Cyan
Write-Host "  Diagnosing Stuck Build Issues" -ForegroundColor Cyan
Write-Host "==================================================" -ForegroundColor Cyan
Write-Host ""

$productionPath = "\\10.0.0.79\Shared\NSCIM_PRODUCTION"

# Check 1: .NET SDK
Write-Host "Step 1: Checking .NET SDK..." -ForegroundColor Yellow
$dotnetPath = Get-Command dotnet -ErrorAction SilentlyContinue
if (-not $dotnetPath) {
    Write-Host "   ❌ .NET SDK not found!" -ForegroundColor Red
    Write-Host "   This is likely the problem. Install .NET 8.0 SDK first." -ForegroundColor Yellow
    Write-Host "   Run: .\scripts\Install-DotNetSDK.ps1" -ForegroundColor Cyan
    exit 1
}

$version = dotnet --version 2>$null
Write-Host "   ✅ .NET SDK found: $version" -ForegroundColor Green
Write-Host ""

# Check 2: Network share connectivity
Write-Host "Step 2: Checking network share connectivity..." -ForegroundColor Yellow
if (-not (Test-Path $productionPath)) {
    Write-Host "   ❌ Cannot access production path: $productionPath" -ForegroundColor Red
    Write-Host "   Check network connectivity and permissions." -ForegroundColor Yellow
    exit 1
}

Write-Host "   ✅ Production path accessible" -ForegroundColor Green
Write-Host ""

# Check 3: Check for locked files
Write-Host "Step 3: Checking for locked files..." -ForegroundColor Yellow
$apiProcesses = Get-Process -Name "NickScanCentralImagingPortal.API" -ErrorAction SilentlyContinue
$frontendProcesses = Get-Process -Name "NickScanWebApp.New" -ErrorAction SilentlyContinue
$dotnetProcesses = Get-Process -Name "dotnet" -ErrorAction SilentlyContinue | Where-Object { 
    $_.Path -like "*NICKSCAN*" -or $_.MainWindowTitle -like "*NickScan*"
}

if ($apiProcesses -or $frontendProcesses -or $dotnetProcesses) {
    Write-Host "   ⚠️  Found running processes that may lock files:" -ForegroundColor Yellow
    if ($apiProcesses) {
        Write-Host "      • API Process: PID $($apiProcesses.Id)" -ForegroundColor Gray
    }
    if ($frontendProcesses) {
        Write-Host "      • Frontend Process: PID $($frontendProcesses.Id)" -ForegroundColor Gray
    }
    if ($dotnetProcesses) {
        Write-Host "      • .NET Processes: $($dotnetProcesses.Count) found" -ForegroundColor Gray
        $dotnetProcesses | ForEach-Object {
            Write-Host "        - PID $($_.Id): $($_.Path)" -ForegroundColor Gray
        }
    }
    Write-Host "   💡 Stop these processes before building:" -ForegroundColor Yellow
    if ($apiProcesses) {
        Write-Host "      Stop-Process -Id $($apiProcesses.Id) -Force" -ForegroundColor Cyan
    }
    if ($frontendProcesses) {
        Write-Host "      Stop-Process -Id $($frontendProcesses.Id) -Force" -ForegroundColor Cyan
    }
} else {
    Write-Host "   ✅ No processes locking files" -ForegroundColor Green
}
Write-Host ""

# Check 4: Check disk space
Write-Host "Step 4: Checking disk space..." -ForegroundColor Yellow
try {
    $drive = (Get-Item $productionPath).PSDrive
    $driveInfo = Get-PSDrive $drive.Name -ErrorAction SilentlyContinue
    if ($driveInfo) {
        $freeSpaceGB = [math]::Round($driveInfo.Free / 1GB, 2)
        $usedSpaceGB = [math]::Round(($driveInfo.Used) / 1GB, 2)
        Write-Host "   Free space: $freeSpaceGB GB" -ForegroundColor $(if ($freeSpaceGB -lt 5) { "Red" } else { "Green" })
        Write-Host "   Used space: $usedSpaceGB GB" -ForegroundColor Gray
        
        if ($freeSpaceGB -lt 1) {
            Write-Host "   ⚠️  Low disk space may cause build issues!" -ForegroundColor Yellow
        }
    }
} catch {
    Write-Host "   ⚠️  Could not check disk space" -ForegroundColor Yellow
}
Write-Host ""

# Check 5: Check NuGet connectivity
Write-Host "Step 5: Testing NuGet connectivity..." -ForegroundColor Yellow
try {
    $nugetTest = dotnet nuget list source 2>&1 | Out-String
    if ($nugetTest -match "error" -or $nugetTest -match "failed") {
        Write-Host "   ⚠️  NuGet source issues detected" -ForegroundColor Yellow
        Write-Host "   Output: $nugetTest" -ForegroundColor Gray
    } else {
        Write-Host "   ✅ NuGet sources configured" -ForegroundColor Green
    }
} catch {
    Write-Host "   ⚠️  Could not check NuGet sources" -ForegroundColor Yellow
}
Write-Host ""

# Check 6: Try a simple build test
Write-Host "Step 6: Testing build with verbose output..." -ForegroundColor Yellow
$apiProject = Join-Path $productionPath "src\NickScanCentralImagingPortal.API\NickScanCentralImagingPortal.API.csproj"

if (Test-Path $apiProject) {
    Write-Host "   Attempting build with timeout (30 seconds)..." -ForegroundColor Gray
    Write-Host "   (This will show where it's stuck)" -ForegroundColor Gray
    Write-Host ""
    
    # Try build with timeout
    $buildJob = Start-Job -ScriptBlock {
        param($projectPath)
        Set-Location (Split-Path $projectPath -Parent)
        dotnet build $projectPath --verbosity minimal 2>&1
    } -ArgumentList $apiProject
    
    $timeout = 30
    $elapsed = 0
    $completed = $false
    
    while ($elapsed -lt $timeout -and -not $completed) {
        Start-Sleep -Seconds 2
        $elapsed += 2
        
        if ($buildJob.State -eq "Completed") {
            $completed = $true
            $output = Receive-Job $buildJob
            Remove-Job $buildJob
            
            Write-Host "   Build completed in $elapsed seconds" -ForegroundColor Green
            Write-Host "   Last output:" -ForegroundColor Gray
            $output | Select-Object -Last 10 | ForEach-Object {
                Write-Host "      $_" -ForegroundColor Gray
            }
        } else {
            Write-Host "   Still building... ($elapsed seconds)" -ForegroundColor Yellow
        }
    }
    
    if (-not $completed) {
        Write-Host "   ❌ Build timed out after $timeout seconds!" -ForegroundColor Red
        Write-Host "   Build is stuck. Stopping..." -ForegroundColor Yellow
        Stop-Job $buildJob -ErrorAction SilentlyContinue
        Remove-Job $buildJob -ErrorAction SilentlyContinue
    }
} else {
    Write-Host "   ⚠️  API project not found" -ForegroundColor Yellow
}
Write-Host ""

# Recommendations
Write-Host "==================================================" -ForegroundColor Cyan
Write-Host "  Recommendations" -ForegroundColor Cyan
Write-Host "==================================================" -ForegroundColor Cyan
Write-Host ""

Write-Host "Common causes of stuck builds:" -ForegroundColor Yellow
Write-Host ""
Write-Host "1. Missing .NET SDK:" -ForegroundColor White
Write-Host "   Run: .\scripts\Install-DotNetSDK.ps1" -ForegroundColor Cyan
Write-Host ""
Write-Host "2. Locked files (running processes):" -ForegroundColor White
Write-Host "   Stop all API/Frontend processes first" -ForegroundColor Cyan
Write-Host "   Then try: dotnet clean" -ForegroundColor Cyan
Write-Host ""
Write-Host "3. Network share issues:" -ForegroundColor White
Write-Host "   Try building locally first, then copy to production" -ForegroundColor Cyan
Write-Host ""
Write-Host "4. NuGet restore hanging:" -ForegroundColor White
Write-Host "   Try: dotnet restore --no-cache" -ForegroundColor Cyan
Write-Host ""
Write-Host "5. Low disk space:" -ForegroundColor White
Write-Host "   Free up space on the production server" -ForegroundColor Cyan
Write-Host ""

Write-Host "Quick fix - Try this:" -ForegroundColor Yellow
Write-Host "   1. Stop all processes" -ForegroundColor White
Write-Host "   2. cd \\10.0.0.79\Shared\NSCIM_PRODUCTION" -ForegroundColor Cyan
Write-Host "   3. dotnet clean" -ForegroundColor Cyan
Write-Host "   4. dotnet restore --no-cache" -ForegroundColor Cyan
Write-Host "   5. dotnet build --verbosity normal" -ForegroundColor Cyan
Write-Host ""


# Restart API with Memory Fixes
# This script gracefully shuts down the API (if possible), force-stops if needed, rebuilds, restarts, and monitors memory

param(
    [string]$ApiUrl = "http://localhost:5205",
    [int]$GracefulShutdownTimeoutSeconds = 10,
    [int]$ForceStopTimeoutSeconds = 30,
    [int]$MemoryMonitorDurationMinutes = 10,
    [int]$MemoryMonitorIntervalSeconds = 30
)

$ErrorActionPreference = "Stop"
$script:apiProcess = $null
$script:startTime = Get-Date

function Write-Step {
    param([string]$Message, [string]$Color = "Cyan")
    $timestamp = Get-Date -Format "HH:mm:ss"
    Write-Host "[$timestamp] $Message" -ForegroundColor $Color
}

function Get-ApiProcess {
    $process = Get-Process -Name "NickScanCentralImagingPortal.API" -ErrorAction SilentlyContinue
    if ($process) {
        $memoryGB = [math]::Round($process.WS / 1GB, 2)
        $memoryMB = [math]::Round($process.WS / 1MB, 0)
        $uptime = (Get-Date) - $process.StartTime
        Write-Host "  Process ID: $($process.Id)" -ForegroundColor Gray
        Write-Host "  Memory: $memoryGB GB ($memoryMB MB)" -ForegroundColor Gray
        Write-Host "  Uptime: $($uptime.ToString('hh\:mm\:ss'))" -ForegroundColor Gray
        return $process
    }
    return $null
}

function Try-GracefulShutdown {
    Write-Step "Attempting graceful shutdown via API endpoint..." "Yellow"
    
    try {
        $shutdownUrl = "$ApiUrl/api/SystemAdmin/shutdown"
        $response = Invoke-RestMethod -Uri $shutdownUrl -Method POST -TimeoutSec $GracefulShutdownTimeoutSeconds -ErrorAction Stop
        
        Write-Step "✅ Graceful shutdown initiated: $($response.message)" "Green"
        
        # Wait for process to stop
        $maxWait = $GracefulShutdownTimeoutSeconds
        $waited = 0
        while ((Get-ApiProcess) -and $waited -lt $maxWait) {
            Start-Sleep -Seconds 1
            $waited++
            Write-Host "  Waiting for graceful shutdown... ($waited/$maxWait seconds)" -ForegroundColor Gray
        }
        
        if (-not (Get-ApiProcess)) {
            Write-Step "✅ API stopped gracefully" "Green"
            return $true
        } else {
            Write-Step "⚠️ Graceful shutdown timed out, will force stop" "Yellow"
            return $false
        }
    }
    catch {
        Write-Step "⚠️ Graceful shutdown failed (API may be unresponsive): $($_.Exception.Message)" "Yellow"
        return $false
    }
}

function Force-StopApi {
    Write-Step "Force stopping API process..." "Yellow"
    
    $process = Get-ApiProcess
    if (-not $process) {
        Write-Step "✅ API process not found (already stopped)" "Green"
        return $true
    }
    
    $processId = $process.Id
    Write-Host "  Stopping process ID: $processId" -ForegroundColor Gray
    
    try {
        Stop-Process -Id $processId -Force -ErrorAction Stop
        
        # Wait for process to stop
        $maxWait = $ForceStopTimeoutSeconds
        $waited = 0
        while ((Get-Process -Id $processId -ErrorAction SilentlyContinue) -and $waited -lt $maxWait) {
            Start-Sleep -Seconds 1
            $waited++
            Write-Host "  Waiting for process to stop... ($waited/$maxWait seconds)" -ForegroundColor Gray
        }
        
        if (-not (Get-Process -Id $processId -ErrorAction SilentlyContinue)) {
            Write-Step "✅ API process force-stopped successfully" "Green"
            return $true
        } else {
            Write-Step "❌ Failed to stop API process (may require manual intervention)" "Red"
            return $false
        }
    }
    catch {
        Write-Step "❌ Error force-stopping API: $($_.Exception.Message)" "Red"
        return $false
    }
}

function Build-Api {
    Write-Step "Building API project..." "Cyan"
    
    $apiProjectPath = "src\NickScanCentralImagingPortal.API\NickScanCentralImagingPortal.API.csproj"
    
    if (-not (Test-Path $apiProjectPath)) {
        Write-Step "❌ API project not found at: $apiProjectPath" "Red"
        return $false
    }
    
    try {
        Push-Location $PSScriptRoot\..
        $buildOutput = dotnet build $apiProjectPath --no-incremental 2>&1
        
        if ($LASTEXITCODE -eq 0) {
            Write-Step "✅ API build succeeded" "Green"
            return $true
        } else {
            Write-Step "❌ API build failed" "Red"
            $buildOutput | Select-String -Pattern "error" | ForEach-Object { Write-Host "  $_" -ForegroundColor Red }
            return $false
        }
    }
    catch {
        Write-Step "❌ Error building API: $($_.Exception.Message)" "Red"
        return $false
    }
    finally {
        Pop-Location
    }
}

function Start-Api {
    Write-Step "Starting API..." "Cyan"
    
    $apiExePath = "src\NickScanCentralImagingPortal.API\bin\Debug\net8.0\NickScanCentralImagingPortal.API.exe"
    $apiProjectPath = "src\NickScanCentralImagingPortal.API"
    
    if (Test-Path $apiExePath) {
        # Use executable if available
        Write-Host "  Starting API from: $apiExePath" -ForegroundColor Gray
        $process = Start-Process -FilePath $apiExePath -WorkingDirectory (Resolve-Path $apiProjectPath\bin\Debug\net8.0) -PassThru -WindowStyle Normal
    } else {
        # Use dotnet run if executable not found
        Write-Host "  Starting API with: dotnet run" -ForegroundColor Gray
        Push-Location $apiProjectPath
        $process = Start-Process -FilePath "dotnet" -ArgumentList "run" -WorkingDirectory (Resolve-Path $apiProjectPath) -PassThru -WindowStyle Normal
        Pop-Location
    }
    
    if ($process) {
        Write-Step "✅ API process started (PID: $($process.Id))" "Green"
        
        # Wait a bit for API to initialize
        Write-Host "  Waiting for API to initialize..." -ForegroundColor Gray
        Start-Sleep -Seconds 5
        
        # Check if process is still running
        $processCheck = Get-Process -Id $process.Id -ErrorAction SilentlyContinue
        if ($processCheck) {
            return $process
        } else {
            Write-Step "❌ API process exited immediately (check logs for errors)" "Red"
            return $null
        }
    } else {
        Write-Step "❌ Failed to start API process" "Red"
        return $null
    }
}

function Monitor-Memory {
    param(
        [int]$DurationMinutes = 10,
        [int]$IntervalSeconds = 30
    )
    
    Write-Step "Monitoring API memory usage for $DurationMinutes minutes..." "Cyan"
    Write-Host "  Interval: $IntervalSeconds seconds" -ForegroundColor Gray
    Write-Host ""
    
    $endTime = (Get-Date).AddMinutes($DurationMinutes)
    $readings = @()
    $iteration = 0
    
    Write-Host "Time     | Memory (GB) | Memory (MB) | Change (MB) | Status" -ForegroundColor Yellow
    Write-Host "---------|-------------|-------------|-------------|--------" -ForegroundColor Yellow
    
    while ((Get-Date) -lt $endTime) {
        $process = Get-ApiProcess
        if ($process) {
            $memoryGB = [math]::Round($process.WS / 1GB, 2)
            $memoryMB = [math]::Round($process.WS / 1MB, 0)
            
            $changeMB = 0
            if ($readings.Count -gt 0) {
                $previousMB = $readings[-1]
                $changeMB = $memoryMB - $previousMB
            }
            
            $readings += $memoryMB
            
            $status = "Running"
            if ($memoryGB -gt 15) {
                $status = "⚠️ High"
            } elseif ($memoryGB -gt 10) {
                $status = "⚠️ Medium"
            } else {
                $status = "✅ Normal"
            }
            
            $timestamp = Get-Date -Format "HH:mm:ss"
            $changeStr = if ($changeMB -ge 0) { "+$changeMB" } else { "$changeMB" }
            Write-Host "$timestamp | $($memoryGB.ToString('N2').PadLeft(11)) | $($memoryMB.ToString('N0').PadLeft(11)) | $($changeStr.PadLeft(11)) | $status"
        } else {
            Write-Host "$(Get-Date -Format 'HH:mm:ss') | API process not found!" -ForegroundColor Red
            break
        }
        
        $iteration++
        if ((Get-Date) -lt $endTime) {
            Start-Sleep -Seconds $IntervalSeconds
        }
    }
    
    Write-Host ""
    if ($readings.Count -gt 1) {
        $initialMB = $readings[0]
        $finalMB = $readings[-1]
        $totalChange = $finalMB - $initialMB
        $averageMB = ($readings | Measure-Object -Average).Average
        $peakMB = ($readings | Measure-Object -Maximum).Maximum
        
        Write-Step "Memory Monitoring Summary:" "Cyan"
        Write-Host "  Initial Memory: $([math]::Round($initialMB / 1024, 2)) GB ($initialMB MB)" -ForegroundColor Gray
        Write-Host "  Final Memory: $([math]::Round($finalMB / 1024, 2)) GB ($finalMB MB)" -ForegroundColor Gray
        Write-Host "  Total Change: $([math]::Round($totalChange / 1024, 2)) GB ($totalChange MB)" -ForegroundColor $(if ($totalChange -gt 1000) { "Yellow" } else { "Green" })
        Write-Host "  Average Memory: $([math]::Round($averageMB / 1024, 2)) GB ($([math]::Round($averageMB, 0)) MB)" -ForegroundColor Gray
        Write-Host "  Peak Memory: $([math]::Round($peakMB / 1024, 2)) GB ($peakMB MB)" -ForegroundColor Gray
        
        if ($totalChange -gt 2000) {
            Write-Step "⚠️ WARNING: Memory increased by more than 2 GB during monitoring!" "Yellow"
        } elseif ($totalChange -lt 0) {
            Write-Step "✅ Memory decreased (GC or fixes working)" "Green"
        }
    }
}

# Main execution
Write-Step "=== API Restart Script with Memory Fixes ===" "Cyan"
Write-Host ""

# Step 1: Check current API status
Write-Step "Step 1: Checking current API status..." "Cyan"
$currentProcess = Get-ApiProcess
if ($currentProcess) {
    Write-Host "  API is currently running" -ForegroundColor Gray
} else {
    Write-Host "  API is not running" -ForegroundColor Gray
}

Write-Host ""

# Step 2: Attempt graceful shutdown
if ($currentProcess) {
    Write-Step "Step 2: Shutting down API..." "Cyan"
    $gracefulSuccess = Try-GracefulShutdown
    
    if (-not $gracefulSuccess) {
        # Step 3: Force stop if graceful shutdown failed
        Write-Step "Step 3: Force stopping API..." "Cyan"
        $forceSuccess = Force-StopApi
        if (-not $forceSuccess) {
            Write-Step "❌ Failed to stop API. Please stop it manually and run this script again." "Red"
            exit 1
        }
    }
    
    # Wait a moment for cleanup
    Start-Sleep -Seconds 2
}

Write-Host ""

# Step 4: Build API
Write-Step "Step 4: Building API with memory fixes..." "Cyan"
$buildSuccess = Build-Api
if (-not $buildSuccess) {
    Write-Step "❌ Build failed. Please fix build errors and try again." "Red"
    exit 1
}

Write-Host ""

# Step 5: Start API
Write-Step "Step 5: Starting API..." "Cyan"
$newProcess = Start-Api
if (-not $newProcess) {
    Write-Step "❌ Failed to start API. Check logs for errors." "Red"
    exit 1
}

Write-Host ""

# Step 6: Monitor memory
Write-Step "Step 6: Monitoring memory usage..." "Cyan"
Monitor-Memory -DurationMinutes $MemoryMonitorDurationMinutes -IntervalSeconds $MemoryMonitorIntervalSeconds

Write-Host ""
Write-Step "=== Script Complete ===" "Green"
$elapsed = (Get-Date) - $script:startTime
Write-Host "Total execution time: $($elapsed.ToString('mm\:ss'))" -ForegroundColor Gray


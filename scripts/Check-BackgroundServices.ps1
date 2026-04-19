# Check which background services are running and their activity
# This helps identify which services might be causing memory leaks

param(
    [int]$SampleIntervalSeconds = 10,
    [int]$SampleCount = 6
)

$ErrorActionPreference = "Continue"

function Write-Step {
    param([string]$Message, [string]$Color = "Cyan")
    $timestamp = Get-Date -Format "HH:mm:ss"
    Write-Host "[$timestamp] $Message" -ForegroundColor $Color
}

Write-Step "=== Background Services Activity Check ===" "Cyan"
Write-Host ""

# Step 1: Check API Process
Write-Step "Step 1: Checking API Process..." "Cyan"
$apiProcess = Get-Process -Name "NickScanCentralImagingPortal.API" -ErrorAction SilentlyContinue
if ($apiProcess) {
    $memGB = [math]::Round($apiProcess.WS / 1GB, 2)
    $memMB = [math]::Round($apiProcess.WS / 1MB, 0)
    $uptime = (Get-Date) - $apiProcess.StartTime
    Write-Host "  Process ID: $($apiProcess.Id)" -ForegroundColor Gray
    Write-Host "  Memory: $memGB GB ($memMB MB)" -ForegroundColor $(if ($memGB -gt 20) { "Red" } else { "Yellow" })
    Write-Host "  Uptime: $($uptime.ToString('hh\:mm\:ss')) ($([math]::Round($uptime.TotalMinutes, 1)) minutes)" -ForegroundColor Gray
} else {
    Write-Step "❌ API process not found" "Red"
    exit 1
}

Write-Host ""

# Step 2: Check Log Files for Service Activity
Write-Step "Step 2: Checking Log Files for Service Activity..." "Cyan"

$logDirs = @(
    "src\NickScanCentralImagingPortal.API\logs",
    "logs",
    ".\logs"
)

$logFile = $null
foreach ($dir in $logDirs) {
    if (Test-Path $dir) {
        $logFile = Get-ChildItem $dir -Filter "*.log" -ErrorAction SilentlyContinue | 
            Sort-Object LastWriteTime -Descending | 
            Select-Object -First 1
        if ($logFile) {
            Write-Host "  Found log file: $($logFile.FullName)" -ForegroundColor Gray
            Write-Host "  Size: $([math]::Round($logFile.Length / 1MB, 2)) MB" -ForegroundColor Gray
            Write-Host "  Last Modified: $($logFile.LastWriteTime)" -ForegroundColor Gray
            break
        }
    }
}

if (-not $logFile) {
    Write-Step "⚠️  No log files found in standard locations" "Yellow"
    Write-Host "  Checked directories: $($logDirs -join ', ')" -ForegroundColor Gray
    Write-Host ""
    Write-Step "Step 3: Identifying Background Services from Code..." "Cyan"
} else {
    Write-Host ""
    
    # Check for ImageAnalysisOrchestrator activity
    Write-Step "Step 3: Analyzing Service Activity from Logs..." "Cyan"
    Write-Host "  Checking for ImageAnalysisOrchestrator activity..." -ForegroundColor Yellow
    $orchestratorLines = Get-Content $logFile.FullName -Tail 500 -ErrorAction SilentlyContinue | 
        Select-String -Pattern "ORCHESTRATOR|\[ORCHESTRATOR\]" | 
        Select-Object -Last 10
    if ($orchestratorLines) {
        Write-Host "  ✅ ImageAnalysisOrchestrator is active" -ForegroundColor Green
        $orchestratorLines | Select-Object -First 5 | ForEach-Object { 
            Write-Host "    $_" -ForegroundColor Gray 
        }
    } else {
        Write-Host "  ⚠️  No recent orchestrator activity" -ForegroundColor Yellow
    }
    Write-Host ""
    
    # Check for Intake workflow
    Write-Host "  Checking for Intake workflow..." -ForegroundColor Yellow
    $intakeLines = Get-Content $logFile.FullName -Tail 500 -ErrorAction SilentlyContinue | 
        Select-String -Pattern "\[INTAKE\]|Intake workflow|completenessRows" | 
        Select-Object -Last 10
    if ($intakeLines) {
        Write-Host "  ✅ Intake workflow is active" -ForegroundColor Green
        $intakeLines | Select-Object -First 5 | ForEach-Object { 
            Write-Host "    $_" -ForegroundColor Gray 
        }
    } else {
        Write-Host "  ⚠️  No recent intake activity" -ForegroundColor Yellow
    }
    Write-Host ""
    
    # Check for Assignment workflow
    Write-Host "  Checking for Assignment workflow..." -ForegroundColor Yellow
    $assignmentLines = Get-Content $logFile.FullName -Tail 500 -ErrorAction SilentlyContinue | 
        Select-String -Pattern "\[ASSIGNMENT\]|Assignment workflow" | 
        Select-Object -Last 10
    if ($assignmentLines) {
        Write-Host "  ✅ Assignment workflow is active" -ForegroundColor Green
        $assignmentLines | Select-Object -First 5 | ForEach-Object { 
            Write-Host "    $_" -ForegroundColor Gray 
        }
    } else {
        Write-Host "  ⚠️  No recent assignment activity" -ForegroundColor Yellow
    }
    Write-Host ""
    
    # Check for Container Completeness services
    Write-Host "  Checking for Container Completeness services..." -ForegroundColor Yellow
    $completenessLines = Get-Content $logFile.FullName -Tail 500 -ErrorAction SilentlyContinue | 
        Select-String -Pattern "COMPLETENESS|ContainerCompleteness" | 
        Select-Object -Last 10
    if ($completenessLines) {
        Write-Host "  ✅ Container Completeness services are active" -ForegroundColor Green
        $completenessLines | Select-Object -First 5 | ForEach-Object { 
            Write-Host "    $_" -ForegroundColor Gray 
        }
    } else {
        Write-Host "  ⚠️  No recent completeness activity" -ForegroundColor Yellow
    }
    Write-Host ""
    
    # Check for ICUMS/Download services
    Write-Host "  Checking for ICUMS/Download services..." -ForegroundColor Yellow
    $icumsLines = Get-Content $logFile.FullName -Tail 500 -ErrorAction SilentlyContinue | 
        Select-String -Pattern "ICUMS|Download|BOE|IcumPipeline" | 
        Select-Object -Last 10
    if ($icumsLines) {
        Write-Host "  ✅ ICUMS/Download services are active" -ForegroundColor Green
        $icumsLines | Select-Object -First 5 | ForEach-Object { 
            Write-Host "    $_" -ForegroundColor Gray 
        }
    } else {
        Write-Host "  ⚠️  No recent ICUMS activity" -ForegroundColor Yellow
    }
    Write-Host ""
    
    # Check for errors
    Write-Host "  Checking for errors/exceptions..." -ForegroundColor Yellow
    $errorLines = Get-Content $logFile.FullName -Tail 500 -ErrorAction SilentlyContinue | 
        Select-String -Pattern "Exception|Error|Failed|Memory|OutOfMemory" -CaseSensitive:$false | 
        Select-Object -Last 15
    if ($errorLines) {
        Write-Host "  ⚠️  Errors/exceptions found:" -ForegroundColor Yellow
        $errorLines | Select-Object -First 10 | ForEach-Object { 
            Write-Host "    $_" -ForegroundColor Red 
        }
    } else {
        Write-Host "  ✅ No recent errors found" -ForegroundColor Green
    }
}

Write-Host ""

# Step 4: List Known Background Services from Code
Write-Step "Step 4: Known Background Services (from codebase)..." "Cyan"
Write-Host ""
Write-Host "The following background services are registered:" -ForegroundColor White
Write-Host "  1. ImageAnalysisOrchestratorService" -ForegroundColor Gray
Write-Host "     - Intake workflow" -ForegroundColor DarkGray
Write-Host "     - Assignment workflow" -ForegroundColor DarkGray
Write-Host "     - Submission workflow" -ForegroundColor DarkGray
Write-Host "     - Housekeeping workflow" -ForegroundColor DarkGray
Write-Host ""
Write-Host "  2. ContainerCompletenessOrchestratorService" -ForegroundColor Gray
Write-Host "     - Completeness check workflow" -ForegroundColor DarkGray
Write-Host "     - Data mapping workflow" -ForegroundColor DarkGray
Write-Host "     - BOE selectivity workflow" -ForegroundColor DarkGray
Write-Host "     - Post-ICUMS validation workflow" -ForegroundColor DarkGray
Write-Host ""
Write-Host "  3. IcumPipelineOrchestratorService" -ForegroundColor Gray
Write-Host "     - ICUMS download processing" -ForegroundColor DarkGray
Write-Host "     - BOE document processing" -ForegroundColor DarkGray
Write-Host ""
Write-Host "  4. Other Services" -ForegroundColor Gray
Write-Host "     - ServiceHealthMonitor (singleton)" -ForegroundColor DarkGray
Write-Host "     - ReadyGroupsCacheService (singleton)" -ForegroundColor DarkGray

Write-Host ""
Write-Step "=== Script Complete ===" "Green"
Write-Host ""
Write-Host "Next Steps:" -ForegroundColor Cyan
Write-Host "  1. Use a memory profiler (dotMemory, PerfView, or VS Diagnostics)" -ForegroundColor White
Write-Host "  2. Check which services are processing large amounts of data" -ForegroundColor White
Write-Host "  3. Consider temporarily disabling non-critical services" -ForegroundColor White
Write-Host "  4. Monitor memory growth while services are active" -ForegroundColor White
Write-Host ""


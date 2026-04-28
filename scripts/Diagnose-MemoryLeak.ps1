# Diagnostic Script for Memory Leak Investigation
# Checks API logs, process status, and identifies potential leak sources

param(
    [int]$LogLines = 200,
    [int]$ProcessCheckIntervalSeconds = 5
)

# Continues past errors intentionally: memory-leak diagnostic samples process and logs at intervals — partial samples are still informative, hard-failing loses the trend signal.
$ErrorActionPreference = "Continue"
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
        return @{
            Process = $process
            MemoryGB = $memoryGB
            MemoryMB = $memoryMB
            Uptime = $uptime
        }
    }
    return $null
}

Write-Step "=== Memory Leak Diagnostic Script ===" "Cyan"
Write-Host ""

# Step 1: Check API Process Status
Write-Step "Step 1: Checking API Process Status..." "Cyan"
$processInfo = Get-ApiProcess
if ($processInfo) {
    Write-Host "  Process ID: $($processInfo.Process.Id)" -ForegroundColor Gray
    Write-Host "  Memory: $($processInfo.MemoryGB) GB ($($processInfo.MemoryMB) MB)" -ForegroundColor $(if ($processInfo.MemoryGB -gt 15) { "Red" } elseif ($processInfo.MemoryGB -gt 10) { "Yellow" } else { "Green" })
    Write-Host "  Uptime: $($processInfo.Uptime.ToString('hh\:mm\:ss'))" -ForegroundColor Gray
    Write-Host "  Start Time: $($processInfo.Process.StartTime)" -ForegroundColor Gray
} else {
    Write-Step "❌ API process not found" "Red"
    exit 1
}

Write-Host ""

# Step 2: Check Recent Logs for Workflow Activity
Write-Step "Step 2: Analyzing Recent API Logs..." "Cyan"

$logFiles = Get-ChildItem "src\NickScanCentralImagingPortal.API\logs\" -Filter "*.log" -ErrorAction SilentlyContinue | Sort-Object LastWriteTime -Descending
if ($logFiles) {
    $latestLog = $logFiles[0]
    Write-Host "  Latest log: $($latestLog.Name)" -ForegroundColor Gray
    Write-Host "  Size: $([math]::Round($latestLog.Length / 1MB, 2)) MB" -ForegroundColor Gray
    Write-Host "  Last Modified: $($latestLog.LastWriteTime)" -ForegroundColor Gray
    Write-Host ""
    
    # Check for orchestrator activity
    Write-Host "  Checking for Orchestrator activity..." -ForegroundColor Yellow
    $orchestratorLines = Get-Content $latestLog.FullName -Tail $LogLines | Select-String -Pattern "ORCHESTRATOR|\[ORCHESTRATOR\]" | Select-Object -Last 10
    if ($orchestratorLines) {
        Write-Host "  ✅ Orchestrator is active:" -ForegroundColor Green
        $orchestratorLines | ForEach-Object { Write-Host "    $_" -ForegroundColor Gray }
    } else {
        Write-Host "  ⚠️  No recent orchestrator activity found" -ForegroundColor Yellow
    }
    Write-Host ""
    
    # Check for Intake workflow
    Write-Host "  Checking for Intake workflow activity..." -ForegroundColor Yellow
    $intakeLines = Get-Content $latestLog.FullName -Tail $LogLines | Select-String -Pattern "\[INTAKE\]|Intake workflow" | Select-Object -Last 10
    if ($intakeLines) {
        Write-Host "  ✅ Intake workflow is active:" -ForegroundColor Green
        $intakeLines | Select-Object -First 5 | ForEach-Object { Write-Host "    $_" -ForegroundColor Gray }
    } else {
        Write-Host "  ⚠️  No recent intake activity found" -ForegroundColor Yellow
    }
    Write-Host ""
    
    # Check for Assignment workflow
    Write-Host "  Checking for Assignment workflow activity..." -ForegroundColor Yellow
    $assignmentLines = Get-Content $latestLog.FullName -Tail $LogLines | Select-String -Pattern "\[ASSIGNMENT\]|Assignment workflow" | Select-Object -Last 10
    if ($assignmentLines) {
        Write-Host "  ✅ Assignment workflow is active:" -ForegroundColor Green
        $assignmentLines | Select-Object -First 5 | ForEach-Object { Write-Host "    $_" -ForegroundColor Gray }
    } else {
        Write-Host "  ⚠️  No recent assignment activity found" -ForegroundColor Yellow
    }
    Write-Host ""
    
    # Check for Completeness workflow
    Write-Host "  Checking for Container Completeness activity..." -ForegroundColor Yellow
    $completenessLines = Get-Content $latestLog.FullName -Tail $LogLines | Select-String -Pattern "COMPLETENESS|ContainerCompleteness" | Select-Object -Last 10
    if ($completenessLines) {
        Write-Host "  ✅ Container Completeness is active:" -ForegroundColor Green
        $completenessLines | Select-Object -First 5 | ForEach-Object { Write-Host "    $_" -ForegroundColor Gray }
    } else {
        Write-Host "  ⚠️  No recent completeness activity found" -ForegroundColor Yellow
    }
    Write-Host ""
    
    # Check for ICUMS/Download activity
    Write-Host "  Checking for ICUMS/Download activity..." -ForegroundColor Yellow
    $icumsLines = Get-Content $latestLog.FullName -Tail $LogLines | Select-String -Pattern "ICUMS|Download|BOE" | Select-Object -Last 10
    if ($icumsLines) {
        Write-Host "  ✅ ICUMS/Download activity detected:" -ForegroundColor Green
        $icumsLines | Select-Object -First 5 | ForEach-Object { Write-Host "    $_" -ForegroundColor Gray }
    } else {
        Write-Host "  ⚠️  No recent ICUMS activity found" -ForegroundColor Yellow
    }
    Write-Host ""
    
    # Check for errors/exceptions
    Write-Host "  Checking for errors/exceptions..." -ForegroundColor Yellow
    $errorLines = Get-Content $latestLog.FullName -Tail $LogLines | Select-String -Pattern "Exception|Error|Failed|Memory|OutOfMemory" -CaseSensitive:$false | Select-Object -Last 20
    if ($errorLines) {
        Write-Host "  ⚠️  Errors/exceptions found:" -ForegroundColor Yellow
        $errorLines | Select-Object -First 10 | ForEach-Object { Write-Host "    $_" -ForegroundColor Red }
        if ($errorLines.Count -gt 10) {
            Write-Host "    ... and $($errorLines.Count - 10) more errors" -ForegroundColor Gray
        }
    } else {
        Write-Host "  ✅ No recent errors found" -ForegroundColor Green
    }
    Write-Host ""
    
    # Check for memory-related log entries
    Write-Host "  Checking for memory-related log entries..." -ForegroundColor Yellow
    $memoryLines = Get-Content $latestLog.FullName -Tail $LogLines | Select-String -Pattern "Memory|GC|Garbage|Collection|Heap" -CaseSensitive:$false | Select-Object -Last 10
    if ($memoryLines) {
        Write-Host "  ⚠️  Memory-related log entries found:" -ForegroundColor Yellow
        $memoryLines | ForEach-Object { Write-Host "    $_" -ForegroundColor Gray }
    } else {
        Write-Host "  ℹ️  No memory-related log entries found" -ForegroundColor Gray
    }
} else {
    Write-Step "⚠️  No log files found" "Yellow"
}

Write-Host ""

# Step 3: Check for HttpClient errors (the URI error)
Write-Step "Step 3: Checking for HttpClient/URI Errors..." "Cyan"
if ($logFiles) {
    $latestLog = $logFiles[0]
    $uriErrors = Get-Content $latestLog.FullName -Tail $LogLines | Select-String -Pattern "invalid request URI|BaseAddress|absolute URI" -CaseSensitive:$false | Select-Object -Last 10
    if ($uriErrors) {
        Write-Host "  ⚠️  HttpClient/URI errors found:" -ForegroundColor Yellow
        $uriErrors | ForEach-Object { Write-Host "    $_" -ForegroundColor Red }
    } else {
        Write-Host "  ✅ No HttpClient/URI errors in recent logs" -ForegroundColor Green
    }
}

Write-Host ""

# Step 4: Monitor Memory Growth
$monitorDuration = 30
Write-Step "Step 4: Monitoring Memory Growth..." "Cyan"
Write-Host "  Checking memory every $ProcessCheckIntervalSeconds seconds..." -ForegroundColor Gray
Write-Host ""

$initialInfo = Get-ApiProcess
if ($initialInfo) {
    $initialMemory = $initialInfo.MemoryMB
    $initialMemoryGB = [math]::Round($initialMemory / 1024, 2)
    Write-Host "  Initial Memory: $initialMemoryGB GB ($initialMemory MB)" -ForegroundColor Gray
    Write-Host ""
    
    $checks = 0
    $maxChecks = 6
    while ($checks -lt $maxChecks) {
        Start-Sleep -Seconds $ProcessCheckIntervalSeconds
        $currentInfo = Get-ApiProcess
        if ($currentInfo) {
            $currentMemory = $currentInfo.MemoryMB
            $change = $currentMemory - $initialMemory
            if ($change -ge 0) {
                $changeStr = "+$change"
            } else {
                $changeStr = "$change"
            }
            $changeColor = if ($change -gt 100) { "Red" } elseif ($change -gt 50) { "Yellow" } else { "Green" }
            
            $currentMemoryGB = [math]::Round($currentMemory / 1024, 2)
            Write-Host "  Check $($checks + 1)/$maxChecks: $currentMemoryGB GB ($currentMemory MB) - Change: $changeStr MB" -ForegroundColor $changeColor
        } else {
            Write-Host "  ⚠️  API process no longer running" -ForegroundColor Yellow
            break
        }
        $checks++
    }
    
    $finalInfo = Get-ApiProcess
    if ($finalInfo) {
        $finalMemory = $finalInfo.MemoryMB
        $totalChange = $finalMemory - $initialMemory
        $ratePerMinute = ($totalChange / ($maxChecks * $ProcessCheckIntervalSeconds)) * 60
        
        $finalMemoryGB = [math]::Round($finalMemory / 1024, 2)
        $totalChangeGB = [math]::Round($totalChange / 1024, 2)
        $ratePerMinuteRounded = [math]::Round($ratePerMinute, 0)
        
        Write-Host ""
        Write-Host "  Summary:" -ForegroundColor Cyan
        Write-Host "    Initial: $initialMemoryGB GB ($initialMemory MB)" -ForegroundColor Gray
        Write-Host "    Final: $finalMemoryGB GB ($finalMemory MB)" -ForegroundColor Gray
        $changeColor = if ($totalChange -gt 500) { "Red" } elseif ($totalChange -gt 200) { "Yellow" } else { "Green" }
        Write-Host "    Change: $totalChangeGB GB ($totalChange MB)" -ForegroundColor $changeColor
        $rateColor = if ($ratePerMinute -gt 1000) { "Red" } elseif ($ratePerMinute -gt 500) { "Yellow" } else { "Green" }
        Write-Host "    Rate: $ratePerMinuteRounded MB/min" -ForegroundColor $rateColor
        
        if ($ratePerMinute -gt 1000) {
            Write-Host ""
            Write-Step "⚠️  WARNING: Memory growth rate is very high ($ratePerMinuteRounded MB/min)!" "Red"
            Write-Step "This indicates a critical memory leak that needs immediate attention." "Red"
        }
    }
}

Write-Host ""
Write-Step "=== Diagnostic Complete ===" "Green"
$elapsed = (Get-Date) - $script:startTime
Write-Host "Total execution time: $($elapsed.ToString('mm\:ss'))" -ForegroundColor Gray
Write-Host ""

# Recommendations
Write-Step "Recommendations:" "Cyan"
Write-Host "  1. Review the logs above to identify active workflows" -ForegroundColor White
Write-Host "  2. Check if any background services are processing large datasets" -ForegroundColor White
Write-Host "  3. Consider temporarily disabling non-critical background services" -ForegroundColor White
Write-Host "  4. Use a memory profiler (dotMemory, PerfView, or Visual Studio Diagnostics) for detailed analysis" -ForegroundColor White
Write-Host "  5. Check for unbounded queries loading entire tables" -ForegroundColor White
Write-Host "  6. Verify ChangeTracker.Clear() is being called after all SaveChangesAsync() calls" -ForegroundColor White
Write-Host ""


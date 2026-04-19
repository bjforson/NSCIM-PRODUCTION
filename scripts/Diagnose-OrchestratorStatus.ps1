# Diagnostic script to check orchestrator status and identify why assignments aren't flowing
# Run this to check if orchestrator is stuck or if there are database issues

param(
    [string]$ServerInstance = "localhost",
    [string]$Database = "NickScanCentralImagingPortal",
    [string]$LogFile = "C:\Users\Administrator\Documents\GitHub\NICKSCAN-CENTRAL--IMAGE-PORTAL\src\NickScanCentralImagingPortal.API\logs\nickscan-20260109.txt"
)

Write-Host "================================================" -ForegroundColor Cyan
Write-Host "ORCHESTRATOR STATUS DIAGNOSTIC" -ForegroundColor Cyan
Write-Host "================================================" -ForegroundColor Cyan
Write-Host ""

# Check 1: Recent orchestrator logs
Write-Host "[1] Checking recent orchestrator activity..." -ForegroundColor Yellow
if (Test-Path $LogFile) {
    $recentOrchestrator = Get-Content $LogFile | Select-String -Pattern "\[ORCHESTRATOR\]|\[ASSIGNMENT-POLLING\]|\[ASSIGNMENT\].*=====" | Select-Object -Last 10
    if ($recentOrchestrator) {
        Write-Host "Recent orchestrator logs found:" -ForegroundColor Green
        $recentOrchestrator | ForEach-Object { Write-Host "  $_" -ForegroundColor Gray }
    } else {
        Write-Host "  WARNING: No recent orchestrator logs found!" -ForegroundColor Red
    }
} else {
    Write-Host "  ERROR: Log file not found: $LogFile" -ForegroundColor Red
}
Write-Host ""

# Check 2: AnalysisSettings
Write-Host "[2] Checking AnalysisSettings..." -ForegroundColor Yellow
$settingsQuery = @"
SELECT TOP 1 Enabled, AssignmentMode, MaxConcurrentPerUser, LeaseMinutes
FROM AnalysisSettings
"@
try {
    $settings = Invoke-Sqlcmd -ServerInstance $ServerInstance -Database $Database -Query $settingsQuery -ErrorAction Stop
    if ($settings) {
        Write-Host "  Settings found:" -ForegroundColor Green
        Write-Host "    Enabled: $($settings.Enabled)" -ForegroundColor $(if ($settings.Enabled) { "Green" } else { "Red" })
        Write-Host "    AssignmentMode: $($settings.AssignmentMode)" -ForegroundColor Gray
        Write-Host "    MaxConcurrentPerUser: $($settings.MaxConcurrentPerUser)" -ForegroundColor Gray
        Write-Host "    LeaseMinutes: $($settings.LeaseMinutes)" -ForegroundColor Gray
    } else {
        Write-Host "  WARNING: No AnalysisSettings record found!" -ForegroundColor Red
    }
} catch {
    Write-Host "  ERROR: Could not query AnalysisSettings: $_" -ForegroundColor Red
}
Write-Host ""

# Check 3: Ready Groups Count
Write-Host "[3] Checking Ready Groups count..." -ForegroundColor Yellow
$readyGroupsQuery = @"
SELECT COUNT(*) as ReadyCount
FROM AnalysisGroups
WHERE Status = 'Ready'
"@
try {
    $readyCount = Invoke-Sqlcmd -ServerInstance $ServerInstance -Database $Database -Query $readyGroupsQuery -ErrorAction Stop
    Write-Host "  Ready Groups: $($readyCount.ReadyCount)" -ForegroundColor $(if ($readyCount.ReadyCount -gt 0) { "Green" } else { "Yellow" })
} catch {
    Write-Host "  ERROR: Could not query Ready Groups: $_" -ForegroundColor Red
}
Write-Host ""

# Check 4: UserReadiness for analyst1
Write-Host "[4] Checking UserReadiness for analyst1..." -ForegroundColor Yellow
$readinessQuery = @"
SELECT Username, Role, IsReady, LastHeartbeat,
       DATEDIFF(SECOND, LastHeartbeat, GETUTCDATE()) as SecondsSinceHeartbeat
FROM UserReadiness
WHERE Username = 'analyst1' AND Role = 'Analyst'
"@
try {
    $readiness = Invoke-Sqlcmd -ServerInstance $ServerInstance -Database $Database -Query $readinessQuery -ErrorAction Stop
    if ($readiness) {
        Write-Host "  UserReadiness found:" -ForegroundColor Green
        Write-Host "    IsReady: $($readiness.IsReady)" -ForegroundColor $(if ($readiness.IsReady) { "Green" } else { "Red" })
        Write-Host "    LastHeartbeat: $($readiness.LastHeartbeat)" -ForegroundColor Gray
        Write-Host "    SecondsSinceHeartbeat: $($readiness.SecondsSinceHeartbeat)" -ForegroundColor $(if ($readiness.SecondsSinceHeartbeat -lt 120) { "Green" } else { "Red" })
    } else {
        Write-Host "  WARNING: No UserReadiness record for analyst1!" -ForegroundColor Red
    }
} catch {
    Write-Host "  ERROR: Could not query UserReadiness: $_" -ForegroundColor Red
}
Write-Host ""

# Check 5: Active Assignments
Write-Host "[5] Checking Active Assignments..." -ForegroundColor Yellow
$assignmentsQuery = @"
SELECT COUNT(*) as ActiveCount
FROM AnalysisAssignments
WHERE State = 'Active'
  AND AssignedTo = 'analyst1'
  AND Role = 'Analyst'
"@
try {
    $assignments = Invoke-Sqlcmd -ServerInstance $ServerInstance -Database $Database -Query $assignmentsQuery -ErrorAction Stop
    Write-Host "  Active Assignments for analyst1: $($assignments.ActiveCount)" -ForegroundColor Gray
} catch {
    Write-Host "  ERROR: Could not query Assignments: $_" -ForegroundColor Red
}
Write-Host ""

# Check 6: Work Count Queries Performance
Write-Host "[6] Testing work count query performance..." -ForegroundColor Yellow
$workCountQuery = @"
-- Simulate GetAssignmentWorkCountAsync
SELECT COUNT(*) as ReadyCount
FROM AnalysisGroups
WHERE Status = 'Ready' OR Status = 'Ready'
"@
try {
    $startTime = Get-Date
    $workCount = Invoke-Sqlcmd -ServerInstance $ServerInstance -Database $Database -Query $workCountQuery -ErrorAction Stop -QueryTimeout 10
    $elapsed = (Get-Date) - $startTime
    Write-Host "  Work count query completed in $($elapsed.TotalMilliseconds)ms" -ForegroundColor $(if ($elapsed.TotalSeconds -lt 5) { "Green" } else { "Yellow" })
    Write-Host "  Result: $($workCount.ReadyCount) Ready groups" -ForegroundColor Gray
} catch {
    Write-Host "  ERROR: Work count query failed or timed out: $_" -ForegroundColor Red
}
Write-Host ""

# Check 7: Recent Assignment Workflow Activity
Write-Host "[7] Checking for recent assignment workflow activity..." -ForegroundColor Yellow
if (Test-Path $LogFile) {
    $lastAssignment = Get-Content $LogFile | Select-String -Pattern "\[ASSIGNMENT\].*=====" | Select-Object -Last 1
    if ($lastAssignment) {
        Write-Host "  Last assignment workflow:" -ForegroundColor Green
        Write-Host "    $lastAssignment" -ForegroundColor Gray
    } else {
        Write-Host "  WARNING: No assignment workflow logs found!" -ForegroundColor Red
    }
} else {
    Write-Host "  ERROR: Log file not found" -ForegroundColor Red
}
Write-Host ""

Write-Host "================================================" -ForegroundColor Cyan
Write-Host "DIAGNOSTIC COMPLETE" -ForegroundColor Cyan
Write-Host "================================================" -ForegroundColor Cyan


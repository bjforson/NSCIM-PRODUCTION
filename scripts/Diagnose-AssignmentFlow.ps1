# Comprehensive Assignment Flow Diagnostic
# Checks why assignments aren't coming through even with signed-in analyst

param(
    [string]$Server = "127.0.0.1,1433",
    [string]$Database = "NS_CIS"
)

# Continues past errors intentionally: comprehensive diagnostic running ~10 independent assignment-flow checks; failing fast at check 1 hides subsequent issues.
$ErrorActionPreference = "Continue"

Write-Host "Assignment Flow Diagnostic - Signed-In Analyst" -ForegroundColor Cyan
Write-Host "==============================================" -ForegroundColor Cyan
Write-Host ""

# Check 1: User Readiness for Analysts
Write-Host "Check 1: Analyst User Readiness" -ForegroundColor Yellow
Write-Host "-----------------------------" -ForegroundColor Yellow

$readinessQuery = @"
SELECT 
    ur.Username,
    ur.Role,
    ur.IsReady,
    ur.LastHeartbeat,
    DATEDIFF(second, ur.LastHeartbeat, GETUTCDATE()) AS SecondsSinceHeartbeat,
    CASE 
        WHEN ur.LastHeartbeat > DATEADD(minute, -2, GETUTCDATE()) THEN 'ACTIVE'
        WHEN ur.LastHeartbeat > DATEADD(minute, -5, GETUTCDATE()) THEN 'IDLE'
        ELSE 'STALE'
    END AS HeartbeatStatus
FROM UserReadiness ur
INNER JOIN Users u ON u.UserName = ur.Username
INNER JOIN Roles r ON r.Id = u.RoleId
WHERE r.Name = 'Analyst' AND u.IsActive = 1
ORDER BY ur.LastHeartbeat DESC
"@

try {
    $readiness = Invoke-Sqlcmd -ServerInstance $Server -Database $Database -Query $readinessQuery
    
    if ($readiness) {
        Write-Host "Analyst Readiness Status:" -ForegroundColor Cyan
        $readiness | Format-Table -AutoSize
        Write-Host ""
        
        $readyCount = ($readiness | Where-Object { $_.IsReady -eq $true -and $_.SecondsSinceHeartbeat -lt 120 }).Count
        if ($readyCount -gt 0) {
            Write-Host "SUCCESS: $readyCount analyst(s) are READY (IsReady=true, heartbeat < 2 min)" -ForegroundColor Green
        } else {
            Write-Host "ISSUE: No analysts are READY" -ForegroundColor Red
            Write-Host "  Analysts must have IsReady=true AND heartbeat < 2 minutes" -ForegroundColor Yellow
        }
    } else {
        Write-Host "ISSUE: No UserReadiness records found for analysts" -ForegroundColor Red
        Write-Host "  Analysts need to be logged in to create UserReadiness records" -ForegroundColor Yellow
    }
} catch {
    Write-Host "ERROR: Could not query UserReadiness" -ForegroundColor Red
    Write-Host "  Error: $($_.Exception.Message)" -ForegroundColor Red
}
Write-Host ""

# Check 2: Ready Groups Available
Write-Host "Check 2: Ready Groups Available" -ForegroundColor Yellow
Write-Host "-----------------------------" -ForegroundColor Yellow

$readyGroupsQuery = @"
SELECT 
    COUNT(*) AS ReadyCount,
    MIN(CreatedAtUtc) AS OldestReady,
    MAX(CreatedAtUtc) AS NewestReady
FROM AnalysisGroups
WHERE Status = 'Ready'
"@

$readyGroups = Invoke-Sqlcmd -ServerInstance $Server -Database $Database -Query $readyGroupsQuery

if ($readyGroups.ReadyCount -gt 0) {
    Write-Host "SUCCESS: $($readyGroups.ReadyCount) Ready groups available" -ForegroundColor Green
    Write-Host "  Oldest: $($readyGroups.OldestReady)" -ForegroundColor White
    Write-Host "  Newest: $($readyGroups.NewestReady)" -ForegroundColor White
} else {
    Write-Host "ISSUE: No Ready groups available" -ForegroundColor Red
}
Write-Host ""

# Check 3: Active Assignments per Analyst
Write-Host "Check 3: Active Assignments per Analyst" -ForegroundColor Yellow
Write-Host "--------------------------------------" -ForegroundColor Yellow

$activeAssignmentsQuery = @"
SELECT 
    aa.AssignedTo,
    COUNT(*) AS ActiveCount,
    MAX(aa.LeaseUntilUtc) AS LatestLease,
    CASE WHEN MAX(aa.LeaseUntilUtc) < GETUTCDATE() THEN 'EXPIRED' ELSE 'ACTIVE' END AS LeaseStatus
FROM AnalysisAssignments aa
INNER JOIN Users u ON u.UserName = aa.AssignedTo
INNER JOIN Roles r ON r.Id = u.RoleId
WHERE aa.Role = 'Analyst' 
    AND aa.State = 'Active'
    AND r.Name = 'Analyst'
GROUP BY aa.AssignedTo
ORDER BY ActiveCount DESC
"@

$activeAssignments = Invoke-Sqlcmd -ServerInstance $Server -Database $Database -Query $activeAssignmentsQuery

if ($activeAssignments) {
    Write-Host "Active assignments per analyst:" -ForegroundColor Cyan
    $activeAssignments | Format-Table -AutoSize
    
    $settingsQuery = "SELECT MaxConcurrentPerUser FROM AnalysisSettings"
    $settings = Invoke-Sqlcmd -ServerInstance $Server -Database $Database -Query $settingsQuery
    $maxConcurrent = $settings.MaxConcurrentPerUser
    
    Write-Host "Max Concurrent per User: $maxConcurrent" -ForegroundColor Cyan
    
    foreach ($analyst in $activeAssignments) {
        if ($analyst.ActiveCount -ge $maxConcurrent) {
            Write-Host "WARNING: $($analyst.AssignedTo) is at max capacity ($($analyst.ActiveCount) >= $maxConcurrent)" -ForegroundColor Yellow
        }
    }
} else {
    Write-Host "INFO: No active assignments found" -ForegroundColor Gray
}
Write-Host ""

# Check 4: Expired Assignments Blocking
Write-Host "Check 4: Expired Assignments" -ForegroundColor Yellow
Write-Host "--------------------------" -ForegroundColor Yellow

$expiredQuery = @"
SELECT 
    COUNT(*) AS ExpiredCount,
    MIN(LeaseUntilUtc) AS OldestExpired,
    MAX(LeaseUntilUtc) AS NewestExpired
FROM AnalysisAssignments
WHERE State = 'Active'
    AND LeaseUntilUtc < GETUTCDATE()
"@

$expired = Invoke-Sqlcmd -ServerInstance $Server -Database $Database -Query $expiredQuery

if ($expired.ExpiredCount -gt 0) {
    Write-Host "ISSUE: $($expired.ExpiredCount) expired assignments still marked as 'Active'" -ForegroundColor Red
    Write-Host "  These should be cleaned up by AssignmentWorker" -ForegroundColor Yellow
    Write-Host "  Oldest expired: $($expired.OldestExpired)" -ForegroundColor White
    Write-Host "  Newest expired: $($expired.NewestExpired)" -ForegroundColor White
} else {
    Write-Host "SUCCESS: No expired active assignments" -ForegroundColor Green
}
Write-Host ""

# Check 5: Groups with Active Assignments (shouldn't be Ready)
Write-Host "Check 5: Ready Groups with Active Assignments" -ForegroundColor Yellow
Write-Host "--------------------------------------------" -ForegroundColor Yellow

$readyWithAssignmentsQuery = @"
SELECT COUNT(*) AS Count
FROM AnalysisGroups ag
INNER JOIN AnalysisAssignments aa ON aa.GroupId = ag.Id
WHERE ag.Status = 'Ready'
    AND aa.State = 'Active'
    AND (aa.LeaseUntilUtc IS NULL OR aa.LeaseUntilUtc > GETUTCDATE())
"@

$readyWithAssignments = Invoke-Sqlcmd -ServerInstance $Server -Database $Database -Query $readyWithAssignmentsQuery

if ($readyWithAssignments.Count -gt 0) {
    Write-Host "WARNING: $($readyWithAssignments.Count) Ready groups have active assignments" -ForegroundColor Yellow
    Write-Host "  These groups should have Status='AnalystAssigned', not 'Ready'" -ForegroundColor Yellow
} else {
    Write-Host "SUCCESS: No Ready groups have active assignments" -ForegroundColor Green
}
Write-Host ""

# Check 6: Recent Assignment Activity
Write-Host "Check 6: Recent Assignment Activity" -ForegroundColor Yellow
Write-Host "----------------------------------" -ForegroundColor Yellow

$recentAssignmentsQuery = @"
SELECT TOP 10
    aa.Id,
    aa.AssignedTo,
    aa.Role,
    aa.State,
    aa.CreatedAtUtc,
    aa.LeaseUntilUtc,
    ag.GroupIdentifier,
    DATEDIFF(minute, aa.CreatedAtUtc, GETUTCDATE()) AS MinutesAgo
FROM AnalysisAssignments aa
INNER JOIN AnalysisGroups ag ON ag.Id = aa.GroupId
WHERE aa.Role = 'Analyst'
ORDER BY aa.CreatedAtUtc DESC
"@

$recentAssignments = Invoke-Sqlcmd -ServerInstance $Server -Database $Database -Query $recentAssignmentsQuery

if ($recentAssignments) {
    Write-Host "Most recent assignments:" -ForegroundColor Cyan
    $recentAssignments | Format-Table -Property AssignedTo, State, GroupIdentifier, MinutesAgo -AutoSize
    
    $mostRecent = ($recentAssignments | Select-Object -First 1).MinutesAgo
    if ($mostRecent -lt 5) {
        Write-Host "SUCCESS: Recent assignment activity (last $mostRecent minutes ago)" -ForegroundColor Green
    } elseif ($mostRecent -lt 60) {
        Write-Host "WARNING: No recent assignments (last $mostRecent minutes ago)" -ForegroundColor Yellow
    } else {
        Write-Host "ISSUE: No recent assignments (last $mostRecent minutes ago)" -ForegroundColor Red
        Write-Host "  AssignmentWorker may not be running or processing" -ForegroundColor Yellow
    }
} else {
    Write-Host "ISSUE: No assignment history found" -ForegroundColor Red
}
Write-Host ""

# Summary
Write-Host "========================================" -ForegroundColor Cyan
Write-Host "DIAGNOSIS SUMMARY" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""

$issues = @()
$warnings = @()

if (-not $readiness -or ($readiness | Where-Object { $_.IsReady -eq $true -and $_.SecondsSinceHeartbeat -lt 120 }).Count -eq 0) {
    $issues += "No analysts are READY (IsReady=true AND heartbeat < 2 min)"
}

if ($expired.ExpiredCount -gt 1000) {
    $warnings += "$($expired.ExpiredCount) expired assignments need cleanup"
}

if ($readyGroups.ReadyCount -eq 0) {
    $issues += "No Ready groups available for assignment"
}

if ($recentAssignments) {
    $mostRecent = ($recentAssignments | Select-Object -First 1).MinutesAgo
    if ($mostRecent -gt 60) {
        $issues += "No recent assignment activity (last $mostRecent minutes ago)"
    }
}

if ($issues.Count -eq 0 -and $warnings.Count -eq 0) {
    Write-Host "SUCCESS: All checks passed!" -ForegroundColor Green
    Write-Host ""
    Write-Host "If assignments still not working, check:" -ForegroundColor Yellow
    Write-Host "  1. Application logs for AssignmentWorker errors" -ForegroundColor White
    Write-Host "  2. WorkflowStage of containers (must be 'ImageAnalysis' or 'Pending')" -ForegroundColor White
    Write-Host "  3. AssignmentWorker service is running" -ForegroundColor White
} else {
    if ($issues.Count -gt 0) {
        Write-Host "CRITICAL ISSUES:" -ForegroundColor Red
        foreach ($issue in $issues) {
            Write-Host "  - $issue" -ForegroundColor Red
        }
        Write-Host ""
    }
    
    if ($warnings.Count -gt 0) {
        Write-Host "WARNINGS:" -ForegroundColor Yellow
        foreach ($warning in $warnings) {
            Write-Host "  - $warning" -ForegroundColor Yellow
        }
        Write-Host ""
    }
}

Write-Host ""


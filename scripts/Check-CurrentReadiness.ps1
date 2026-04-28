# Check Current Analyst Readiness - Real-time check
# Run this while analyst is actively on the page

param(
    [string]$Server = "127.0.0.1,1433",
    [string]$Database = "NS_CIS"
)

# Continues past errors intentionally: read-only diagnostic listing all analysts and their readiness; report continues even if individual queries fail.
$ErrorActionPreference = "Continue"

Write-Host "Real-Time Analyst Readiness Check" -ForegroundColor Cyan
Write-Host "=================================" -ForegroundColor Cyan
Write-Host ""

# Check current readiness
$readinessQuery = @"
SELECT 
    ur.Username,
    ur.Role,
    ur.IsReady,
    ur.LastHeartbeat,
    DATEDIFF(second, ur.LastHeartbeat, GETUTCDATE()) AS SecondsAgo,
    CASE 
        WHEN ur.LastHeartbeat > DATEADD(minute, -2, GETUTCDATE()) AND ur.IsReady = 1 THEN 'READY'
        WHEN ur.LastHeartbeat > DATEADD(minute, -2, GETUTCDATE()) AND ur.IsReady = 0 THEN 'NOT READY (IsReady=false)'
        WHEN ur.LastHeartbeat > DATEADD(minute, -5, GETUTCDATE()) THEN 'IDLE'
        ELSE 'STALE'
    END AS Status
FROM UserReadiness ur
INNER JOIN Users u ON u.UserName = ur.Username
INNER JOIN Roles r ON r.Id = u.RoleId
WHERE r.Name = 'Analyst' AND u.IsActive = 1
ORDER BY ur.LastHeartbeat DESC
"@

$readiness = Invoke-Sqlcmd -ServerInstance $Server -Database $Database -Query $readinessQuery

Write-Host "Current Analyst Readiness:" -ForegroundColor Yellow
Write-Host "------------------------" -ForegroundColor Yellow

if ($readiness) {
    $readiness | Format-Table -AutoSize
    Write-Host ""
    
    $readyAnalysts = $readiness | Where-Object { 
        $_.IsReady -eq $true -and $_.SecondsAgo -lt 120 
    }
    
    if ($readyAnalysts) {
        Write-Host "SUCCESS: $($readyAnalysts.Count) analyst(s) are READY" -ForegroundColor Green
        foreach ($analyst in $readyAnalysts) {
            Write-Host "  - $($analyst.Username): Ready (heartbeat $($analyst.SecondsAgo) seconds ago)" -ForegroundColor Green
        }
    } else {
        Write-Host "ISSUE: No analysts are READY" -ForegroundColor Red
        Write-Host ""
        Write-Host "Requirements for READY status:" -ForegroundColor Yellow
        Write-Host "  1. IsReady = true" -ForegroundColor White
        Write-Host "  2. LastHeartbeat < 120 seconds ago" -ForegroundColor White
        Write-Host ""
        Write-Host "Current status:" -ForegroundColor Cyan
        foreach ($analyst in $readiness) {
            $status = if ($analyst.IsReady -eq $true) { "IsReady=true" } else { "IsReady=false" }
            $heartbeat = if ($analyst.SecondsAgo -lt 120) { "Heartbeat OK" } else { "Heartbeat STALE ($($analyst.SecondsAgo) seconds)" }
            Write-Host "  - $($analyst.Username): $status, $heartbeat" -ForegroundColor White
        }
    }
} else {
    Write-Host "ISSUE: No UserReadiness records found for analysts" -ForegroundColor Red
    Write-Host "  This means the analyst hasn't created a UserReadiness record yet" -ForegroundColor Yellow
    Write-Host "  The page may need to be refreshed or SignalR connection established" -ForegroundColor Yellow
}

Write-Host ""

# Check for very recent assignments (last 5 minutes)
Write-Host "Recent Assignment Activity (Last 5 Minutes):" -ForegroundColor Yellow
Write-Host "-------------------------------------------" -ForegroundColor Yellow

$recentAssignmentsQuery = @"
SELECT 
    aa.Id,
    aa.AssignedTo,
    aa.Role,
    aa.State,
    aa.CreatedAtUtc,
    ag.GroupIdentifier,
    DATEDIFF(second, aa.CreatedAtUtc, GETUTCDATE()) AS SecondsAgo
FROM AnalysisAssignments aa
INNER JOIN AnalysisGroups ag ON ag.Id = aa.GroupId
WHERE aa.Role = 'Analyst'
    AND aa.CreatedAtUtc > DATEADD(minute, -5, GETUTCDATE())
ORDER BY aa.CreatedAtUtc DESC
"@

$recentAssignments = Invoke-Sqlcmd -ServerInstance $Server -Database $Database -Query $recentAssignmentsQuery

if ($recentAssignments) {
    Write-Host "SUCCESS: Found $($recentAssignments.Count) assignment(s) in last 5 minutes!" -ForegroundColor Green
    $recentAssignments | Format-Table -Property AssignedTo, GroupIdentifier, State, SecondsAgo -AutoSize
} else {
    Write-Host "No assignments created in last 5 minutes" -ForegroundColor Yellow
    Write-Host ""
    Write-Host "Possible reasons:" -ForegroundColor Cyan
    Write-Host "  1. Analyst not READY (check above)" -ForegroundColor White
    Write-Host "  2. AssignmentWorker not running" -ForegroundColor White
    Write-Host "  3. No Ready groups available" -ForegroundColor White
    Write-Host "  4. AssignmentWorker errors in logs" -ForegroundColor White
}

Write-Host ""

# Check analyst active assignment count (should be < 5 now)
Write-Host "Analyst Active Assignment Count:" -ForegroundColor Yellow
Write-Host "-----------------------------" -ForegroundColor Yellow

$activeCountQuery = @"
SELECT 
    aa.AssignedTo,
    COUNT(*) AS ActiveCount
FROM AnalysisAssignments aa
INNER JOIN Users u ON u.UserName = aa.AssignedTo
INNER JOIN Roles r ON r.Id = u.RoleId
WHERE aa.Role = 'Analyst'
    AND aa.State = 'Active'
    AND (aa.LeaseUntilUtc IS NULL OR aa.LeaseUntilUtc > GETUTCDATE())
    AND r.Name = 'Analyst'
GROUP BY aa.AssignedTo
"@

$activeCounts = Invoke-Sqlcmd -ServerInstance $Server -Database $Database -Query $activeCountQuery

if ($activeCounts) {
    $settingsQuery = "SELECT MaxConcurrentPerUser FROM AnalysisSettings"
    $settings = Invoke-Sqlcmd -ServerInstance $Server -Database $Database -Query $settingsQuery
    $maxConcurrent = $settings.MaxConcurrentPerUser
    
    Write-Host "Active assignments per analyst (Max: $maxConcurrent):" -ForegroundColor Cyan
    $activeCounts | Format-Table -AutoSize
    
    foreach ($analyst in $activeCounts) {
        if ($analyst.ActiveCount -ge $maxConcurrent) {
            Write-Host "WARNING: $($analyst.AssignedTo) is at max capacity ($($analyst.ActiveCount) >= $maxConcurrent)" -ForegroundColor Yellow
        } else {
            Write-Host "OK: $($analyst.AssignedTo) has $($analyst.ActiveCount) active assignments (below max)" -ForegroundColor Green
        }
    }
} else {
    Write-Host "SUCCESS: No active assignments (analysts are available for new assignments)" -ForegroundColor Green
}

Write-Host ""


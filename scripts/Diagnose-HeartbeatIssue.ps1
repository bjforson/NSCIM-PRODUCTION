# Diagnose why heartbeat isn't updating even when user is on page

param(
    [string]$Server = "127.0.0.1,1433",
    [string]$Database = "NS_CIS",
    [string]$Username = "analyst"
)

$ErrorActionPreference = "Continue"

Write-Host "Heartbeat Issue Diagnosis" -ForegroundColor Cyan
Write-Host "========================" -ForegroundColor Cyan
Write-Host ""

Write-Host "Current Heartbeat Status:" -ForegroundColor Yellow
Write-Host "-----------------------" -ForegroundColor Yellow

$readinessQuery = @"
SELECT 
    Username,
    Role,
    IsReady,
    LastHeartbeat,
    DATEDIFF(second, LastHeartbeat, GETUTCDATE()) AS SecondsAgo,
    CASE 
        WHEN LastHeartbeat > DATEADD(minute, -2, GETUTCDATE()) THEN 'ACTIVE'
        WHEN LastHeartbeat > DATEADD(minute, -5, GETUTCDATE()) THEN 'IDLE'
        ELSE 'STALE'
    END AS Status
FROM UserReadiness
WHERE Username = '$Username' AND Role = 'Analyst'
"@

$readiness = Invoke-Sqlcmd -ServerInstance $Server -Database $Database -Query $readinessQuery

if ($readiness) {
    $readiness | Format-Table -AutoSize
    Write-Host ""
    
    if ($readiness.SecondsAgo -ge 120) {
        Write-Host "ISSUE: Heartbeat is STALE ($($readiness.SecondsAgo) seconds ago)" -ForegroundColor Red
        Write-Host ""
        Write-Host "Possible causes:" -ForegroundColor Yellow
        Write-Host "  1. Browser not calling heartbeat API" -ForegroundColor White
        Write-Host "  2. API endpoint returning error (401, 403, 500)" -ForegroundColor White
        Write-Host "  3. SignalR connection not established" -ForegroundColor White
        Write-Host "  4. JavaScript errors on page" -ForegroundColor White
        Write-Host "  5. Authentication token expired" -ForegroundColor White
        Write-Host ""
        Write-Host "SOLUTION: Check browser console and network tab" -ForegroundColor Cyan
        Write-Host "  - Open Developer Tools (F12)" -ForegroundColor White
        Write-Host "  - Go to Network tab" -ForegroundColor White
        Write-Host "  - Filter by 'heartbeat'" -ForegroundColor White
        Write-Host "  - Check if POST /api/image-analysis/user/heartbeat is being called" -ForegroundColor White
        Write-Host "  - Check response status (should be 200 OK)" -ForegroundColor White
        Write-Host "  - Check Console tab for JavaScript errors" -ForegroundColor White
    }
} else {
    Write-Host "ISSUE: No UserReadiness record found" -ForegroundColor Red
    Write-Host "  The page needs to call POST /api/image-analysis/user/ready first" -ForegroundColor Yellow
}

Write-Host ""

# Manual heartbeat update (temporary workaround)
Write-Host "Temporary Workaround:" -ForegroundColor Yellow
Write-Host "-------------------" -ForegroundColor Yellow
Write-Host "Updating heartbeat manually (this is temporary - page should do this automatically)" -ForegroundColor Gray

$updateQuery = @"
UPDATE UserReadiness
SET 
    LastHeartbeat = GETUTCDATE(),
    IsReady = 1
WHERE Username = '$Username' AND Role = 'Analyst'
"@

try {
    Invoke-Sqlcmd -ServerInstance $Server -Database $Database -Query $updateQuery
    Write-Host "SUCCESS: Heartbeat updated manually" -ForegroundColor Green
    Write-Host ""
    Write-Host "NOTE: This is temporary. The page should update heartbeat every 30 seconds." -ForegroundColor Yellow
    Write-Host "If heartbeat becomes stale again, check browser console for API errors." -ForegroundColor Yellow
} catch {
    Write-Host "ERROR: Failed to update heartbeat" -ForegroundColor Red
    Write-Host "  Error: $($_.Exception.Message)" -ForegroundColor Red
}

Write-Host ""


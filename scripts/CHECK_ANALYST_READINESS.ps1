# Script to check analyst readiness status
# This helps diagnose why assignments aren't coming through

Write-Host "========================================" -ForegroundColor Cyan
Write-Host "Analyst Readiness Diagnostic" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""

$apiUrl = "http://localhost:5205"

# 1. Check service state
Write-Host "1. Checking Service State..." -ForegroundColor Yellow
try {
    $serviceState = Invoke-RestMethod -Uri "$apiUrl/api/image-analysis-management/service-state" -Method GET -TimeoutSec 10 -ErrorAction Stop
    Write-Host "   AssignmentMode: $($serviceState.assignmentMode)" -ForegroundColor $(if ($serviceState.assignmentMode -eq "Auto") { "Green" } else { "Red" })
    Write-Host "   AutoAssign: $($serviceState.autoAssign)" -ForegroundColor $(if ($serviceState.autoAssign) { "Green" } else { "Yellow" })
    Write-Host "   Enabled: $($serviceState.enabled)" -ForegroundColor $(if ($serviceState.enabled) { "Green" } else { "Red" })
} catch {
    Write-Host "   ❌ Could not get service state: $($_.Exception.Message)" -ForegroundColor Red
}

Write-Host ""

# 2. Check ready users (requires admin auth - may fail)
Write-Host "2. Checking Ready Users (Analyst role)..." -ForegroundColor Yellow
Write-Host "   Note: This endpoint requires admin authentication" -ForegroundColor Gray
try {
    $readyUsers = Invoke-RestMethod -Uri "$apiUrl/api/image-analysis/user/ready-users?role=Analyst&maxIdleMinutes=5" -Method GET -TimeoutSec 10 -ErrorAction Stop
    Write-Host "   ✅ Found $($readyUsers.count) ready analysts:" -ForegroundColor Green
    if ($readyUsers.users -and $readyUsers.users.Count -gt 0) {
        foreach ($user in $readyUsers.users) {
            $timeSince = [TimeSpan]::FromSeconds($user.timeSinceHeartbeat.totalSeconds)
            Write-Host "     • $($user.username) - Last heartbeat: $($timeSince.TotalSeconds) seconds ago" -ForegroundColor Cyan
        }
    } else {
        Write-Host "     ⚠️  No ready analysts found" -ForegroundColor Yellow
    }
} catch {
    Write-Host "   ⚠️  Could not get ready users (may require authentication): $($_.Exception.Message)" -ForegroundColor Yellow
}

Write-Host ""

# 3. Check recent assignment activity
Write-Host "3. Checking Recent Assignment Activity in Logs..." -ForegroundColor Yellow
$logFile = "src\NickScanCentralImagingPortal.API\logs\nickscan-$(Get-Date -Format 'yyyyMMdd').txt"
$todayLogs = Get-ChildItem "src\NickScanCentralImagingPortal.API\logs" -Filter "nickscan-*.txt" | Sort-Object LastWriteTime -Descending | Select-Object -First 1
if ($todayLogs) {
    $logFile = $todayLogs.FullName
    $recentLogs = Get-Content $logFile -Tail 500 -ErrorAction SilentlyContinue
    $assignmentLogs = $recentLogs | Select-String -Pattern "(ASSIGNMENT|AUTO-ASSIGN|No ready users|ready users found)" -Context 0,1
    if ($assignmentLogs) {
        Write-Host "   Found assignment-related logs:" -ForegroundColor Green
        $assignmentLogs | Select-Object -Last 10 | ForEach-Object {
            $color = if ($_ -match "No ready|ERROR|Error|Failed") { "Red" } elseif ($_ -match "Assigned|Created|Success|ready users") { "Green" } else { "Cyan" }
            Write-Host "     $_" -ForegroundColor $color
        }
    } else {
        Write-Host "   ⚠️  No assignment activity found in recent logs" -ForegroundColor Yellow
        Write-Host "      (This suggests AssignmentMode might be 'Manual' or workflow isn't running)" -ForegroundColor Gray
    }
} else {
    Write-Host "   ⚠️  No log files found" -ForegroundColor Yellow
}

Write-Host ""

# 4. Recommendations
Write-Host "========================================" -ForegroundColor Cyan
Write-Host "RECOMMENDATIONS" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""

if ($serviceState.assignmentMode -ne "Auto") {
    Write-Host "❌ CRITICAL: AssignmentMode is '$($serviceState.assignmentMode)', not 'Auto'" -ForegroundColor Red
    Write-Host "   Action: Change Assignment Mode to 'Auto' in Image Analysis Management page" -ForegroundColor Yellow
    Write-Host ""
}

Write-Host "To get analysts showing as 'ready' in dashboard:" -ForegroundColor Yellow
Write-Host "  1. Analysts must log into the Image Analysis page" -ForegroundColor White
Write-Host "  2. The page auto-sets IsReady=true on load" -ForegroundColor White
Write-Host "  3. Heartbeats are sent automatically every 30 seconds" -ForegroundColor White
Write-Host "  4. If not showing, check the 'Ready for New Assignments' toggle" -ForegroundColor White
Write-Host ""
Write-Host "Note: Assignments should still work if users exist in database" -ForegroundColor Gray
Write-Host "      with Analyst role, even if not marked as ready in SignalR." -ForegroundColor Gray
Write-Host ""


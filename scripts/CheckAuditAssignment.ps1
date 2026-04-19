# Audit Assignment Diagnostic Script
# Checks why records are not getting assigned to audit

Write-Host "========================================" -ForegroundColor Cyan
Write-Host "Audit Assignment Diagnostic" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""

# Check 1: AssignmentMode
Write-Host "1. Checking AssignmentMode..." -ForegroundColor Yellow
$query = "SELECT AssignmentMode, Enabled, MaxConcurrentPerUser, LeaseMinutes FROM AnalysisSettings"
$settings = sqlcmd -S localhost -d NS_CIS -E -Q $query -W -h -1 | ConvertFrom-Csv -Delimiter "`t"
if ($settings) {
    Write-Host "   AssignmentMode: $($settings.AssignmentMode)" -ForegroundColor $(if ($settings.AssignmentMode -eq "Auto") { "Green" } else { "Red" })
    Write-Host "   Enabled: $($settings.Enabled)" -ForegroundColor $(if ($settings.Enabled -eq "True") { "Green" } else { "Red" })
    Write-Host "   MaxConcurrentPerUser: $($settings.MaxConcurrentPerUser)" -ForegroundColor Gray
    Write-Host "   LeaseMinutes: $($settings.LeaseMinutes)" -ForegroundColor Gray
    
    if ($settings.AssignmentMode -ne "Auto") {
        Write-Host "   ⚠️  WARNING: AssignmentMode is not 'Auto' - auto-assignment is disabled!" -ForegroundColor Red
    }
    if ($settings.Enabled -ne "True") {
        Write-Host "   ⚠️  WARNING: Assignment service is disabled!" -ForegroundColor Red
    }
} else {
    Write-Host "   ❌ No AnalysisSettings found!" -ForegroundColor Red
}

Write-Host ""

# Check 2: AnalystCompleted Groups
Write-Host "2. Checking AnalystCompleted Groups..." -ForegroundColor Yellow
$query = "SELECT COUNT(*) as Count FROM AnalysisGroups WHERE Status = 'AnalystCompleted'"
$analystCompleted = sqlcmd -S localhost -d NS_CIS -E -Q $query -W -h -1
Write-Host "   AnalystCompleted groups: $analystCompleted" -ForegroundColor $(if ([int]$analystCompleted -gt 0) { "Green" } else { "Yellow" })

if ([int]$analystCompleted -gt 0) {
    $query = "SELECT TOP 5 Id, GroupIdentifier, Status, Priority, CreatedAtUtc FROM AnalysisGroups WHERE Status = 'AnalystCompleted' ORDER BY CreatedAtUtc DESC"
    Write-Host "   Recent AnalystCompleted groups:" -ForegroundColor Gray
    sqlcmd -S localhost -d NS_CIS -E -Q $query -W -h -1 | ForEach-Object { Write-Host "     $_" -ForegroundColor Gray }
} else {
    Write-Host "   ⚠️  No AnalystCompleted groups found - analysts may not be completing their work" -ForegroundColor Yellow
}

Write-Host ""

# Check 3: WorkflowStage for AnalystCompleted Groups
Write-Host "3. Checking WorkflowStage for AnalystCompleted Groups..." -ForegroundColor Yellow
$query = @"
SELECT 
    ag.GroupIdentifier,
    ag.Status as GroupStatus,
    COUNT(ccs.Id) as TotalContainers,
    SUM(CASE WHEN ccs.WorkflowStage = 'ImageAnalysis' THEN 1 ELSE 0 END) as ImageAnalysisCount,
    SUM(CASE WHEN ccs.WorkflowStage = 'Audit' THEN 1 ELSE 0 END) as AuditCount,
    SUM(CASE WHEN ccs.WorkflowStage = 'Completed' THEN 1 ELSE 0 END) as CompletedCount
FROM AnalysisGroups ag
LEFT JOIN ContainerCompletenessStatuses ccs ON ag.GroupIdentifier = ccs.GroupIdentifier
WHERE ag.Status = 'AnalystCompleted'
GROUP BY ag.GroupIdentifier, ag.Status
HAVING COUNT(ccs.Id) > 0
"@

$workflowStages = sqlcmd -S localhost -d NS_CIS -E -Q $query -W -h -1
if ($workflowStages) {
    Write-Host "   WorkflowStage breakdown:" -ForegroundColor Gray
    $workflowStages | ForEach-Object { Write-Host "     $_" -ForegroundColor Gray }
    
    # Check if any groups have all containers in Audit stage
    $query = @"
SELECT 
    ag.GroupIdentifier,
    COUNT(ccs.Id) as TotalContainers,
    SUM(CASE WHEN ccs.WorkflowStage = 'Audit' THEN 1 ELSE 0 END) as AuditCount
FROM AnalysisGroups ag
LEFT JOIN ContainerCompletenessStatuses ccs ON ag.GroupIdentifier = ccs.GroupIdentifier
WHERE ag.Status = 'AnalystCompleted'
GROUP BY ag.GroupIdentifier
HAVING COUNT(ccs.Id) > 0 AND SUM(CASE WHEN ccs.WorkflowStage = 'Audit' THEN 1 ELSE 0 END) = COUNT(ccs.Id)
"@
    $readyForAudit = sqlcmd -S localhost -d NS_CIS -E -Q $query -W -h -1
    if ($readyForAudit) {
        Write-Host "   ✅ Groups ready for audit (all containers in Audit stage):" -ForegroundColor Green
        $readyForAudit | ForEach-Object { Write-Host "     $_" -ForegroundColor Gray }
    } else {
        Write-Host "   ⚠️  No groups have all containers in 'Audit' WorkflowStage" -ForegroundColor Yellow
    }
} else {
    Write-Host "   ⚠️  No WorkflowStage data found for AnalystCompleted groups" -ForegroundColor Yellow
}

Write-Host ""

# Check 4: Active Audit Assignments
Write-Host "4. Checking Active Audit Assignments..." -ForegroundColor Yellow
$query = "SELECT COUNT(*) as Count FROM AnalysisAssignments WHERE Role = 'Audit' AND State = 'Active' AND (LeaseUntilUtc IS NULL OR LeaseUntilUtc > GETUTCDATE())"
$activeAudit = sqlcmd -S localhost -d NS_CIS -E -Q $query -W -h -1
Write-Host "   Active audit assignments: $activeAudit" -ForegroundColor Gray

if ([int]$activeAudit -gt 0) {
    $query = "SELECT TOP 5 AssignedTo, GroupId, State, LeaseUntilUtc FROM AnalysisAssignments WHERE Role = 'Audit' AND State = 'Active' ORDER BY CreatedAtUtc DESC"
    Write-Host "   Recent active audit assignments:" -ForegroundColor Gray
    sqlcmd -S localhost -d NS_CIS -E -Q $query -W -h -1 | ForEach-Object { Write-Host "     $_" -ForegroundColor Gray }
}

Write-Host ""

# Check 5: Users with Audit Role
Write-Host "5. Checking Users with Audit Role..." -ForegroundColor Yellow
$query = @"
SELECT u.Username, u.IsActive, r.Name as RoleName
FROM Users u
INNER JOIN Roles r ON u.RoleId = r.Id
WHERE r.Name = 'Audit' AND u.IsActive = 1
"@
$auditUsers = sqlcmd -S localhost -d NS_CIS -E -Q $query -W -h -1
if ($auditUsers) {
    Write-Host "   ✅ Active Audit users found:" -ForegroundColor Green
    $auditUsers | ForEach-Object { Write-Host "     $_" -ForegroundColor Gray }
    
    # Check assignment counts per user
    Write-Host "   Checking assignment counts per user..." -ForegroundColor Gray
    foreach ($user in $auditUsers) {
        $username = ($user -split "`t")[0]
        $query = "SELECT COUNT(*) as Count FROM AnalysisAssignments WHERE Role = 'Audit' AND State = 'Active' AND AssignedTo = '$username' AND (LeaseUntilUtc IS NULL OR LeaseUntilUtc > GETUTCDATE())"
        $userCount = sqlcmd -S localhost -d NS_CIS -E -Q $query -W -h -1
        Write-Host "     $username : $userCount active assignments" -ForegroundColor Gray
    }
} else {
    Write-Host "   ❌ No active users with Audit role found!" -ForegroundColor Red
}

Write-Host ""

# Check 6: Groups with Active Assignments (should not be assigned again)
Write-Host "6. Checking Groups Already Assigned..." -ForegroundColor Yellow
$query = @"
SELECT ag.GroupIdentifier, ag.Status, aa.AssignedTo, aa.Role, aa.State
FROM AnalysisGroups ag
INNER JOIN AnalysisAssignments aa ON ag.Id = aa.GroupId
WHERE ag.Status = 'AnalystCompleted' 
  AND aa.State = 'Active' 
  AND (aa.LeaseUntilUtc IS NULL OR aa.LeaseUntilUtc > GETUTCDATE())
"@
$alreadyAssigned = sqlcmd -S localhost -d NS_CIS -E -Q $query -W -h -1
if ($alreadyAssigned) {
    Write-Host "   ⚠️  AnalystCompleted groups that already have active assignments:" -ForegroundColor Yellow
    $alreadyAssigned | ForEach-Object { Write-Host "     $_" -ForegroundColor Gray }
} else {
    Write-Host "   ✅ No AnalystCompleted groups have active assignments (they should be assignable)" -ForegroundColor Green
}

Write-Host ""

# Check 7: Recent AssignmentWorker Logs
Write-Host "7. Checking Recent AssignmentWorker Activity..." -ForegroundColor Yellow
$logPath = "src\NickScanCentralImagingPortal.API\logs\nickscan-20251214.txt"
if (Test-Path $logPath) {
    $auditLogs = Get-Content $logPath -Tail 500 | Select-String -Pattern "AUTO-ASSIGN.*Audit|Assigning.*Audit" | Select-Object -Last 10
    if ($auditLogs) {
        Write-Host "   Recent audit assignment activity:" -ForegroundColor Green
        $auditLogs | ForEach-Object { Write-Host "     $_" -ForegroundColor Gray }
    } else {
        Write-Host "   ⚠️  No recent audit assignment activity in logs" -ForegroundColor Yellow
    }
} else {
    Write-Host "   ⚠️  Log file not found: $logPath" -ForegroundColor Yellow
}

Write-Host ""

# Summary
Write-Host "========================================" -ForegroundColor Cyan
Write-Host "Summary & Recommendations" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""
Write-Host "Common issues preventing audit assignments:" -ForegroundColor Yellow
Write-Host "  1. AssignmentMode must be 'Auto'" -ForegroundColor White
Write-Host "  2. AnalysisSettings.Enabled must be True" -ForegroundColor White
Write-Host "  3. Must have AnalystCompleted groups" -ForegroundColor White
Write-Host "  4. WorkflowStage must be 'Audit' for containers in AnalystCompleted groups" -ForegroundColor White
Write-Host "  5. Must have active users with Audit role" -ForegroundColor White
Write-Host "  6. Audit users must have < MaxConcurrentPerUser active assignments" -ForegroundColor White
Write-Host "  7. Groups must not already have active assignments" -ForegroundColor White
Write-Host ""

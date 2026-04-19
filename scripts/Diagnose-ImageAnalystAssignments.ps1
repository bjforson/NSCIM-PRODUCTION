# Comprehensive Image Analyst Assignment Diagnostic Script
# Diagnoses why analyst assignments are not being created

param(
    [string]$Server = "127.0.0.1,1433",
    [string]$Database = "NS_CIS",
    [switch]$Detailed
)

$ErrorActionPreference = "Continue"

Write-Host "🔍 Image Analyst Assignment Diagnostic Tool" -ForegroundColor Cyan
Write-Host "===========================================" -ForegroundColor Cyan
Write-Host ""

# Check 1: Service Settings
Write-Host "📋 Check 1: Service Settings" -ForegroundColor Yellow
Write-Host "---------------------------" -ForegroundColor Yellow

$settingsQuery = "SELECT Enabled, AssignmentMode, AutoAssignStrategy, MaxConcurrentPerUser, LeaseMinutes FROM AnalysisSettings"
$settings = Invoke-Sqlcmd -ServerInstance $Server -Database $Database -Query $settingsQuery -TrustServerCertificate

if ($settings) {
    Write-Host "✅ Settings found:" -ForegroundColor Green
    Write-Host "  - Enabled: $($settings.Enabled)" -ForegroundColor $(if ($settings.Enabled -eq $true) { "Green" } else { "Red" })
    Write-Host "  - AssignmentMode: $($settings.AssignmentMode)" -ForegroundColor $(if ($settings.AssignmentMode -eq "Auto") { "Green" } else { "Yellow" })
    Write-Host "  - AutoAssignStrategy: $($settings.AutoAssignStrategy)" -ForegroundColor White
    Write-Host "  - MaxConcurrentPerUser: $($settings.MaxConcurrentPerUser)" -ForegroundColor White
    Write-Host "  - LeaseMinutes: $($settings.LeaseMinutes)" -ForegroundColor White
    Write-Host ""
    
    if ($settings.Enabled -eq $false) {
        Write-Host "❌ ISSUE FOUND: Service is DISABLED" -ForegroundColor Red
        Write-Host "   Fix: Set AnalysisSettings.Enabled = 1" -ForegroundColor Yellow
    }
    
    if ($settings.AssignmentMode -ne "Auto") {
        Write-Host "⚠️  WARNING: AssignmentMode is '$($settings.AssignmentMode)' (not 'Auto')" -ForegroundColor Yellow
        Write-Host "   Auto-assignment will NOT work. Use Manual assignment or change to 'Auto'" -ForegroundColor Yellow
    }
} else {
    Write-Host "❌ ISSUE FOUND: No AnalysisSettings record exists" -ForegroundColor Red
    Write-Host "   Fix: Run ImageAnalysisBootstrapper to create default settings" -ForegroundColor Yellow
}
Write-Host ""

# Check 2: Ready Groups
Write-Host "📦 Check 2: Ready Groups" -ForegroundColor Yellow
Write-Host "----------------------" -ForegroundColor Yellow

$readyGroupsQuery = @"
SELECT 
    COUNT(*) AS ReadyGroupsCount,
    MIN(CreatedAtUtc) AS OldestReady,
    MAX(CreatedAtUtc) AS NewestReady
FROM AnalysisGroups
WHERE Status = 'Ready'
"@

$readyGroups = Invoke-Sqlcmd -ServerInstance $Server -Database $Database -Query $readyGroupsQuery -TrustServerCertificate

if ($readyGroups -and $readyGroups.ReadyGroupsCount -gt 0) {
    Write-Host "✅ Found $($readyGroups.ReadyGroupsCount) Ready groups" -ForegroundColor Green
    Write-Host "  - Oldest: $($readyGroups.OldestReady)" -ForegroundColor White
    Write-Host "  - Newest: $($readyGroups.NewestReady)" -ForegroundColor White
} else {
    Write-Host "❌ ISSUE FOUND: No groups with Status='Ready'" -ForegroundColor Red
    Write-Host "   This means IntakeWorker may not be creating groups" -ForegroundColor Yellow
    Write-Host "   OR groups are being assigned immediately" -ForegroundColor Yellow
}
Write-Host ""

# Check 3: Groups with Active Assignments
Write-Host "🔗 Check 3: Ready Groups with Active Assignments" -ForegroundColor Yellow
Write-Host "-----------------------------------------------" -ForegroundColor Yellow

$assignedQuery = @"
SELECT 
    COUNT(*) AS ReadyGroupsWithActiveAssignments
FROM AnalysisGroups ag
INNER JOIN AnalysisAssignments aa ON aa.GroupId = ag.Id
WHERE ag.Status = 'Ready'
    AND aa.State = 'Active'
    AND (aa.LeaseUntilUtc IS NULL OR aa.LeaseUntilUtc > GETUTCDATE())
"@

$assigned = Invoke-Sqlcmd -ServerInstance $Server -Database $Database -Query $assignedQuery -TrustServerCertificate

if ($assigned.ReadyGroupsWithActiveAssignments -gt 0) {
    Write-Host "⚠️  WARNING: $($assigned.ReadyGroupsWithActiveAssignments) Ready groups have active assignments" -ForegroundColor Yellow
    Write-Host "   These groups should not be 'Ready' if they have active assignments" -ForegroundColor Yellow
} else {
    Write-Host "✅ No Ready groups have active assignments (correct)" -ForegroundColor Green
}
Write-Host ""

# Check 4: Analyst Users
Write-Host "👥 Check 4: Analyst Users" -ForegroundColor Yellow
Write-Host "----------------------" -ForegroundColor Yellow

$analystsQuery = @"
SELECT 
    u.UserName,
    u.Email,
    COUNT(DISTINCT r.Name) AS RoleCount
FROM AspNetUsers u
INNER JOIN AspNetUserRoles ur ON ur.UserId = u.Id
INNER JOIN AspNetRoles r ON r.Id = ur.RoleId
WHERE r.Name = 'Analyst'
GROUP BY u.UserName, u.Email
"@

$analysts = Invoke-Sqlcmd -ServerInstance $Server -Database $Database -Query $analystsQuery -TrustServerCertificate

if ($analysts) {
    Write-Host "✅ Found $($analysts.Count) analyst(s):" -ForegroundColor Green
    $analysts | Format-Table -AutoSize
} else {
    Write-Host "❌ ISSUE FOUND: No users with 'Analyst' role" -ForegroundColor Red
    Write-Host "   Fix: Assign 'Analyst' role to users" -ForegroundColor Yellow
}
Write-Host ""

# Check 5: Analyst Active Assignments
Write-Host "📊 Check 5: Analyst Active Assignments" -ForegroundColor Yellow
Write-Host "------------------------------------" -ForegroundColor Yellow

if ($analysts) {
    $activeAssignmentsQuery = @"
SELECT 
    aa.AssignedTo,
    COUNT(*) AS ActiveAssignments,
    MAX(aa.LeaseUntilUtc) AS LatestLease
FROM AnalysisAssignments aa
WHERE aa.Role = 'Analyst'
    AND aa.State = 'Active'
    AND (aa.LeaseUntilUtc IS NULL OR aa.LeaseUntilUtc > GETUTCDATE())
GROUP BY aa.AssignedTo
ORDER BY ActiveAssignments DESC
"@

    $activeAssignments = Invoke-Sqlcmd -ServerInstance $Server -Database $Database -Query $activeAssignmentsQuery -TrustServerCertificate
    
    if ($activeAssignments) {
        Write-Host "Active assignments per analyst:" -ForegroundColor Cyan
        $activeAssignments | Format-Table -AutoSize
        
        $maxConcurrent = $settings.MaxConcurrentPerUser
        foreach ($analyst in $activeAssignments) {
            if ($analyst.ActiveAssignments -ge $maxConcurrent) {
                $assignCount = $analyst.ActiveAssignments
                Write-Host "⚠️  WARNING: $($analyst.AssignedTo) is at max capacity ($assignCount of $maxConcurrent)" -ForegroundColor Yellow
            }
        }
    } else {
        Write-Host "✅ No active assignments (analysts are available)" -ForegroundColor Green
    }
} else {
    Write-Host "⏭️  Skipping (no analysts found)" -ForegroundColor Gray
}
Write-Host ""

# Check 6: Workflow Stages
Write-Host "🔄 Check 6: Container Workflow Stages" -ForegroundColor Yellow
Write-Host "-----------------------------------" -ForegroundColor Yellow

$workflowQuery = @"
SELECT 
    ag.GroupIdentifier,
    ag.Status AS GroupStatus,
    ccs.WorkflowStage,
    COUNT(DISTINCT ar.ContainerNumber) AS ContainersInStage
FROM AnalysisGroups ag
INNER JOIN AnalysisRecords ar ON ar.GroupId = ag.Id
INNER JOIN ContainerCompletenessStatus ccs ON ccs.ContainerNumber = ar.ContainerNumber
WHERE ag.Status = 'Ready'
GROUP BY ag.GroupIdentifier, ag.Status, ccs.WorkflowStage
ORDER BY ag.GroupIdentifier
"@

$workflows = Invoke-Sqlcmd -ServerInstance $Server -Database $Database -Query $workflowQuery -TrustServerCertificate

if ($workflows) {
    Write-Host "Workflow stages for Ready groups:" -ForegroundColor Cyan
    $workflows | Format-Table -AutoSize
    
    $invalidStages = $workflows | Where-Object { $_.WorkflowStage -in @("Audit", "Completed") }
    if ($invalidStages) {
        Write-Host "❌ ISSUE FOUND: Some containers are in 'Audit' or 'Completed' stage" -ForegroundColor Red
        Write-Host "   AssignmentWorker filters out these stages - containers won't be assigned" -ForegroundColor Yellow
        Write-Host "   Fix: Reset WorkflowStage to 'ImageAnalysis' or 'Pending'" -ForegroundColor Yellow
    } else {
        Write-Host "✅ All containers in valid stages for assignment" -ForegroundColor Green
    }
} else {
    Write-Host "⚠️  No workflow stage data found for Ready groups" -ForegroundColor Yellow
}
Write-Host ""

# Check 7: Expired Assignments
Write-Host "⏰ Check 7: Expired Active Assignments" -ForegroundColor Yellow
Write-Host "-------------------------------------" -ForegroundColor Yellow

$expiredQuery = @"
SELECT 
    COUNT(*) AS ExpiredButActive,
    MIN(LeaseUntilUtc) AS OldestExpired,
    MAX(LeaseUntilUtc) AS NewestExpired
FROM AnalysisAssignments
WHERE State = 'Active'
    AND LeaseUntilUtc < GETUTCDATE()
"@

$expired = Invoke-Sqlcmd -ServerInstance $Server -Database $Database -Query $expiredQuery -TrustServerCertificate

if ($expired.ExpiredButActive -gt 0) {
    Write-Host "❌ ISSUE FOUND: $($expired.ExpiredButActive) expired assignments still marked as 'Active'" -ForegroundColor Red
    Write-Host "   These should be cleaned up by AssignmentWorker" -ForegroundColor Yellow
    Write-Host "   Oldest expired: $($expired.OldestExpired)" -ForegroundColor White
    Write-Host "   Newest expired: $($expired.NewestExpired)" -ForegroundColor White
} else {
    Write-Host "✅ No expired active assignments (cleanup working)" -ForegroundColor Green
}
Write-Host ""

# Check 8: User Readiness
Write-Host "💚 Check 8: Analyst User Readiness" -ForegroundColor Yellow
Write-Host "--------------------------------" -ForegroundColor Yellow

$readinessQuery = @"
SELECT 
    ur.UserName,
    ur.IsReady,
    ur.LastHeartbeatUtc,
    DATEDIFF(second, ur.LastHeartbeatUtc, GETUTCDATE()) AS SecondsSinceHeartbeat
FROM UserReadiness ur
INNER JOIN AspNetUsers u ON u.UserName = ur.UserName
INNER JOIN AspNetUserRoles ur2 ON ur2.UserId = u.Id
INNER JOIN AspNetRoles r ON r.Id = ur2.RoleId
WHERE r.Name = 'Analyst'
"@

$readiness = Invoke-Sqlcmd -ServerInstance $Server -Database $Database -Query $readinessQuery -TrustServerCertificate

if ($readiness) {
    Write-Host "Analyst readiness status:" -ForegroundColor Cyan
    $readiness | Format-Table -AutoSize
    
    $notReady = $readiness | Where-Object { $_.IsReady -eq $false }
    $stale = $readiness | Where-Object { $_.SecondsSinceHeartbeat -gt 300 }
    
    if ($notReady) {
        Write-Host "⚠️  WARNING: Some analysts marked as NOT READY" -ForegroundColor Yellow
    }
    if ($stale) {
        Write-Host "⚠️  WARNING: Some analysts have stale heartbeats (>5 minutes)" -ForegroundColor Yellow
    }
    if (-not $notReady -and -not $stale) {
        Write-Host "✅ All analysts are ready" -ForegroundColor Green
    }
} else {
    Write-Host "⚠️  No UserReadiness records found for analysts" -ForegroundColor Yellow
    Write-Host "   Analysts need to be active (heartbeat within 5 minutes)" -ForegroundColor Yellow
}
Write-Host ""

# Check 9: Complete Containers
Write-Host "✅ Check 9: Complete Containers (IntakeWorker Input)" -ForegroundColor Yellow
Write-Host "--------------------------------------------------" -ForegroundColor Yellow

$completeQuery = @"
SELECT 
    COUNT(*) AS CompleteContainers,
    COUNT(DISTINCT GroupIdentifier) AS UniqueGroups,
    MIN(UpdatedAtUtc) AS OldestComplete,
    MAX(UpdatedAtUtc) AS NewestComplete
FROM ContainerCompletenessStatus
WHERE Status = 'Complete'
"@

$complete = Invoke-Sqlcmd -ServerInstance $Server -Database $Database -Query $completeQuery -TrustServerCertificate

if ($complete.CompleteContainers -gt 0) {
    Write-Host "✅ Found $($complete.CompleteContainers) complete containers" -ForegroundColor Green
    Write-Host "  - Unique groups: $($complete.UniqueGroups)" -ForegroundColor White
    Write-Host "  - Oldest: $($complete.OldestComplete)" -ForegroundColor White
    Write-Host "  - Newest: $($complete.NewestComplete)" -ForegroundColor White
} else {
    Write-Host "⚠️  No containers with Status='Complete'" -ForegroundColor Yellow
    Write-Host "   IntakeWorker needs complete containers to create groups" -ForegroundColor Yellow
}
Write-Host ""

# Summary
Write-Host "📊 DIAGNOSIS SUMMARY" -ForegroundColor Cyan
Write-Host "===================" -ForegroundColor Cyan
Write-Host ""

$issues = @()

if ($settings.Enabled -eq $false) {
    $issues += "❌ Service is DISABLED"
}
if ($settings.AssignmentMode -ne "Auto") {
    $issues += "⚠️  AssignmentMode is '$($settings.AssignmentMode)' (not 'Auto')"
}
if (-not $readyGroups -or $readyGroups.ReadyGroupsCount -eq 0) {
    $issues += "❌ No Ready groups found"
}
if (-not $analysts) {
    $issues += "❌ No users with 'Analyst' role"
}
if ($expired.ExpiredButActive -gt 0) {
    $issues += "❌ $($expired.ExpiredButActive) expired assignments not cleaned up"
}

if ($issues.Count -eq 0) {
    Write-Host "✅ No critical issues found!" -ForegroundColor Green
    Write-Host ""
    Write-Host "If assignments still not working, check:" -ForegroundColor Yellow
    Write-Host "  1. Application logs for AssignmentWorker errors" -ForegroundColor White
    Write-Host "  2. WorkflowStage of containers (must be 'ImageAnalysis' or 'Pending')" -ForegroundColor White
    Write-Host "  3. Analyst capacity (MaxConcurrentPerUser)" -ForegroundColor White
    Write-Host "  4. User Readiness (analysts must be active)" -ForegroundColor White
} else {
    Write-Host "Issues found:" -ForegroundColor Red
    foreach ($issue in $issues) {
        Write-Host "  $issue" -ForegroundColor Red
    }
}

Write-Host ""
Write-Host "💡 Next Steps:" -ForegroundColor Cyan
Write-Host "  1. Review issues above" -ForegroundColor White
Write-Host "  2. Check application logs for AssignmentWorker activity" -ForegroundColor White
Write-Host "  3. Fix identified issues" -ForegroundColor White
Write-Host "  4. Re-run diagnostic to verify fixes" -ForegroundColor White


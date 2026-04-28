# Comprehensive Image Analyst Assignment Diagnostic Script
# Diagnoses why analyst assignments are not being created

param(
    [string]$Server = "127.0.0.1,1433",
    [string]$Database = "NS_CIS"
)

# Continues past errors intentionally: comprehensive diagnostic with many independent assignment-flow checks; failing fast at one check hides subsequent issues.
$ErrorActionPreference = "Continue"

Write-Host "Image Analyst Assignment Diagnostic Tool" -ForegroundColor Cyan
Write-Host "=======================================" -ForegroundColor Cyan
Write-Host ""

# Check 1: Service Settings
Write-Host "Check 1: Service Settings" -ForegroundColor Yellow
Write-Host "------------------------" -ForegroundColor Yellow

$settingsQuery = "SELECT Enabled, AssignmentMode, AutoAssignStrategy, MaxConcurrentPerUser, LeaseMinutes FROM AnalysisSettings"
$settings = Invoke-Sqlcmd -ServerInstance $Server -Database $Database -Query $settingsQuery 
if ($settings) {
    Write-Host "Settings found:" -ForegroundColor Green
    Write-Host "  - Enabled: $($settings.Enabled)" -ForegroundColor $(if ($settings.Enabled -eq $true) { "Green" } else { "Red" })
    Write-Host "  - AssignmentMode: $($settings.AssignmentMode)" -ForegroundColor $(if ($settings.AssignmentMode -eq "Auto") { "Green" } else { "Yellow" })
    Write-Host "  - AutoAssignStrategy: $($settings.AutoAssignStrategy)" -ForegroundColor White
    Write-Host "  - MaxConcurrentPerUser: $($settings.MaxConcurrentPerUser)" -ForegroundColor White
    Write-Host "  - LeaseMinutes: $($settings.LeaseMinutes)" -ForegroundColor White
    Write-Host ""
    
    if ($settings.Enabled -eq $false) {
        Write-Host "ISSUE FOUND: Service is DISABLED" -ForegroundColor Red
        Write-Host "   Fix: Set AnalysisSettings.Enabled = 1" -ForegroundColor Yellow
    }
    
    if ($settings.AssignmentMode -ne "Auto") {
        Write-Host "WARNING: AssignmentMode is '$($settings.AssignmentMode)' (not 'Auto')" -ForegroundColor Yellow
        Write-Host "   Auto-assignment will NOT work. Use Manual assignment or change to 'Auto'" -ForegroundColor Yellow
    }
} else {
    Write-Host "ISSUE FOUND: No AnalysisSettings record exists" -ForegroundColor Red
    Write-Host "   Fix: Run ImageAnalysisBootstrapper to create default settings" -ForegroundColor Yellow
}
Write-Host ""

# Check 2: Ready Groups
Write-Host "Check 2: Ready Groups" -ForegroundColor Yellow
Write-Host "-------------------" -ForegroundColor Yellow

$readyGroupsQuery = "SELECT COUNT(*) AS ReadyGroupsCount, MIN(CreatedAtUtc) AS OldestReady, MAX(CreatedAtUtc) AS NewestReady FROM AnalysisGroups WHERE Status = 'Ready'"
$readyGroups = Invoke-Sqlcmd -ServerInstance $Server -Database $Database -Query $readyGroupsQuery 
if ($readyGroups -and $readyGroups.ReadyGroupsCount -gt 0) {
    Write-Host "Found $($readyGroups.ReadyGroupsCount) Ready groups" -ForegroundColor Green
    Write-Host "  - Oldest: $($readyGroups.OldestReady)" -ForegroundColor White
    Write-Host "  - Newest: $($readyGroups.NewestReady)" -ForegroundColor White
} else {
    Write-Host "ISSUE FOUND: No groups with Status='Ready'" -ForegroundColor Red
    Write-Host "   This means IntakeWorker may not be creating groups" -ForegroundColor Yellow
    Write-Host "   OR groups are being assigned immediately" -ForegroundColor Yellow
}
Write-Host ""

# Check 3: Analyst Users
Write-Host "Check 3: Analyst Users" -ForegroundColor Yellow
Write-Host "---------------------" -ForegroundColor Yellow

$analystsQuery = "SELECT u.UserName, u.Email FROM AspNetUsers u INNER JOIN AspNetUserRoles ur ON ur.UserId = u.Id INNER JOIN AspNetRoles r ON r.Id = ur.RoleId WHERE r.Name = 'Analyst' GROUP BY u.UserName, u.Email"
$analysts = Invoke-Sqlcmd -ServerInstance $Server -Database $Database -Query $analystsQuery 
if ($analysts) {
    Write-Host "Found $($analysts.Count) analyst(s):" -ForegroundColor Green
    $analysts | Format-Table -AutoSize
} else {
    Write-Host "ISSUE FOUND: No users with 'Analyst' role" -ForegroundColor Red
    Write-Host "   Fix: Assign 'Analyst' role to users" -ForegroundColor Yellow
}
Write-Host ""

# Check 4: Analyst Active Assignments
Write-Host "Check 4: Analyst Active Assignments" -ForegroundColor Yellow
Write-Host "----------------------------------" -ForegroundColor Yellow

if ($analysts) {
    $now = Get-Date -Format "yyyy-MM-dd HH:mm:ss"
    $activeAssignmentsQuery = "SELECT aa.AssignedTo, COUNT(*) AS ActiveAssignments FROM AnalysisAssignments aa WHERE aa.Role = 'Analyst' AND aa.State = 'Active' AND (aa.LeaseUntilUtc IS NULL OR aa.LeaseUntilUtc > GETUTCDATE()) GROUP BY aa.AssignedTo ORDER BY ActiveAssignments DESC"
    $activeAssignments = Invoke-Sqlcmd -ServerInstance $Server -Database $Database -Query $activeAssignmentsQuery     
    if ($activeAssignments) {
        Write-Host "Active assignments per analyst:" -ForegroundColor Cyan
        $activeAssignments | Format-Table -AutoSize
        
        $maxConcurrent = $settings.MaxConcurrentPerUser
        foreach ($analyst in $activeAssignments) {
            if ($analyst.ActiveAssignments -ge $maxConcurrent) {
                $assignCount = $analyst.ActiveAssignments
                Write-Host "WARNING: $($analyst.AssignedTo) is at max capacity ($assignCount of $maxConcurrent)" -ForegroundColor Yellow
            }
        }
    } else {
        Write-Host "No active assignments (analysts are available)" -ForegroundColor Green
    }
} else {
    Write-Host "Skipping (no analysts found)" -ForegroundColor Gray
}
Write-Host ""

# Check 5: Expired Assignments
Write-Host "Check 5: Expired Active Assignments" -ForegroundColor Yellow
Write-Host "-----------------------------------" -ForegroundColor Yellow

$expiredQuery = "SELECT COUNT(*) AS ExpiredButActive, MIN(LeaseUntilUtc) AS OldestExpired, MAX(LeaseUntilUtc) AS NewestExpired FROM AnalysisAssignments WHERE State = 'Active' AND LeaseUntilUtc < GETUTCDATE()"
$expired = Invoke-Sqlcmd -ServerInstance $Server -Database $Database -Query $expiredQuery 
if ($expired.ExpiredButActive -gt 0) {
    Write-Host "ISSUE FOUND: $($expired.ExpiredButActive) expired assignments still marked as 'Active'" -ForegroundColor Red
    Write-Host "   These should be cleaned up by AssignmentWorker" -ForegroundColor Yellow
    Write-Host "   Oldest expired: $($expired.OldestExpired)" -ForegroundColor White
    Write-Host "   Newest expired: $($expired.NewestExpired)" -ForegroundColor White
} else {
    Write-Host "No expired active assignments (cleanup working)" -ForegroundColor Green
}
Write-Host ""

# Check 6: Complete Containers
Write-Host "Check 6: Complete Containers (IntakeWorker Input)" -ForegroundColor Yellow
Write-Host "--------------------------------------------------" -ForegroundColor Yellow

$completeQuery = "SELECT COUNT(*) AS CompleteContainers, COUNT(DISTINCT GroupIdentifier) AS UniqueGroups, MIN(UpdatedAtUtc) AS OldestComplete, MAX(UpdatedAtUtc) AS NewestComplete FROM ContainerCompletenessStatus WHERE Status = 'Complete'"
$complete = Invoke-Sqlcmd -ServerInstance $Server -Database $Database -Query $completeQuery 
if ($complete.CompleteContainers -gt 0) {
    Write-Host "Found $($complete.CompleteContainers) complete containers" -ForegroundColor Green
    Write-Host "  - Unique groups: $($complete.UniqueGroups)" -ForegroundColor White
    Write-Host "  - Oldest: $($complete.OldestComplete)" -ForegroundColor White
    Write-Host "  - Newest: $($complete.NewestComplete)" -ForegroundColor White
} else {
    Write-Host "No containers with Status='Complete'" -ForegroundColor Yellow
    Write-Host "   IntakeWorker needs complete containers to create groups" -ForegroundColor Yellow
}
Write-Host ""

# Summary
Write-Host "DIAGNOSIS SUMMARY" -ForegroundColor Cyan
Write-Host "=================" -ForegroundColor Cyan
Write-Host ""

$issues = @()

if ($settings.Enabled -eq $false) {
    $issues += "Service is DISABLED"
}
if ($settings.AssignmentMode -ne "Auto") {
    $issues += "AssignmentMode is '$($settings.AssignmentMode)' (not 'Auto')"
}
if (-not $readyGroups -or $readyGroups.ReadyGroupsCount -eq 0) {
    $issues += "No Ready groups found"
}
if (-not $analysts) {
    $issues += "No users with 'Analyst' role"
}
if ($expired.ExpiredButActive -gt 0) {
    $issues += "$($expired.ExpiredButActive) expired assignments not cleaned up"
}

if ($issues.Count -eq 0) {
    Write-Host "No critical issues found!" -ForegroundColor Green
    Write-Host ""
    Write-Host "If assignments still not working, check:" -ForegroundColor Yellow
    Write-Host "  1. Application logs for AssignmentWorker errors" -ForegroundColor White
    Write-Host "  2. WorkflowStage of containers (must be 'ImageAnalysis' or 'Pending')" -ForegroundColor White
    Write-Host "  3. Analyst capacity (MaxConcurrentPerUser)" -ForegroundColor White
    Write-Host "  4. User Readiness (analysts must be active)" -ForegroundColor White
} else {
    Write-Host "Issues found:" -ForegroundColor Red
    foreach ($issue in $issues) {
        Write-Host "  - $issue" -ForegroundColor Red
    }
}

Write-Host ""
Write-Host "Next Steps:" -ForegroundColor Cyan
Write-Host "  1. Review issues above" -ForegroundColor White
Write-Host "  2. Check application logs for AssignmentWorker activity" -ForegroundColor White
Write-Host "  3. Fix identified issues" -ForegroundColor White
Write-Host "  4. Re-run diagnostic to verify fixes" -ForegroundColor White


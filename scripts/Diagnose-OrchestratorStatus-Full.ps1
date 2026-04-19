# Full Orchestrator Status Diagnostic Script
# Checks orchestrator execution, intake/assignment status, ready users, and ready groups

param(
    [string]$ServerInstance = "localhost",
    [string]$Database = "NickScanCentralImagingPortal"
)

Write-Host "=== FULL ORCHESTRATOR DIAGNOSTIC ===" -ForegroundColor Cyan
Write-Host ""

# Connection string
$connectionString = "Server=$ServerInstance;Database=$Database;Integrated Security=true;TrustServerCertificate=true;"

try {
    Write-Host "[1/7] Checking orchestrator service status..." -ForegroundColor Yellow
    # Check if orchestrator service is running (this is a placeholder - actual check depends on how it's hosted)
    Write-Host "  Note: Orchestrator service check not implemented (check logs or process list)" -ForegroundColor Gray
    Write-Host ""
    
    Write-Host "[2/7] Checking AnalysisSettings..." -ForegroundColor Yellow
    $settingsQuery = "SELECT Enabled, AssignmentMode, MaxConcurrentPerUser, LeaseMinutes, AutoAssignStrategy FROM AnalysisSettings"
    $settings = Invoke-Sqlcmd -ConnectionString $connectionString -Query $settingsQuery
    if ($settings) {
        Write-Host "  Enabled: $($settings.Enabled)" -ForegroundColor $(if ($settings.Enabled) { "Green" } else { "Red" })
        Write-Host "  AssignmentMode: $($settings.AssignmentMode)" -ForegroundColor $(if ($settings.AssignmentMode -eq "Auto") { "Green" } else { "Yellow" })
        Write-Host "  MaxConcurrentPerUser: $($settings.MaxConcurrentPerUser)"
        Write-Host "  LeaseMinutes: $($settings.LeaseMinutes)"
        Write-Host "  AutoAssignStrategy: $($settings.AutoAssignStrategy)"
    } else {
        Write-Host "  ⚠️ NO ANALYSIS SETTINGS FOUND!" -ForegroundColor Red
    }
    Write-Host ""
    
    Write-Host "[3/7] Checking ready groups (Status='Ready')..." -ForegroundColor Yellow
    $readyGroupsQuery = @"
        SELECT COUNT(*) as Count 
        FROM AnalysisGroups 
        WHERE Status = 'Ready'
    "@
    $readyGroups = Invoke-Sqlcmd -ConnectionString $connectionString -Query $readyGroupsQuery
    Write-Host "  Ready groups: $($readyGroups.Count)" -ForegroundColor $(if ($readyGroups.Count -gt 0) { "Green" } else { "Yellow" })
    Write-Host ""
    
    Write-Host "[4/7] Checking groups with ImageAnalysis WorkflowStage containers..." -ForegroundColor Yellow
    $workflowGroupsQuery = @"
        SELECT COUNT(DISTINCT GroupIdentifier) as Count
        FROM ContainerCompletenessStatuses
        WHERE WorkflowStage = 'ImageAnalysis'
            AND Status LIKE 'Complete%'
            AND GroupIdentifier IS NOT NULL
    "@
    $workflowGroups = Invoke-Sqlcmd -ConnectionString $connectionString -Query $workflowGroupsQuery
    Write-Host "  Groups with ImageAnalysis containers: $($workflowGroups.Count)" -ForegroundColor $(if ($workflowGroups.Count -gt 0) { "Green" } else { "Yellow" })
    Write-Host ""
    
    Write-Host "[5/7] Checking UserReadiness for Analyst role..." -ForegroundColor Yellow
    $readinessQuery = @"
        SELECT Username, IsReady, LastHeartbeat, Role,
               DATEDIFF(SECOND, LastHeartbeat, GETUTCDATE()) as SecondsSinceHeartbeat
        FROM UserReadiness
        WHERE Role = 'Analyst'
        ORDER BY LastHeartbeat DESC
    "@
    $readiness = Invoke-Sqlcmd -ConnectionString $connectionString -Query $readinessQuery
    if ($readiness) {
        Write-Host "  Found $($readiness.Count) Analyst readiness records:" -ForegroundColor Cyan
        foreach ($record in $readiness) {
            $status = if ($record.IsReady -and $record.SecondsSinceHeartbeat -lt 120) { "READY" } else { "NOT READY" }
            $color = if ($status -eq "READY") { "Green" } else { "Red" }
            Write-Host "    $($record.Username): $status (Heartbeat: $($record.SecondsSinceHeartbeat)s ago, IsReady: $($record.IsReady))" -ForegroundColor $color
        }
    } else {
        Write-Host "  ⚠️ NO USER READINESS RECORDS FOUND FOR ANALYST ROLE!" -ForegroundColor Red
    }
    Write-Host ""
    
    Write-Host "[6/7] Checking users with Analyst role..." -ForegroundColor Yellow
    $usersQuery = @"
        SELECT u.Username, u.IsActive, r.Name as RoleName
        FROM Users u
        INNER JOIN Roles r ON u.RoleId = r.Id
        WHERE r.Name = 'Analyst' AND u.IsActive = 1
    "@
    $users = Invoke-Sqlcmd -ConnectionString $connectionString -Query $usersQuery
    if ($users) {
        Write-Host "  Found $($users.Count) active users with Analyst role:" -ForegroundColor Cyan
        foreach ($user in $users) {
            Write-Host "    $($user.Username)" -ForegroundColor Green
        }
    } else {
        Write-Host "  ⚠️ NO ACTIVE USERS WITH ANALYST ROLE!" -ForegroundColor Red
    }
    Write-Host ""
    
    Write-Host "[7/7] Checking active assignments..." -ForegroundColor Yellow
    $assignmentsQuery = @"
        SELECT COUNT(*) as Count
        FROM AnalysisAssignments
        WHERE State = 'Active' 
            AND (LeaseUntilUtc IS NULL OR LeaseUntilUtc > GETUTCDATE())
    "@
    $assignments = Invoke-Sqlcmd -ConnectionString $connectionString -Query $assignmentsQuery
    Write-Host "  Active assignments: $($assignments.Count)" -ForegroundColor $(if ($assignments.Count -gt 0) { "Green" } else { "Yellow" })
    Write-Host ""
    
    Write-Host "=== SUMMARY ===" -ForegroundColor Cyan
    $issues = @()
    if (-not $settings -or -not $settings.Enabled) { $issues += "AnalysisSettings not enabled" }
    if ($settings -and $settings.AssignmentMode -ne "Auto") { $issues += "AssignmentMode is not 'Auto'" }
    if ($readyGroups.Count -eq 0) { $issues += "No ready groups found" }
    if ($workflowGroups.Count -eq 0) { $issues += "No groups with ImageAnalysis WorkflowStage" }
    if (-not $readiness -or ($readiness | Where-Object { $_.IsReady -and $_.SecondsSinceHeartbeat -lt 120 }).Count -eq 0) { 
        $issues += "No ready analysts (IsReady=true and heartbeat < 2 min)" 
    }
    if (-not $users) { $issues += "No users with Analyst role" }
    
    if ($issues.Count -eq 0) {
        Write-Host "✅ All checks passed - assignments should be working!" -ForegroundColor Green
        Write-Host "  If assignments still not coming through, check orchestrator logs for execution status." -ForegroundColor Yellow
    } else {
        Write-Host "⚠️ Issues found:" -ForegroundColor Red
        foreach ($issue in $issues) {
            Write-Host "  - $issue" -ForegroundColor Red
        }
    }
    
} catch {
    Write-Host "❌ Error: $_" -ForegroundColor Red
    Write-Host "Stack trace: $($_.ScriptStackTrace)" -ForegroundColor Red
}


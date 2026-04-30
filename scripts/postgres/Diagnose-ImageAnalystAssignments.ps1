# Comprehensive Image Analyst Assignment Diagnostic — Postgres edition
#
# Postgres equivalent of scripts/Diagnose-ImageAnalystAssignments.ps1
# (the SQL Server original used Invoke-Sqlcmd against AspNetUsers + GETUTCDATE();
#  this version uses Npgsql against the live nickscan_production DB.)
#
# Output shape mirrors the SQL Server version so anyone familiar with it can
# read this one. Differences worth knowing:
#   - Tables/columns are lowercase (analysissettings, analysisgroups, …)
#   - Identity tables are users + roles (NOT AspNetUsers / AspNetRoles)
#   - Time math uses GREATEST / EXTRACT(EPOCH FROM …) instead of DATEDIFF
#   - Tenant context is set to '1' on every connection (RLS is fail-closed
#     since 2026-04-25; otherwise SELECTs silently return 0 rows)
#
# Usage:
#   pwsh ./Diagnose-ImageAnalystAssignments.ps1
#   pwsh ./Diagnose-ImageAnalystAssignments.ps1 -PgHost 127.0.0.1 -Database nickscan_production
#
# Read-only — uses nscim_app role.

[CmdletBinding()]
param(
    [string]$PgHost = "127.0.0.1",
    [int]$Port = 5432,
    [string]$Database = "nickscan_production",
    [string]$TenantId = "1",
    [switch]$Detailed
)

. "$PSScriptRoot\_NpgsqlHelper.ps1"

# Continue past errors intentionally: many independent assignment-flow checks; failing fast
# at one check hides downstream issues. Same rationale as the SQL Server original.
$ErrorActionPreference = "Continue"

Write-Host "Image Analyst Assignment Diagnostic Tool (Postgres)" -ForegroundColor Cyan
Write-Host "===================================================" -ForegroundColor Cyan
Write-Host "  DB: $Database on $PgHost`:$Port (tenant=$TenantId)"
Write-Host ""

$h = Open-NscimConnection -PgHost $PgHost -Port $Port -Database $Database -TenantId $TenantId

try {
    # ------------------------------------------------------------------
    # Check 1: Service Settings
    # ------------------------------------------------------------------
    Write-Host "Check 1: Service Settings" -ForegroundColor Yellow
    Write-Host "-------------------------" -ForegroundColor Yellow

    $settings = Invoke-NscimQuery -Handle $h -Sql @"
SELECT enabled, assignmentmode, autoassignstrategy, maxconcurrentperuser, leaseminutes
FROM analysissettings
"@ | Select-Object -First 1

    if ($settings) {
        $color = if ($settings.enabled) { 'Green' } else { 'Red' }
        Write-Host "  Settings found:" -ForegroundColor Green
        Write-Host ("    Enabled              : {0}" -f $settings.enabled) -ForegroundColor $color
        $mc = if ($settings.assignmentmode -eq 'Auto') { 'Green' } else { 'Yellow' }
        Write-Host ("    AssignmentMode       : {0}" -f $settings.assignmentmode) -ForegroundColor $mc
        Write-Host ("    AutoAssignStrategy   : {0}" -f $settings.autoassignstrategy) -ForegroundColor White
        Write-Host ("    MaxConcurrentPerUser : {0}" -f $settings.maxconcurrentperuser) -ForegroundColor White
        Write-Host ("    LeaseMinutes         : {0}" -f $settings.leaseminutes) -ForegroundColor White
        if (-not $settings.enabled) {
            Write-Host "  ISSUE: Service is DISABLED" -ForegroundColor Red
            Write-Host "    Fix: UPDATE analysissettings SET enabled = true" -ForegroundColor Yellow
        }
        if ($settings.assignmentmode -ne 'Auto') {
            Write-Host ("  WARNING: AssignmentMode is '{0}' (not 'Auto')" -f $settings.assignmentmode) -ForegroundColor Yellow
            Write-Host "    Auto-assignment will NOT work unless changed to 'Auto'." -ForegroundColor Yellow
        }
    } else {
        Write-Host "  ISSUE: No analysissettings row exists" -ForegroundColor Red
        Write-Host "    Fix: Run ImageAnalysisBootstrapper to create defaults" -ForegroundColor Yellow
    }
    Write-Host ""

    # ------------------------------------------------------------------
    # Check 2: Ready Groups
    # ------------------------------------------------------------------
    Write-Host "Check 2: Ready Groups" -ForegroundColor Yellow
    Write-Host "---------------------" -ForegroundColor Yellow

    $readyGroups = Invoke-NscimQuery -Handle $h -Sql @"
SELECT
    COUNT(*)            AS readygroupscount,
    MIN(createdatutc)   AS oldestready,
    MAX(createdatutc)   AS newestready
FROM analysisgroups
WHERE status = 'Ready'
"@ | Select-Object -First 1

    if ($readyGroups -and $readyGroups.readygroupscount -gt 0) {
        Write-Host ("  Found {0} Ready groups" -f $readyGroups.readygroupscount) -ForegroundColor Green
        Write-Host ("    Oldest: {0}" -f $readyGroups.oldestready) -ForegroundColor White
        Write-Host ("    Newest: {0}" -f $readyGroups.newestready) -ForegroundColor White
    } else {
        Write-Host "  ISSUE: No groups with status='Ready'" -ForegroundColor Red
        Write-Host "    IntakeWorker may not be creating groups, OR groups assigned immediately" -ForegroundColor Yellow
    }
    Write-Host ""

    # ------------------------------------------------------------------
    # Check 3: Ready Groups with Active Assignments (should be zero)
    # ------------------------------------------------------------------
    Write-Host "Check 3: Ready Groups with Active Assignments" -ForegroundColor Yellow
    Write-Host "---------------------------------------------" -ForegroundColor Yellow

    $assigned = Invoke-NscimQuery -Handle $h -Sql @"
SELECT COUNT(*) AS readygroupswithactiveassignments
FROM analysisgroups ag
INNER JOIN analysisassignments aa ON aa.groupid = ag.id
WHERE ag.status = 'Ready'
    AND aa.state = 'Active'
    AND (aa.leaseuntilutc IS NULL OR aa.leaseuntilutc > NOW() AT TIME ZONE 'UTC')
"@ | Select-Object -First 1

    if ($assigned.readygroupswithactiveassignments -gt 0) {
        Write-Host ("  WARNING: {0} Ready groups have active assignments" -f $assigned.readygroupswithactiveassignments) -ForegroundColor Yellow
        Write-Host "    These groups should be 'AnalystAssigned', not 'Ready'" -ForegroundColor Yellow
    } else {
        Write-Host "  No Ready groups have active assignments (correct)" -ForegroundColor Green
    }
    Write-Host ""

    # ------------------------------------------------------------------
    # Check 4: Analyst Users
    # ------------------------------------------------------------------
    Write-Host "Check 4: Analyst Users" -ForegroundColor Yellow
    Write-Host "----------------------" -ForegroundColor Yellow

    $analysts = Invoke-NscimQuery -Handle $h -Sql @"
SELECT
    u.username,
    u.email,
    r.name AS role_name,
    u.isactive
FROM users u
INNER JOIN roles r ON r.id = u.roleid
WHERE r.name = 'Analyst'
ORDER BY u.username
"@

    if ($analysts.Count -gt 0) {
        Write-Host ("  Found {0} analyst(s):" -f $analysts.Count) -ForegroundColor Green
        $analysts | Format-Table username, email, role_name, isactive -AutoSize
    } else {
        Write-Host "  ISSUE: No users with 'Analyst' role" -ForegroundColor Red
        Write-Host "    Fix: Assign Analyst role (id=8) to users" -ForegroundColor Yellow
    }
    Write-Host ""

    # ------------------------------------------------------------------
    # Check 5: Analyst Active Assignments
    # ------------------------------------------------------------------
    Write-Host "Check 5: Analyst Active Assignments" -ForegroundColor Yellow
    Write-Host "-----------------------------------" -ForegroundColor Yellow

    if ($analysts.Count -gt 0) {
        $activeAssignments = Invoke-NscimQuery -Handle $h -Sql @"
SELECT
    aa.assignedto,
    COUNT(*)               AS activeassignments,
    MAX(aa.leaseuntilutc)  AS latestlease
FROM analysisassignments aa
WHERE aa.role = 'Analyst'
    AND aa.state = 'Active'
    AND (aa.leaseuntilutc IS NULL OR aa.leaseuntilutc > NOW() AT TIME ZONE 'UTC')
GROUP BY aa.assignedto
ORDER BY 2 DESC
"@

        if ($activeAssignments.Count -gt 0) {
            Write-Host "  Active assignments per analyst:" -ForegroundColor Cyan
            $activeAssignments | Format-Table assignedto, activeassignments, latestlease -AutoSize
            $maxConcurrent = if ($settings) { [int]$settings.maxconcurrentperuser } else { 0 }
            foreach ($row in $activeAssignments) {
                if ($maxConcurrent -gt 0 -and $row.activeassignments -ge $maxConcurrent) {
                    Write-Host ("  WARNING: {0} is at max capacity ({1} of {2})" -f $row.assignedto, $row.activeassignments, $maxConcurrent) -ForegroundColor Yellow
                }
            }
        } else {
            Write-Host "  No active assignments (analysts are available)" -ForegroundColor Green
        }
    } else {
        Write-Host "  Skipping (no analysts found)" -ForegroundColor Gray
    }
    Write-Host ""

    # ------------------------------------------------------------------
    # Check 6: Container Workflow Stages
    #   Note table name change: containercompletenessstatuses (plural)
    # ------------------------------------------------------------------
    Write-Host "Check 6: Container Workflow Stages" -ForegroundColor Yellow
    Write-Host "----------------------------------" -ForegroundColor Yellow

    $workflows = Invoke-NscimQuery -Handle $h -Sql @"
SELECT
    ag.groupidentifier,
    ag.status                          AS groupstatus,
    ccs.workflowstage,
    COUNT(DISTINCT ar.containernumber) AS containersinstage
FROM analysisgroups ag
INNER JOIN analysisrecords ar             ON ar.groupid = ag.id
INNER JOIN containercompletenessstatuses ccs
       ON ccs.containernumber = ar.containernumber
WHERE ag.status = 'Ready'
GROUP BY ag.groupidentifier, ag.status, ccs.workflowstage
ORDER BY ag.groupidentifier
"@

    if ($workflows.Count -gt 0) {
        Write-Host "  Workflow stages for Ready groups:" -ForegroundColor Cyan
        $workflows | Format-Table groupidentifier, groupstatus, workflowstage, containersinstage -AutoSize
        $invalid = $workflows | Where-Object { $_.workflowstage -in @('Audit','Completed') }
        if ($invalid) {
            Write-Host "  ISSUE: Some containers in 'Audit' or 'Completed' stage" -ForegroundColor Red
            Write-Host "    AssignmentWorker filters these out — won't be assigned" -ForegroundColor Yellow
            Write-Host "    Fix: reset workflowstage to 'ImageAnalysis' or 'Pending'" -ForegroundColor Yellow
        } else {
            Write-Host "  All containers in valid stages for assignment" -ForegroundColor Green
        }
    } else {
        Write-Host "  No workflow-stage data found for Ready groups" -ForegroundColor Yellow
    }
    Write-Host ""

    # ------------------------------------------------------------------
    # Check 7: Expired Active Assignments
    # ------------------------------------------------------------------
    Write-Host "Check 7: Expired Active Assignments" -ForegroundColor Yellow
    Write-Host "-----------------------------------" -ForegroundColor Yellow

    $expired = Invoke-NscimQuery -Handle $h -Sql @"
SELECT
    COUNT(*)             AS expiredbutactive,
    MIN(leaseuntilutc)   AS oldestexpired,
    MAX(leaseuntilutc)   AS newestexpired
FROM analysisassignments
WHERE state = 'Active'
    AND leaseuntilutc < NOW() AT TIME ZONE 'UTC'
"@ | Select-Object -First 1

    if ($expired.expiredbutactive -gt 0) {
        Write-Host ("  ISSUE: {0} expired assignments still 'Active'" -f $expired.expiredbutactive) -ForegroundColor Red
        Write-Host "    Should be cleaned by AssignmentWorker" -ForegroundColor Yellow
        Write-Host ("    Oldest expired: {0}" -f $expired.oldestexpired) -ForegroundColor White
        Write-Host ("    Newest expired: {0}" -f $expired.newestexpired) -ForegroundColor White
    } else {
        Write-Host "  No expired active assignments (cleanup working)" -ForegroundColor Green
    }
    Write-Host ""

    # ------------------------------------------------------------------
    # Check 8: Analyst User Readiness
    #   Note: userreadiness has its own `username` and `role`; join via users
    # ------------------------------------------------------------------
    Write-Host "Check 8: Analyst User Readiness" -ForegroundColor Yellow
    Write-Host "-------------------------------" -ForegroundColor Yellow

    $readiness = Invoke-NscimQuery -Handle $h -Sql @"
SELECT
    ur.username,
    ur.isready,
    ur.lastheartbeat,
    EXTRACT(EPOCH FROM (NOW() AT TIME ZONE 'UTC' - ur.lastheartbeat))::bigint AS secondssinceheartbeat
FROM userreadiness ur
INNER JOIN users u ON u.username = ur.username
INNER JOIN roles r ON r.id = u.roleid
WHERE r.name = 'Analyst'
ORDER BY ur.lastheartbeat DESC
"@

    if ($readiness.Count -gt 0) {
        Write-Host "  Analyst readiness status:" -ForegroundColor Cyan
        $readiness | Format-Table username, isready, lastheartbeat, secondssinceheartbeat -AutoSize
        $notReady = $readiness | Where-Object { -not $_.isready }
        $stale    = $readiness | Where-Object { $_.secondssinceheartbeat -gt 300 }
        if ($notReady) {
            Write-Host "  WARNING: Some analysts marked NOT READY" -ForegroundColor Yellow
        }
        if ($stale) {
            Write-Host "  WARNING: Some analysts have stale heartbeats (>5 min)" -ForegroundColor Yellow
        }
        if (-not $notReady -and -not $stale) {
            Write-Host "  All analysts are ready" -ForegroundColor Green
        }
    } else {
        Write-Host "  No userreadiness records found for analysts" -ForegroundColor Yellow
        Write-Host "    Analysts must be active (heartbeat within 5 min)" -ForegroundColor Yellow
    }
    Write-Host ""

    # ------------------------------------------------------------------
    # Check 9: Complete Containers (IntakeWorker input)
    # ------------------------------------------------------------------
    Write-Host "Check 9: Complete Containers (IntakeWorker input)" -ForegroundColor Yellow
    Write-Host "--------------------------------------------------" -ForegroundColor Yellow

    $complete = Invoke-NscimQuery -Handle $h -Sql @"
SELECT
    COUNT(*)                       AS completecontainers,
    COUNT(DISTINCT groupidentifier) AS uniquegroups,
    MIN(updatedat)                 AS oldestcomplete,
    MAX(updatedat)                 AS newestcomplete
FROM containercompletenessstatuses
WHERE status = 'Complete'
"@ | Select-Object -First 1

    if ($complete.completecontainers -gt 0) {
        Write-Host ("  Found {0} complete containers" -f $complete.completecontainers) -ForegroundColor Green
        Write-Host ("    Unique groups : {0}" -f $complete.uniquegroups) -ForegroundColor White
        Write-Host ("    Oldest        : {0}" -f $complete.oldestcomplete) -ForegroundColor White
        Write-Host ("    Newest        : {0}" -f $complete.newestcomplete) -ForegroundColor White
    } else {
        Write-Host "  No containers with status='Complete'" -ForegroundColor Yellow
        Write-Host "    IntakeWorker needs complete containers to create groups" -ForegroundColor Yellow
    }
    Write-Host ""

    # ------------------------------------------------------------------
    # Summary
    # ------------------------------------------------------------------
    Write-Host "DIAGNOSIS SUMMARY" -ForegroundColor Cyan
    Write-Host "=================" -ForegroundColor Cyan
    Write-Host ""

    $issues = @()
    if ($settings -and -not $settings.enabled)                          { $issues += "Service is DISABLED" }
    if ($settings -and $settings.assignmentmode -ne 'Auto')             { $issues += "AssignmentMode is '$($settings.assignmentmode)' (not 'Auto')" }
    if (-not $readyGroups -or $readyGroups.readygroupscount -eq 0)      { $issues += "No Ready groups found" }
    if ($analysts.Count -eq 0)                                          { $issues += "No users with 'Analyst' role" }
    if ($expired.expiredbutactive -gt 0)                                { $issues += "$($expired.expiredbutactive) expired assignments not cleaned up" }

    if ($issues.Count -eq 0) {
        Write-Host "  No critical issues found." -ForegroundColor Green
        Write-Host ""
        Write-Host "  If assignments still aren't working, check:" -ForegroundColor Yellow
        Write-Host "    1. Application logs for AssignmentWorker errors" -ForegroundColor White
        Write-Host "    2. workflowstage of containers (must be ImageAnalysis or Pending)" -ForegroundColor White
        Write-Host "    3. Analyst capacity (maxconcurrentperuser)" -ForegroundColor White
        Write-Host "    4. UserReadiness (analysts must be active)" -ForegroundColor White
    } else {
        Write-Host "  Issues found:" -ForegroundColor Red
        foreach ($i in $issues) { Write-Host ("    - {0}" -f $i) -ForegroundColor Red }
    }

    Write-Host ""
    Write-Host "Next steps:" -ForegroundColor Cyan
    Write-Host "  1. Review issues above" -ForegroundColor White
    Write-Host "  2. Check application logs for AssignmentWorker activity" -ForegroundColor White
    Write-Host "  3. Fix identified issues" -ForegroundColor White
    Write-Host "  4. Re-run diagnostic to verify fixes" -ForegroundColor White
}
finally {
    Close-NscimConnection -Handle $h
}

# Comprehensive Assignment Flow Diagnostic — Postgres edition
#
# Postgres equivalent of scripts/Diagnose-AssignmentFlow.ps1.
# Checks why assignments aren't coming through even with a signed-in analyst.
#
# Read-only — uses nscim_app role.

[CmdletBinding()]
param(
    [string]$PgHost = "127.0.0.1",
    [int]$Port = 5432,
    [string]$Database = "nickscan_production",
    [string]$TenantId = "1"
)

. "$PSScriptRoot\_NpgsqlHelper.ps1"

# Continue past errors intentionally: many independent assignment-flow checks.
$ErrorActionPreference = "Continue"

Write-Host "Assignment Flow Diagnostic — Signed-In Analyst (Postgres)" -ForegroundColor Cyan
Write-Host "=========================================================" -ForegroundColor Cyan
Write-Host ""

$h = Open-NscimConnection -PgHost $PgHost -Port $Port -Database $Database -TenantId $TenantId

try {
    # ------------------------------------------------------------------
    # Check 1: Analyst User Readiness
    # ------------------------------------------------------------------
    Write-Host "Check 1: Analyst User Readiness" -ForegroundColor Yellow
    Write-Host "-------------------------------" -ForegroundColor Yellow

    $readiness = Invoke-NscimQuery -Handle $h -Sql @"
SELECT
    ur.username,
    ur.role,
    ur.isready,
    ur.lastheartbeat,
    EXTRACT(EPOCH FROM (NOW() AT TIME ZONE 'UTC' - ur.lastheartbeat))::bigint AS secondssinceheartbeat,
    CASE
        WHEN ur.lastheartbeat > NOW() AT TIME ZONE 'UTC' - INTERVAL '2 minutes' THEN 'ACTIVE'
        WHEN ur.lastheartbeat > NOW() AT TIME ZONE 'UTC' - INTERVAL '5 minutes' THEN 'IDLE'
        ELSE 'STALE'
    END AS heartbeatstatus
FROM userreadiness ur
INNER JOIN users u ON u.username = ur.username
INNER JOIN roles r ON r.id = u.roleid
WHERE r.name = 'Analyst' AND u.isactive = true
ORDER BY ur.lastheartbeat DESC
"@

    if ($readiness.Count -gt 0) {
        Write-Host "  Analyst Readiness Status:" -ForegroundColor Cyan
        $readiness | Format-Table username, role, isready, lastheartbeat, secondssinceheartbeat, heartbeatstatus -AutoSize
        $readyCount = ($readiness | Where-Object { $_.isready -and $_.secondssinceheartbeat -lt 120 }).Count
        if ($readyCount -gt 0) {
            Write-Host ("  SUCCESS: {0} analyst(s) READY (isready=true, heartbeat < 2 min)" -f $readyCount) -ForegroundColor Green
        } else {
            Write-Host "  ISSUE: No analysts are READY" -ForegroundColor Red
            Write-Host "    Analysts must have isready=true AND heartbeat < 2 minutes" -ForegroundColor Yellow
        }
    } else {
        Write-Host "  ISSUE: No userreadiness rows found for analysts" -ForegroundColor Red
        Write-Host "    Analysts need to log in to create userreadiness records" -ForegroundColor Yellow
    }
    Write-Host ""

    # ------------------------------------------------------------------
    # Check 2: Ready Groups Available
    # ------------------------------------------------------------------
    Write-Host "Check 2: Ready Groups Available" -ForegroundColor Yellow
    Write-Host "-------------------------------" -ForegroundColor Yellow

    $readyGroups = Invoke-NscimQuery -Handle $h -Sql @"
SELECT
    COUNT(*)            AS readycount,
    MIN(createdatutc)   AS oldestready,
    MAX(createdatutc)   AS newestready
FROM analysisgroups
WHERE status = 'Ready'
"@ | Select-Object -First 1

    if ($readyGroups.readycount -gt 0) {
        Write-Host ("  SUCCESS: {0} Ready groups available" -f $readyGroups.readycount) -ForegroundColor Green
        Write-Host ("    Oldest: {0}" -f $readyGroups.oldestready) -ForegroundColor White
        Write-Host ("    Newest: {0}" -f $readyGroups.newestready) -ForegroundColor White
    } else {
        Write-Host "  ISSUE: No Ready groups available" -ForegroundColor Red
    }
    Write-Host ""

    # ------------------------------------------------------------------
    # Check 3: Active Assignments per Analyst
    # ------------------------------------------------------------------
    Write-Host "Check 3: Active Assignments per Analyst" -ForegroundColor Yellow
    Write-Host "---------------------------------------" -ForegroundColor Yellow

    $activeAssignments = Invoke-NscimQuery -Handle $h -Sql @"
SELECT
    aa.assignedto,
    COUNT(*)              AS activecount,
    MAX(aa.leaseuntilutc) AS latestlease,
    CASE WHEN MAX(aa.leaseuntilutc) < NOW() AT TIME ZONE 'UTC' THEN 'EXPIRED' ELSE 'ACTIVE' END AS leasestatus
FROM analysisassignments aa
INNER JOIN users u ON u.username = aa.assignedto
INNER JOIN roles r ON r.id = u.roleid
WHERE aa.role = 'Analyst'
    AND aa.state = 'Active'
    AND r.name = 'Analyst'
GROUP BY aa.assignedto
ORDER BY 2 DESC
"@

    if ($activeAssignments.Count -gt 0) {
        Write-Host "  Active assignments per analyst:" -ForegroundColor Cyan
        $activeAssignments | Format-Table assignedto, activecount, latestlease, leasestatus -AutoSize

        $settings = Invoke-NscimQuery -Handle $h -Sql "SELECT maxconcurrentperuser FROM analysissettings" |
                    Select-Object -First 1
        $maxConcurrent = if ($settings) { [int]$settings.maxconcurrentperuser } else { 0 }
        Write-Host ("  Max concurrent per user: {0}" -f $maxConcurrent) -ForegroundColor Cyan
        foreach ($r in $activeAssignments) {
            if ($maxConcurrent -gt 0 -and $r.activecount -ge $maxConcurrent) {
                Write-Host ("  WARNING: {0} at max capacity ({1} >= {2})" -f $r.assignedto, $r.activecount, $maxConcurrent) -ForegroundColor Yellow
            }
        }
    } else {
        Write-Host "  INFO: No active assignments found" -ForegroundColor Gray
    }
    Write-Host ""

    # ------------------------------------------------------------------
    # Check 4: Expired Assignments
    # ------------------------------------------------------------------
    Write-Host "Check 4: Expired Assignments" -ForegroundColor Yellow
    Write-Host "----------------------------" -ForegroundColor Yellow

    $expired = Invoke-NscimQuery -Handle $h -Sql @"
SELECT
    COUNT(*)             AS expiredcount,
    MIN(leaseuntilutc)   AS oldestexpired,
    MAX(leaseuntilutc)   AS newestexpired
FROM analysisassignments
WHERE state = 'Active'
    AND leaseuntilutc < NOW() AT TIME ZONE 'UTC'
"@ | Select-Object -First 1

    if ($expired.expiredcount -gt 0) {
        Write-Host ("  ISSUE: {0} expired assignments still 'Active'" -f $expired.expiredcount) -ForegroundColor Red
        Write-Host "    Should be cleaned by AssignmentWorker" -ForegroundColor Yellow
        Write-Host ("    Oldest expired: {0}" -f $expired.oldestexpired) -ForegroundColor White
        Write-Host ("    Newest expired: {0}" -f $expired.newestexpired) -ForegroundColor White
    } else {
        Write-Host "  SUCCESS: No expired active assignments" -ForegroundColor Green
    }
    Write-Host ""

    # ------------------------------------------------------------------
    # Check 5: Ready Groups with Active Assignments (shouldn't happen)
    # ------------------------------------------------------------------
    Write-Host "Check 5: Ready Groups with Active Assignments" -ForegroundColor Yellow
    Write-Host "---------------------------------------------" -ForegroundColor Yellow

    $readyWithAssignments = Invoke-NscimQuery -Handle $h -Sql @"
SELECT COUNT(*) AS count
FROM analysisgroups ag
INNER JOIN analysisassignments aa ON aa.groupid = ag.id
WHERE ag.status = 'Ready'
    AND aa.state = 'Active'
    AND (aa.leaseuntilutc IS NULL OR aa.leaseuntilutc > NOW() AT TIME ZONE 'UTC')
"@ | Select-Object -First 1

    if ($readyWithAssignments.count -gt 0) {
        Write-Host ("  WARNING: {0} Ready groups have active assignments" -f $readyWithAssignments.count) -ForegroundColor Yellow
        Write-Host "    These groups should be 'AnalystAssigned', not 'Ready'" -ForegroundColor Yellow
    } else {
        Write-Host "  SUCCESS: No Ready groups have active assignments" -ForegroundColor Green
    }
    Write-Host ""

    # ------------------------------------------------------------------
    # Check 6: Recent Assignment Activity
    # ------------------------------------------------------------------
    Write-Host "Check 6: Recent Assignment Activity" -ForegroundColor Yellow
    Write-Host "-----------------------------------" -ForegroundColor Yellow

    $recentAssignments = Invoke-NscimQuery -Handle $h -Sql @"
SELECT
    aa.id,
    aa.assignedto,
    aa.role,
    aa.state,
    aa.createdatutc,
    aa.leaseuntilutc,
    ag.groupidentifier,
    EXTRACT(EPOCH FROM (NOW() AT TIME ZONE 'UTC' - aa.createdatutc))::bigint / 60 AS minutesago
FROM analysisassignments aa
INNER JOIN analysisgroups ag ON ag.id = aa.groupid
WHERE aa.role = 'Analyst'
ORDER BY aa.createdatutc DESC
LIMIT 10
"@

    if ($recentAssignments.Count -gt 0) {
        Write-Host "  Most recent assignments:" -ForegroundColor Cyan
        $recentAssignments | Format-Table assignedto, state, groupidentifier, minutesago -AutoSize
        $mostRecent = [int64]$recentAssignments[0].minutesago
        if ($mostRecent -lt 5) {
            Write-Host ("  SUCCESS: Recent activity (last {0} min ago)" -f $mostRecent) -ForegroundColor Green
        } elseif ($mostRecent -lt 60) {
            Write-Host ("  WARNING: No recent assignments (last {0} min ago)" -f $mostRecent) -ForegroundColor Yellow
        } else {
            Write-Host ("  ISSUE: No recent assignments (last {0} min ago)" -f $mostRecent) -ForegroundColor Red
            Write-Host "    AssignmentWorker may not be running or processing" -ForegroundColor Yellow
        }
    } else {
        Write-Host "  ISSUE: No assignment history found" -ForegroundColor Red
    }
    Write-Host ""

    # ------------------------------------------------------------------
    # Summary
    # ------------------------------------------------------------------
    Write-Host "========================================" -ForegroundColor Cyan
    Write-Host "DIAGNOSIS SUMMARY" -ForegroundColor Cyan
    Write-Host "========================================" -ForegroundColor Cyan
    Write-Host ""

    $issues = @()
    $warnings = @()

    $readyAnalysts = $readiness | Where-Object { $_.isready -and $_.secondssinceheartbeat -lt 120 }
    if (-not $readyAnalysts -or $readyAnalysts.Count -eq 0) {
        $issues += "No analysts are READY (isready=true AND heartbeat < 2 min)"
    }
    if ($expired.expiredcount -gt 1000) {
        $warnings += "$($expired.expiredcount) expired assignments need cleanup"
    }
    if ($readyGroups.readycount -eq 0) {
        $issues += "No Ready groups available for assignment"
    }
    if ($recentAssignments.Count -gt 0) {
        $mostRecent = [int64]$recentAssignments[0].minutesago
        if ($mostRecent -gt 60) {
            $issues += "No recent assignment activity (last $mostRecent min ago)"
        }
    }

    if ($issues.Count -eq 0 -and $warnings.Count -eq 0) {
        Write-Host "  SUCCESS: All checks passed!" -ForegroundColor Green
        Write-Host ""
        Write-Host "  If assignments still not working, check:" -ForegroundColor Yellow
        Write-Host "    1. Application logs for AssignmentWorker errors" -ForegroundColor White
        Write-Host "    2. workflowstage (must be ImageAnalysis or Pending)" -ForegroundColor White
        Write-Host "    3. AssignmentWorker service is running" -ForegroundColor White
    } else {
        if ($issues.Count -gt 0) {
            Write-Host "  CRITICAL ISSUES:" -ForegroundColor Red
            foreach ($i in $issues) { Write-Host ("    - {0}" -f $i) -ForegroundColor Red }
            Write-Host ""
        }
        if ($warnings.Count -gt 0) {
            Write-Host "  WARNINGS:" -ForegroundColor Yellow
            foreach ($w in $warnings) { Write-Host ("    - {0}" -f $w) -ForegroundColor Yellow }
            Write-Host ""
        }
    }
}
finally {
    Close-NscimConnection -Handle $h
}

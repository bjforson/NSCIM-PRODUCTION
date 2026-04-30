# Real-Time Analyst Readiness Check — Postgres edition
#
# Postgres equivalent of scripts/Check-CurrentReadiness.ps1.
# Run this while an analyst is actively on the page.
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

$ErrorActionPreference = "Continue"

Write-Host "Real-Time Analyst Readiness Check (Postgres)" -ForegroundColor Cyan
Write-Host "============================================" -ForegroundColor Cyan
Write-Host ""

$h = Open-NscimConnection -PgHost $PgHost -Port $Port -Database $Database -TenantId $TenantId

try {
    # Current readiness
    $readiness = Invoke-NscimQuery -Handle $h -Sql @"
SELECT
    ur.username,
    ur.role,
    ur.isready,
    ur.lastheartbeat,
    EXTRACT(EPOCH FROM (NOW() AT TIME ZONE 'UTC' - ur.lastheartbeat))::bigint AS secondsago,
    CASE
        WHEN ur.lastheartbeat > NOW() AT TIME ZONE 'UTC' - INTERVAL '2 minutes' AND ur.isready = true  THEN 'READY'
        WHEN ur.lastheartbeat > NOW() AT TIME ZONE 'UTC' - INTERVAL '2 minutes' AND ur.isready = false THEN 'NOT READY (isready=false)'
        WHEN ur.lastheartbeat > NOW() AT TIME ZONE 'UTC' - INTERVAL '5 minutes'                        THEN 'IDLE'
        ELSE 'STALE'
    END AS status
FROM userreadiness ur
INNER JOIN users u ON u.username = ur.username
INNER JOIN roles r ON r.id = u.roleid
WHERE r.name = 'Analyst' AND u.isactive = true
ORDER BY ur.lastheartbeat DESC
"@

    Write-Host "Current Analyst Readiness:" -ForegroundColor Yellow
    Write-Host "--------------------------" -ForegroundColor Yellow

    if ($readiness.Count -gt 0) {
        $readiness | Format-Table username, role, isready, lastheartbeat, secondsago, status -AutoSize
        $ready = $readiness | Where-Object { $_.isready -and $_.secondsago -lt 120 }
        if ($ready) {
            Write-Host ("  SUCCESS: {0} analyst(s) are READY" -f $ready.Count) -ForegroundColor Green
            foreach ($a in $ready) {
                Write-Host ("    - {0}: Ready (heartbeat {1}s ago)" -f $a.username, $a.secondsago) -ForegroundColor Green
            }
        } else {
            Write-Host "  ISSUE: No analysts are READY" -ForegroundColor Red
            Write-Host ""
            Write-Host "  Requirements for READY status:" -ForegroundColor Yellow
            Write-Host "    1. isready = true" -ForegroundColor White
            Write-Host "    2. lastheartbeat < 120 seconds ago" -ForegroundColor White
            Write-Host ""
            Write-Host "  Current status:" -ForegroundColor Cyan
            foreach ($a in $readiness) {
                $rs = if ($a.isready) { 'isready=true' } else { 'isready=false' }
                $hb = if ($a.secondsago -lt 120) { 'Heartbeat OK' } else { ("Heartbeat STALE ({0}s)" -f $a.secondsago) }
                Write-Host ("    - {0}: {1}, {2}" -f $a.username, $rs, $hb) -ForegroundColor White
            }
        }
    } else {
        Write-Host "  ISSUE: No userreadiness rows found for analysts" -ForegroundColor Red
        Write-Host "    The analyst hasn't created a userreadiness row yet." -ForegroundColor Yellow
        Write-Host "    The page may need a refresh, or SignalR may not be connected." -ForegroundColor Yellow
    }
    Write-Host ""

    # Recent assignments — last 5 minutes
    Write-Host "Recent Assignment Activity (last 5 minutes):" -ForegroundColor Yellow
    Write-Host "--------------------------------------------" -ForegroundColor Yellow

    $recent = Invoke-NscimQuery -Handle $h -Sql @"
SELECT
    aa.id,
    aa.assignedto,
    aa.role,
    aa.state,
    aa.createdatutc,
    ag.groupidentifier,
    EXTRACT(EPOCH FROM (NOW() AT TIME ZONE 'UTC' - aa.createdatutc))::bigint AS secondsago
FROM analysisassignments aa
INNER JOIN analysisgroups ag ON ag.id = aa.groupid
WHERE aa.role = 'Analyst'
    AND aa.createdatutc > NOW() AT TIME ZONE 'UTC' - INTERVAL '5 minutes'
ORDER BY aa.createdatutc DESC
"@

    if ($recent.Count -gt 0) {
        Write-Host ("  SUCCESS: {0} assignment(s) in last 5 minutes!" -f $recent.Count) -ForegroundColor Green
        $recent | Format-Table assignedto, groupidentifier, state, secondsago -AutoSize
    } else {
        Write-Host "  No assignments created in last 5 minutes" -ForegroundColor Yellow
        Write-Host ""
        Write-Host "  Possible reasons:" -ForegroundColor Cyan
        Write-Host "    1. Analyst not READY (see above)" -ForegroundColor White
        Write-Host "    2. AssignmentWorker not running" -ForegroundColor White
        Write-Host "    3. No Ready groups available" -ForegroundColor White
        Write-Host "    4. AssignmentWorker errors in logs" -ForegroundColor White
    }
    Write-Host ""

    # Active assignment count
    Write-Host "Analyst Active Assignment Count:" -ForegroundColor Yellow
    Write-Host "--------------------------------" -ForegroundColor Yellow

    $active = Invoke-NscimQuery -Handle $h -Sql @"
SELECT
    aa.assignedto,
    COUNT(*) AS activecount
FROM analysisassignments aa
INNER JOIN users u ON u.username = aa.assignedto
INNER JOIN roles r ON r.id = u.roleid
WHERE aa.role = 'Analyst'
    AND aa.state = 'Active'
    AND (aa.leaseuntilutc IS NULL OR aa.leaseuntilutc > NOW() AT TIME ZONE 'UTC')
    AND r.name = 'Analyst'
GROUP BY aa.assignedto
"@

    if ($active.Count -gt 0) {
        $settings = Invoke-NscimQuery -Handle $h -Sql "SELECT maxconcurrentperuser FROM analysissettings" |
                    Select-Object -First 1
        $maxConcurrent = if ($settings) { [int]$settings.maxconcurrentperuser } else { 0 }
        Write-Host ("  Active assignments per analyst (Max: {0}):" -f $maxConcurrent) -ForegroundColor Cyan
        $active | Format-Table assignedto, activecount -AutoSize
        foreach ($a in $active) {
            if ($maxConcurrent -gt 0 -and $a.activecount -ge $maxConcurrent) {
                Write-Host ("  WARNING: {0} at max ({1} >= {2})" -f $a.assignedto, $a.activecount, $maxConcurrent) -ForegroundColor Yellow
            } else {
                Write-Host ("  OK: {0} has {1} active (below max)" -f $a.assignedto, $a.activecount) -ForegroundColor Green
            }
        }
    } else {
        Write-Host "  SUCCESS: No active assignments (analysts available for new work)" -ForegroundColor Green
    }
    Write-Host ""
}
finally {
    Close-NscimConnection -Handle $h
}

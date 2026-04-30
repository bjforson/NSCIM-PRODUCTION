# Heartbeat Issue Diagnosis — Postgres edition
#
# Postgres equivalent of scripts/Diagnose-HeartbeatIssue.ps1.
# Diagnoses why heartbeat isn't updating even when user is on the page.
# Optionally applies a temporary manual heartbeat update.
#
# IMPORTANT: The temporary-workaround UPDATE writes to userreadiness. By default
# nscim_app can perform this UPDATE (it owns the application data). If the script
# fails with permission/RLS errors, re-run with -UseSuperuser to use the postgres
# role instead. Pass -ApplyManualUpdate to actually run the UPDATE; without it
# the script is fully read-only.

[CmdletBinding()]
param(
    [string]$PgHost = "127.0.0.1",
    [int]$Port = 5432,
    [string]$Database = "nickscan_production",
    [string]$TenantId = "1",
    [Parameter(Mandatory=$true)][string]$Username,
    [switch]$ApplyManualUpdate,
    [switch]$UseSuperuser
)

. "$PSScriptRoot\_NpgsqlHelper.ps1"

$ErrorActionPreference = "Continue"

Write-Host "Heartbeat Issue Diagnosis (Postgres)" -ForegroundColor Cyan
Write-Host "====================================" -ForegroundColor Cyan
Write-Host "  Username: $Username"
Write-Host ""

$h = Open-NscimConnection -PgHost $PgHost -Port $Port -Database $Database -TenantId $TenantId -UseSuperuser:$UseSuperuser

try {
    Write-Host "Current Heartbeat Status:" -ForegroundColor Yellow
    Write-Host "-------------------------" -ForegroundColor Yellow

    $readiness = Invoke-NscimQuery -Handle $h -Sql @"
SELECT
    username,
    role,
    isready,
    lastheartbeat,
    EXTRACT(EPOCH FROM (NOW() AT TIME ZONE 'UTC' - lastheartbeat))::bigint AS secondsago,
    CASE
        WHEN lastheartbeat > NOW() AT TIME ZONE 'UTC' - INTERVAL '2 minutes' THEN 'ACTIVE'
        WHEN lastheartbeat > NOW() AT TIME ZONE 'UTC' - INTERVAL '5 minutes' THEN 'IDLE'
        ELSE 'STALE'
    END AS status
FROM userreadiness
WHERE username = @username AND role = 'Analyst'
"@ -Parameters @{ '@username' = $Username }

    if ($readiness.Count -gt 0) {
        $readiness | Format-Table username, role, isready, lastheartbeat, secondsago, status -AutoSize
        $row = $readiness[0]
        if ($row.secondsago -ge 120) {
            Write-Host ("  ISSUE: Heartbeat is STALE ({0}s ago)" -f $row.secondsago) -ForegroundColor Red
            Write-Host ""
            Write-Host "  Possible causes:" -ForegroundColor Yellow
            Write-Host "    1. Browser not calling heartbeat API" -ForegroundColor White
            Write-Host "    2. API endpoint returning error (401, 403, 500)" -ForegroundColor White
            Write-Host "    3. SignalR connection not established" -ForegroundColor White
            Write-Host "    4. JavaScript errors on page" -ForegroundColor White
            Write-Host "    5. Authentication token expired" -ForegroundColor White
            Write-Host ""
            Write-Host "  SOLUTION: Check browser console + network tab" -ForegroundColor Cyan
            Write-Host "    - F12, Network, filter 'heartbeat'" -ForegroundColor White
            Write-Host "    - Look for POST /api/image-analysis/user/heartbeat" -ForegroundColor White
            Write-Host "    - Verify status 200 OK" -ForegroundColor White
            Write-Host "    - Check Console tab for JavaScript errors" -ForegroundColor White
        } else {
            Write-Host ("  Heartbeat is fresh ({0}s ago)" -f $row.secondsago) -ForegroundColor Green
        }
    } else {
        Write-Host "  ISSUE: No userreadiness row found" -ForegroundColor Red
        Write-Host "    The page needs to call POST /api/image-analysis/user/ready first" -ForegroundColor Yellow
    }
    Write-Host ""

    # Optional manual heartbeat update — only if explicitly requested.
    if ($ApplyManualUpdate) {
        Write-Host "Temporary Workaround:" -ForegroundColor Yellow
        Write-Host "---------------------" -ForegroundColor Yellow
        Write-Host "  Updating heartbeat manually (TEMPORARY — page should do this every 30s)" -ForegroundColor Gray

        try {
            $rows = Invoke-NscimNonQuery -Handle $h -Sql @"
UPDATE userreadiness
SET lastheartbeat = NOW() AT TIME ZONE 'UTC',
    isready       = true
WHERE username = @username AND role = 'Analyst'
"@ -Parameters @{ '@username' = $Username }
            # Read-only helper opened a transaction; commit it for the UPDATE.
            $h.Transaction.Commit()
            # Re-open a fresh tx so Close-NscimConnection's commit-attempt is benign.
            $h.Transaction = $h.Connection.BeginTransaction()
            $cmd = $h.Connection.CreateCommand()
            $cmd.Transaction = $h.Transaction
            $cmd.CommandText = "SET LOCAL app.tenant_id = '$TenantId'"
            $null = $cmd.ExecuteNonQuery()

            if ($rows -gt 0) {
                Write-Host ("  SUCCESS: Heartbeat updated manually ({0} row affected)" -f $rows) -ForegroundColor Green
                Write-Host ""
                Write-Host "  NOTE: This is temporary. The page should update heartbeat every 30s." -ForegroundColor Yellow
                Write-Host "  If heartbeat goes stale again, check browser console for API errors." -ForegroundColor Yellow
            } else {
                Write-Host "  WARN: UPDATE affected 0 rows — userreadiness row may not exist for this username/role." -ForegroundColor Yellow
            }
        } catch {
            Write-Host "  ERROR: Failed to update heartbeat" -ForegroundColor Red
            Write-Host ("    {0}" -f $_.Exception.Message) -ForegroundColor Red
            Write-Host "    If this is an RLS / permission error, re-run with -UseSuperuser" -ForegroundColor Yellow
        }
    } else {
        Write-Host "Skipping manual heartbeat UPDATE (pass -ApplyManualUpdate to apply)" -ForegroundColor Gray
    }
    Write-Host ""
}
finally {
    Close-NscimConnection -Handle $h
}

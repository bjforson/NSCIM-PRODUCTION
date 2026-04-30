# Fix UserReadiness — set analyst as ready and update heartbeat — Postgres edition
#
# Postgres equivalent of scripts/Fix-UserReadiness.ps1.
# Manually marks an analyst as ready and refreshes their heartbeat.
#
# WRITE script — performs an UPDATE. By default does a dry-run; pass -Apply to commit.
# Confirms target row exists before writing. Uses nscim_app by default; falls back
# to -UseSuperuser if the writer needs the postgres role.

[CmdletBinding()]
param(
    [string]$PgHost = "127.0.0.1",
    [int]$Port = 5432,
    [string]$Database = "nickscan_production",
    [string]$TenantId = "1",
    [Parameter(Mandatory=$true)][string]$Username,
    [switch]$Apply,
    [switch]$UseSuperuser
)

. "$PSScriptRoot\_NpgsqlHelper.ps1"

$ErrorActionPreference = "Stop"

Write-Host "Fix UserReadiness for Analyst (Postgres)" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ("  Username : {0}" -f $Username)
Write-Host ("  Mode     : {0}" -f $(if ($Apply) { "APPLY (will commit)" } else { "DRY RUN (no changes)" }))
Write-Host ""

$h = Open-NscimConnection -PgHost $PgHost -Port $Port -Database $Database -TenantId $TenantId -UseSuperuser:$UseSuperuser

try {
    # 1. Confirm there is a row to update.
    $existing = Invoke-NscimQuery -Handle $h -Sql @"
SELECT username, role, isready, lastheartbeat,
       EXTRACT(EPOCH FROM (NOW() AT TIME ZONE 'UTC' - lastheartbeat))::bigint AS secondsago
FROM userreadiness
WHERE username = @username AND role = 'Analyst'
"@ -Parameters @{ '@username' = $Username }

    if ($existing.Count -eq 0) {
        Write-Host "  ERROR: No userreadiness row found for username='$Username' role='Analyst'." -ForegroundColor Red
        Write-Host "    The user must hit the Image Analysis page once to create the row." -ForegroundColor Yellow
        return
    }

    Write-Host "Current row (before update):" -ForegroundColor Yellow
    $existing | Format-Table username, role, isready, lastheartbeat, secondsago -AutoSize

    if (-not $Apply) {
        Write-Host "Dry run — would UPDATE userreadiness SET isready=true, lastheartbeat=NOW()…" -ForegroundColor Cyan
        Write-Host "Re-run with -Apply to commit." -ForegroundColor Cyan
        return
    }

    # 2. Apply the UPDATE.
    Write-Host "Updating userreadiness…" -ForegroundColor Yellow
    $rows = Invoke-NscimNonQuery -Handle $h -Sql @"
UPDATE userreadiness
SET isready       = true,
    lastheartbeat = NOW() AT TIME ZONE 'UTC',
    lastchangedat = NOW() AT TIME ZONE 'UTC',
    changedby     = @username
WHERE username = @username AND role = 'Analyst'
"@ -Parameters @{ '@username' = $Username }

    # Commit and reopen a tx so Close-NscimConnection's safe-commit doesn't fail.
    $h.Transaction.Commit()
    $h.Transaction = $h.Connection.BeginTransaction()
    $cmd = $h.Connection.CreateCommand()
    $cmd.Transaction = $h.Transaction
    $cmd.CommandText = "SET LOCAL app.tenant_id = '$TenantId'"
    $null = $cmd.ExecuteNonQuery()

    Write-Host ("  SUCCESS: Updated {0} row" -f $rows) -ForegroundColor Green
    Write-Host ""

    # 3. Verify.
    $after = Invoke-NscimQuery -Handle $h -Sql @"
SELECT username, role, isready, lastheartbeat,
       EXTRACT(EPOCH FROM (NOW() AT TIME ZONE 'UTC' - lastheartbeat))::bigint AS secondsago
FROM userreadiness
WHERE username = @username AND role = 'Analyst'
"@ -Parameters @{ '@username' = $Username }

    Write-Host "Updated Status:" -ForegroundColor Cyan
    $after | Format-Table username, role, isready, lastheartbeat, secondsago -AutoSize

    if ($after[0].isready -and [int64]$after[0].secondsago -lt 120) {
        Write-Host "  SUCCESS: User is now READY for assignments!" -ForegroundColor Green
        Write-Host "    Heartbeat will need to be kept fresh by the page (every 30s)." -ForegroundColor Yellow
        Write-Host "    If heartbeat doesn't update, check browser console for API errors." -ForegroundColor Yellow
    }
}
catch {
    Write-Host ("  ERROR: {0}" -f $_.Exception.Message) -ForegroundColor Red
    Write-Host "    If this is an RLS / permission error, re-run with -UseSuperuser." -ForegroundColor Yellow
    throw
}
finally {
    Close-NscimConnection -Handle $h
}

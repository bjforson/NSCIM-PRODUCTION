# Clear expired analysis assignments (set State = 'Expired')
# Usage: .\scripts\Run-Clear-Expired-Assignments.ps1

$ErrorActionPreference = "Stop"
$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$projectRoot = Split-Path -Parent $scriptDir

$appsettingsPath = Join-Path $projectRoot "src\NickScanCentralImagingPortal.API\appsettings.json"
$appsettings = Get-Content $appsettingsPath -Raw | ConvertFrom-Json
$connString = $appsettings.ConnectionStrings.NS_CIS_Connection
if ($connString -match 'Server=([^;]+)') { $ServerInstance = $Matches[1].Trim() } else { $ServerInstance = '127.0.0.1,1433' }
if ($connString -match 'Database=([^;]+)') { $Database = $Matches[1].Trim() } else { $Database = 'NS_CIS' }

Write-Host "Clearing expired assignments..." -ForegroundColor Cyan

$countQuery = "SELECT COUNT(*) AS Cnt FROM AnalysisAssignments WHERE State = 'Active' AND LeaseUntilUtc IS NOT NULL AND LeaseUntilUtc <= GETUTCDATE()"
$before = Invoke-Sqlcmd -ServerInstance $ServerInstance -Database $Database -Query $countQuery
$beforeCount = $before.Cnt

if ($beforeCount -eq 0) {
    Write-Host "No expired assignments to clear." -ForegroundColor Green
    exit 0
}

Write-Host "Found $beforeCount expired assignment(s). Marking as Expired..." -ForegroundColor Yellow

$updateQuery = @"
UPDATE AnalysisAssignments
SET State = 'Expired', UpdatedAtUtc = GETUTCDATE()
WHERE State = 'Active'
    AND LeaseUntilUtc IS NOT NULL
    AND LeaseUntilUtc <= GETUTCDATE()
"@

Invoke-Sqlcmd -ServerInstance $ServerInstance -Database $Database -Query $updateQuery

Write-Host "Done. Cleared $beforeCount expired assignment(s)." -ForegroundColor Green

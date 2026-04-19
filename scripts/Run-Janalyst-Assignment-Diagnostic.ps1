# Run Janalyst assignment discrepancy diagnostic
# Usage: .\scripts\Run-Janalyst-Assignment-Diagnostic.ps1

$ErrorActionPreference = "Stop"
$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$projectRoot = Split-Path -Parent $scriptDir

# Get connection settings from appsettings
$appsettingsPath = Join-Path $projectRoot "src\NickScanCentralImagingPortal.API\appsettings.json"
if (-not (Test-Path $appsettingsPath)) {
    Write-Error "appsettings.json not found at $appsettingsPath"
}
$appsettings = Get-Content $appsettingsPath -Raw | ConvertFrom-Json
$connString = $appsettings.ConnectionStrings.NS_CIS_Connection
if (-not $connString) {
    Write-Error "NS_CIS_Connection not found in appsettings"
}
# Parse for SqlServer module (Invoke-Sqlcmd uses -ServerInstance, -Database, not -ConnectionString)
if ($connString -match 'Server=([^;]+)') { $ServerInstance = $Matches[1].Trim() } else { $ServerInstance = '127.0.0.1,1433' }
if ($connString -match 'Database=([^;]+)') { $Database = $Matches[1].Trim() } else { $Database = 'NS_CIS' }

$sqlPath = Join-Path $scriptDir "Diagnose-Janalyst-Assignment-Discrepancy.sql"
$outputPath = Join-Path $projectRoot "janalyst_diagnostic_result.txt"

Write-Host "Running Janalyst assignment diagnostic..." -ForegroundColor Cyan
Write-Host "Output will be saved to: $outputPath" -ForegroundColor Gray
Write-Host ""

$output = @()
try {
    # Query 1: Count by status
    $q1 = @"
SELECT g.Status, COUNT(*) AS AssignmentCount,
    CASE WHEN g.Status IN ('AnalystCompleted','AuditCompleted','Completed') THEN 'FILTERED OUT' ELSE 'SHOWN' END AS Visibility
FROM AnalysisAssignments a
JOIN AnalysisGroups g ON a.GroupId = g.Id
WHERE a.AssignedTo = 'Janalyst' AND a.State = 'Active'
    AND (a.LeaseUntilUtc IS NULL OR a.LeaseUntilUtc > GETUTCDATE())
GROUP BY g.Status
ORDER BY Visibility, g.Status
"@
    Write-Host "=== 1. Assignments by group status ===" -ForegroundColor Yellow
    $r1 = Invoke-Sqlcmd -ServerInstance $ServerInstance -Database $Database -Query $q1
    $r1 | Format-Table -AutoSize
    $output += "=== 1. Assignments by group status ===`n" + ($r1 | Format-Table -AutoSize | Out-String)

    # Query 2: Summary
    $q2 = @"
SELECT 
    SUM(CASE WHEN g.Status NOT IN ('AnalystCompleted','AuditCompleted','Completed') THEN 1 ELSE 0 END) AS ShownInUI,
    SUM(CASE WHEN g.Status IN ('AnalystCompleted','AuditCompleted','Completed') THEN 1 ELSE 0 END) AS FilteredOut,
    COUNT(*) AS TotalActive
FROM AnalysisAssignments a JOIN AnalysisGroups g ON a.GroupId = g.Id
WHERE a.AssignedTo = 'Janalyst' AND a.State = 'Active'
    AND (a.LeaseUntilUtc IS NULL OR a.LeaseUntilUtc > GETUTCDATE())
"@
    Write-Host "`n=== 2. Summary ===" -ForegroundColor Yellow
    $r2 = Invoke-Sqlcmd -ServerInstance $ServerInstance -Database $Database -Query $q2
    $r2 | Format-Table -AutoSize
    $output += "`n=== 2. Summary ===`n" + ($r2 | Format-Table -AutoSize | Out-String)

    # Query 3: Expired leases
    $q3 = "SELECT COUNT(*) AS ExpiredLeaseCount FROM AnalysisAssignments WHERE AssignedTo = 'Janalyst' AND State = 'Active' AND LeaseUntilUtc IS NOT NULL AND LeaseUntilUtc <= GETUTCDATE()"
    Write-Host "`n=== 3. Expired leases (also filtered by API) ===" -ForegroundColor Yellow
    $r3 = Invoke-Sqlcmd -ServerInstance $ServerInstance -Database $Database -Query $q3
    $r3 | Format-Table -AutoSize
    $output += "`n=== 3. Expired leases ===`n" + ($r3 | Format-Table -AutoSize | Out-String)

    $output | Set-Content $outputPath -Encoding UTF8
    Write-Host "`nResults saved to $outputPath" -ForegroundColor Green
} catch {
    Write-Host "Error: $_" -ForegroundColor Red
    throw
}

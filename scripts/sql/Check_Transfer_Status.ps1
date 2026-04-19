# Quick status check for data transfer

$sourceInstance = "localhost\NS_CIS"
$targetInstance = "(local)"
$db = "NS_CIS"

Write-Host "Checking transfer status..." -ForegroundColor Cyan
Write-Host ""

# Sample tables to check
$sampleTables = @("Users", "Roles", "SystemSettings", "Permissions", "AnalysisAssignments", "EndpointUsageLog", "AseScans")

Write-Host "Table Comparison:" -ForegroundColor Yellow
Write-Host ("{0,-30} {1,15} {2,15}" -f "Table", "Source", "Target")
Write-Host ("-" * 60)

foreach ($table in $sampleTables) {
    $sourceQuery = "SELECT COUNT(*) FROM [$table]"
    $targetQuery = "SELECT COUNT(*) FROM [$table]"
    
    $sourceCount = (sqlcmd -S $sourceInstance -E -d $db -Q $sourceQuery -W -h -1 2>&1 | Where-Object { $_ -match '^\s*\d+\s*$' } | ForEach-Object { $_.Trim() } | Select-Object -First 1)
    $targetCount = (sqlcmd -S $targetInstance -E -d $db -Q $targetQuery -W -h -1 2>&1 | Where-Object { $_ -match '^\s*\d+\s*$' } | ForEach-Object { $_.Trim() } | Select-Object -First 1)
    
    if ([string]::IsNullOrWhiteSpace($sourceCount)) { $sourceCount = "Error" }
    if ([string]::IsNullOrWhiteSpace($targetCount)) { $targetCount = "0" }
    
    $status = if ($sourceCount -eq $targetCount -and $sourceCount -ne "0" -and $sourceCount -ne "Error") { "✓" } elseif ($targetCount -ne "0") { "→" } else { " " }
    
    Write-Host ("{0,-30} {1,15} {2,15} {3}" -f $table, $sourceCount, $targetCount, $status)
}

Write-Host ""
Write-Host "Legend: ✓ = Match, → = Partial, (blank) = Not started" -ForegroundColor Gray


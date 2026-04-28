# Transfer all tables using the simple transfer script
param(
    [string]$Database = "NS_CIS",
    [string]$SourceInstance = "localhost\NS_CIS",
    [string]$TargetInstance = "(local)"
)

# Continues past errors intentionally: loops through every table in a DB calling Transfer_Table_Simple.ps1; per-table errors are tallied in $errorCount, must not abort the rest.
$ErrorActionPreference = "Continue"

Write-Host "========================================" -ForegroundColor Cyan
Write-Host "Transferring All Tables: $Database" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""

# Get list of tables
$tablesFile = [System.IO.Path]::GetTempFileName()
$query = "SELECT TABLE_SCHEMA, TABLE_NAME FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_TYPE = 'BASE TABLE' AND TABLE_NAME != '__EFMigrationsHistory' ORDER BY TABLE_SCHEMA, TABLE_NAME"
sqlcmd -S $SourceInstance -E -d $Database -Q $query -W -h -1 -o $tablesFile

$tables = Get-Content $tablesFile | Where-Object { $_ -match '\S' -and $_ -notmatch 'rows affected' -and $_ -notmatch '^---' }
$tableCount = ($tables | Measure-Object -Line).Lines
Remove-Item $tablesFile -Force

Write-Host "Found $tableCount tables to transfer" -ForegroundColor Yellow
Write-Host ""

$successCount = 0
$errorCount = 0
$currentTable = 0

foreach ($tableLine in $tables) {
    $parts = $tableLine -split '\s+', 2
    if ($parts.Length -ge 2) {
        $schema = $parts[0]
        $table = $parts[1]
        $currentTable++
        
        Write-Host "[$currentTable/$tableCount] " -NoNewline -ForegroundColor Gray
        
        $result = powershell -NoProfile -ExecutionPolicy Bypass -File "scripts\sql\Transfer_Table_Simple.ps1" -TableName $table -Schema $schema -Database $Database -TargetInstance $TargetInstance -SourceInstance $SourceInstance 2>&1
        
        if ($LASTEXITCODE -eq 0) {
            $successCount++
        } else {
            $errorCount++
            Write-Host "  Error transferring $schema.$table" -ForegroundColor Red
        }
        
        Write-Host ""
    }
}

Write-Host "========================================" -ForegroundColor Cyan
Write-Host "Transfer Complete!" -ForegroundColor Green
Write-Host "Successful: $successCount" -ForegroundColor $(if ($errorCount -eq 0) { "Green" } else { "Yellow" })
Write-Host "Errors: $errorCount" -ForegroundColor $(if ($errorCount -eq 0) { "Green" } else { "Red" })
Write-Host "========================================" -ForegroundColor Cyan


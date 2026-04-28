# Copy schemas for all missing tables
# Creates table structures on target for tables that don't exist

param(
    [string]$SourceInstance = "localhost\NS_CIS",
    [string]$TargetInstance = "(local)",
    [string[]]$Databases = @("NS_CIS", "ICUMS", "ICUMS_Downloads")
)

# Continues past errors intentionally: loops across DBs/tables creating missing schemas on target; per-table create errors are tallied and reported, must not abort the run.
$ErrorActionPreference = "Continue"

Write-Host "========================================" -ForegroundColor Cyan
Write-Host "Copying Missing Table Schemas" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host "Source: $SourceInstance" -ForegroundColor Yellow
Write-Host "Target: $TargetInstance" -ForegroundColor Yellow
Write-Host ""

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$totalMissing = 0
$totalCreated = 0
$totalErrors = 0

foreach ($db in $Databases) {
    Write-Host "Processing: $db" -ForegroundColor Cyan
    Write-Host ("-" * 60) -ForegroundColor Gray
    
    # Get list of tables from source
    $tablesQuery = "SELECT TABLE_SCHEMA, TABLE_NAME FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_TYPE = 'BASE TABLE' AND TABLE_NAME != '__EFMigrationsHistory' ORDER BY TABLE_SCHEMA, TABLE_NAME"
    $tablesFile = [System.IO.Path]::GetTempFileName()
    sqlcmd -S $SourceInstance -E -d $db -Q $tablesQuery -W -h -1 -o $tablesFile 2>&1 | Out-Null
    
    if ($LASTEXITCODE -ne 0) {
        Write-Host "  Error: Could not connect to source database $db" -ForegroundColor Red
        Remove-Item $tablesFile -Force -ErrorAction SilentlyContinue
        continue
    }
    
    $tables = Get-Content $tablesFile | Where-Object { $_ -match '\S' -and $_ -notmatch 'rows affected' -and $_ -notmatch '^---' }
    Remove-Item $tablesFile -Force
    
    if (-not $tables -or $tables.Count -eq 0) {
        Write-Host "  No tables found" -ForegroundColor Yellow
        Write-Host ""
        continue
    }
    
    $dbMissing = 0
    $dbCreated = 0
    $dbErrors = 0
    
    foreach ($tableLine in $tables) {
        $parts = $tableLine -split '\s+', 2
        if ($parts.Length -ge 2) {
            $schema = $parts[0].Trim()
            $table = $parts[1].Trim()
            
            # Check if table exists on target
            $checkQuery = "SELECT COUNT(*) FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_SCHEMA = '$schema' AND TABLE_NAME = '$table'"
            $checkFile = [System.IO.Path]::GetTempFileName()
            sqlcmd -S $TargetInstance -E -d $db -Q $checkQuery -W -h -1 -o $checkFile 2>&1 | Out-Null
            
            $exists = $false
            if ($LASTEXITCODE -eq 0) {
                $result = Get-Content $checkFile | Where-Object { $_ -match '^\s*[1-9]' }
                $exists = ($result -ne $null)
            }
            Remove-Item $checkFile -Force -ErrorAction SilentlyContinue
            
            if (-not $exists) {
                $dbMissing++
                $totalMissing++
                
                Write-Host "  [$db].[$schema].[$table]..." -NoNewline -ForegroundColor Gray
                
                $result = & "$scriptDir\Copy_Table_Schema_Final.ps1" `
                    -TableName $table `
                    -Schema $schema `
                    -Database $db `
                    -TargetInstance $TargetInstance `
                    -SourceInstance $SourceInstance 2>&1
                
                if ($LASTEXITCODE -eq 0) {
                    Write-Host " Created" -ForegroundColor Green
                    $dbCreated++
                    $totalCreated++
                } else {
                    Write-Host " Failed" -ForegroundColor Red
                    $dbErrors++
                    $totalErrors++
                    Write-Host "    $result" -ForegroundColor Red
                }
            }
        }
    }
    
    Write-Host ""
    Write-Host "  Summary for $db :" -ForegroundColor Yellow
    Write-Host "    Missing:  $dbMissing" -ForegroundColor $(if ($dbMissing -eq 0) { "Green" } else { "Yellow" })
    Write-Host "    Created:  $dbCreated" -ForegroundColor Green
    Write-Host "    Errors:   $dbErrors" -ForegroundColor $(if ($dbErrors -eq 0) { "Green" } else { "Red" })
    Write-Host ""
}

Write-Host "========================================" -ForegroundColor Cyan
Write-Host "Overall Summary" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host "Total Missing Tables: $totalMissing" -ForegroundColor White
Write-Host "Successfully Created: $totalCreated" -ForegroundColor Green
Write-Host "Errors:              $totalErrors" -ForegroundColor $(if ($totalErrors -eq 0) { "Green" } else { "Red" })
Write-Host ""

if ($totalErrors -eq 0 -and $totalCreated -gt 0) {
    Write-Host "All missing table schemas have been created!" -ForegroundColor Green
    Write-Host "You can now run the data transfer script." -ForegroundColor Yellow
}


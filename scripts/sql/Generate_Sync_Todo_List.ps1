# Generate Todo List from Sync Status Check Results
# This script creates a todo list for completing the database sync

param(
    [string]$StatusCsvFile = "sync_status_report.csv",
    [string]$TodoListFile = "sync_todo_list.txt",
    [string]$TodoScriptFile = "sync_transfer_remaining_tables.ps1"
)

if (-not (Test-Path $StatusCsvFile)) {
    Write-Host "Error: Status CSV file not found: $StatusCsvFile" -ForegroundColor Red
    Write-Host "Please run Check_Database_Sync_Status.ps1 first to generate the status report." -ForegroundColor Yellow
    exit 1
}

Write-Host "Loading status data from: $StatusCsvFile" -ForegroundColor Cyan

$statusData = Import-Csv -Path $StatusCsvFile

# Filter tables that need transfer
$needsTransfer = $statusData | Where-Object { 
    $_.Status -in @("Missing", "Incomplete", "Empty") 
} | Sort-Object Database, Schema, Table

$complete = $statusData | Where-Object { $_.Status -eq "Complete" }
$errors = $statusData | Where-Object { $_.Status -match "Error" }

Write-Host ""
Write-Host "========================================" -ForegroundColor Cyan
Write-Host "Generating Todo List" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""

Write-Host "Tables requiring transfer: $($needsTransfer.Count)" -ForegroundColor Yellow
Write-Host "Tables already complete: $($complete.Count)" -ForegroundColor Green
Write-Host "Tables with errors: $($errors.Count)" -ForegroundColor $(if ($errors.Count -eq 0) { "Green" } else { "Red" })
Write-Host ""

if ($needsTransfer.Count -eq 0) {
    Write-Host "✅ All tables are synced! No action needed." -ForegroundColor Green
    
    $todoContent = @"
========================================
Database Sync Todo List
Generated: $(Get-Date -Format "yyyy-MM-dd HH:mm:ss")
========================================

✅ ALL TABLES ARE COMPLETE - NO ACTION REQUIRED

Total Tables Checked: $($statusData.Count)
Complete: $($complete.Count)
Errors: $($errors.Count)
"@
    
    $todoContent | Out-File -FilePath $TodoListFile -Encoding UTF8
    Write-Host "Todo list saved to: $TodoListFile" -ForegroundColor Cyan
    exit 0
}

# Group by database
$byDatabase = $needsTransfer | Group-Object Database

# Generate todo list text file
$todoContent = @"
========================================
Database Sync Todo List
Generated: $(Get-Date -Format "yyyy-MM-dd HH:mm:ss")
========================================

SUMMARY
========================================
Total Tables Requiring Transfer: $($needsTransfer.Count)
Total Tables Complete: $($complete.Count)
Total Tables with Errors: $($errors.Count)

TABLES REQUIRING TRANSFER
========================================

"@

foreach ($dbGroup in $byDatabase) {
    $db = $dbGroup.Name
    $dbTables = $dbGroup.Group
    
    $todoContent += "`nDATABASE: $db ($($dbTables.Count) tables)`n"
    $todoContent += ("=" * 60) + "`n"
    
    # Group by status
    $byStatus = $dbTables | Group-Object Status
    foreach ($statusGroup in $byStatus) {
        $status = $statusGroup.Name
        $statusTables = $statusGroup.Group
        
        $todoContent += "`n$status ($($statusTables.Count) tables):`n"
        $todoContent += ("-" * 60) + "`n"
        
        foreach ($table in $statusTables) {
            $sourceCount = if ($table.SourceCount) { $table.SourceCount } else { "N/A" }
            $targetCount = if ($table.TargetCount) { $table.TargetCount } else { "N/A" }
            $todoContent += "  - [$($table.Schema)].[$($table.Table)] (Source: $sourceCount, Target: $targetCount)`n"
        }
    }
    
    $todoContent += "`n"
}

# Add error tables if any
if ($errors.Count -gt 0) {
    $todoContent += "`nTABLES WITH ERRORS (Require Investigation)`n"
    $todoContent += ("=" * 60) + "`n"
    foreach ($errorTable in $errors) {
        $todoContent += "  - [$($errorTable.Database)].[$($errorTable.Schema)].[$($errorTable.Table)]`n"
        if ($errorTable.Error) {
            $todoContent += "    Error: $($errorTable.Error)`n"
        }
    }
    $todoContent += "`n"
}

# Generate PowerShell script to transfer remaining tables
$scriptContent = @"
# Transfer Remaining Tables Script
# Generated: $(Get-Date -Format "yyyy-MM-dd HH:mm:ss")
# This script transfers all tables that are missing, incomplete, or empty
# Based on sync status check results

param(
    [string]`$SourceInstance = "localhost\NS_CIS",
    [string]`$TargetInstance = "(local)"
)

# Continues past errors intentionally: generated runner transfers many tables one-by-one; per-table errors are tallied, must not abort the rest.
`$ErrorActionPreference = "Continue"

Write-Host "========================================" -ForegroundColor Cyan
Write-Host "Transferring Remaining Tables" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host "Based on sync status check" -ForegroundColor Gray
Write-Host ""

`$totalTables = $($needsTransfer.Count)
`$currentTable = 0
`$successCount = 0
`$errorCount = 0

"@

# Add transfer commands for each table
foreach ($table in $needsTransfer) {
    $scriptContent += @"

# Transfer: [$($table.Database)].[$($table.Schema)].[$($table.Table)]
# Status: $($table.Status) | Source: $($table.SourceCount) rows | Target: $($table.TargetCount) rows
`$currentTable++
Write-Host "[`$currentTable/`$totalTables] Transferring [$($table.Database)].[$($table.Schema)].[$($table.Table)]..." -ForegroundColor Cyan

`$result = & "`$scriptDir\Transfer_Table_Simple.ps1" -TableName "$($table.Table)" -Schema "$($table.Schema)" -Database "$($table.Database)" -TargetInstance `$TargetInstance -SourceInstance `$SourceInstance 2>&1

if (`$LASTEXITCODE -eq 0) {
    Write-Host "  [OK] Success" -ForegroundColor Green
    `$successCount++
} else {
    Write-Host "  [X] Failed" -ForegroundColor Red
    `$errorCount++
    Write-Host `$result -ForegroundColor Red
}

Write-Host ""
"@
}

$scriptContent += @"

Write-Host "========================================" -ForegroundColor Cyan
Write-Host "Transfer Complete!" -ForegroundColor Green
Write-Host "========================================" -ForegroundColor Cyan
Write-Host "Successful: `$successCount" -ForegroundColor $(if ($needsTransfer.Count -eq 0) { "Green" } else { "Yellow" })
Write-Host "Errors: `$errorCount" -ForegroundColor $(if ($needsTransfer.Count -eq 0) { "Green" } else { "Red" })
Write-Host ""

if (`$errorCount -gt 0) {
    Write-Host "Some tables failed to transfer. Please review the errors above." -ForegroundColor Yellow
    Write-Host "You can re-run this script to retry failed transfers." -ForegroundColor Yellow
}
"@

# Save files
$todoContent | Out-File -FilePath $TodoListFile -Encoding UTF8
$scriptContent | Out-File -FilePath $TodoScriptFile -Encoding UTF8

Write-Host "Todo list saved to: $TodoListFile" -ForegroundColor Cyan
Write-Host "Transfer script saved to: $TodoScriptFile" -ForegroundColor Cyan
Write-Host ""
Write-Host "Next steps:" -ForegroundColor Yellow
Write-Host "1. Review the todo list: $TodoListFile" -ForegroundColor White
Write-Host "2. Run the transfer script: .\$TodoScriptFile" -ForegroundColor White
Write-Host "3. Re-run the status check to verify completion" -ForegroundColor White
Write-Host ""


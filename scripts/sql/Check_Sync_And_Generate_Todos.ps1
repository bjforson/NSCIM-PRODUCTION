# Master Script: Check Database Sync Status and Generate Todo List
# This script runs the status check and automatically generates the todo list

param(
    [string]$SourceInstance = "localhost\NS_CIS",
    [string]$TargetInstance = "(local)",
    [string[]]$Databases = @("NS_CIS", "ICUMS", "ICUMS_Downloads")
)

# Continues past errors intentionally: orchestrates Check_Database_Sync_Status + Generate_Sync_Todo_List, both of which iterate per-table; explicit $LASTEXITCODE checks enforce the real fail-fast.
$ErrorActionPreference = "Continue"

Write-Host "========================================" -ForegroundColor Cyan
Write-Host "Database Sync Status Check & Todo Generation" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""

# Step 1: Run status check
Write-Host "Step 1: Running sync status check..." -ForegroundColor Yellow
Write-Host ""

$statusFile = "sync_status_report.txt"
$csvFile = "sync_status_report.csv"

$statusResults = & ".\Check_Database_Sync_Status.ps1" `
    -SourceInstance $SourceInstance `
    -TargetInstance $TargetInstance `
    -Databases $Databases `
    -OutputFile $statusFile

if ($LASTEXITCODE -ne 0 -or -not $statusResults) {
    Write-Host "Error: Status check failed. Please review the output above." -ForegroundColor Red
    exit 1
}

Write-Host ""
Write-Host "Step 2: Generating todo list..." -ForegroundColor Yellow
Write-Host ""

# Step 2: Generate todo list
& ".\Generate_Sync_Todo_List.ps1" `
    -StatusCsvFile $csvFile `
    -TodoListFile "sync_todo_list.txt" `
    -TodoScriptFile "sync_transfer_remaining_tables.ps1"

if ($LASTEXITCODE -ne 0) {
    Write-Host "Error: Todo list generation failed. Please review the output above." -ForegroundColor Red
    exit 1
}

Write-Host ""
Write-Host "========================================" -ForegroundColor Cyan
Write-Host "Complete!" -ForegroundColor Green
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""
Write-Host "Files generated:" -ForegroundColor Yellow
Write-Host "  - $statusFile (Detailed status report)" -ForegroundColor White
Write-Host "  - $csvFile (CSV export for analysis)" -ForegroundColor White
Write-Host "  - sync_todo_list.txt (Human-readable todo list)" -ForegroundColor White
Write-Host "  - sync_transfer_remaining_tables.ps1 (PowerShell script to transfer remaining tables)" -ForegroundColor White
Write-Host ""
Write-Host "Next steps:" -ForegroundColor Yellow
Write-Host "1. Review sync_todo_list.txt to see what needs to be transferred" -ForegroundColor White
Write-Host "2. Review sync_status_report.txt for detailed status information" -ForegroundColor White
Write-Host "3. Run sync_transfer_remaining_tables.ps1 to transfer remaining tables" -ForegroundColor White
Write-Host ""


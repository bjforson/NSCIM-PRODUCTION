# Run SQL Index Creation Scripts
# This script executes the SQL scripts to create missing date indexes

param(
    [string]$Server = "127.0.0.1,1433",
    [string]$Database = "NS_CIS"
)

# Continues past errors intentionally: applies multiple independent .sql index files; each is wrapped in try/catch and reported separately so a failure in one file doesn't block the others.
$ErrorActionPreference = "Continue"

Write-Host "Creating Date Indexes for Memory Optimization" -ForegroundColor Cyan
Write-Host "=============================================" -ForegroundColor Cyan
Write-Host ""

# Get script directory
$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$rootDir = Split-Path -Parent $scriptDir

# 1. Run ContainerScanQueues index script
Write-Host "1. Creating indexes for ContainerScanQueues table..." -ForegroundColor Yellow
$containerScanQueueScript = Join-Path $rootDir "scripts\Add_ContainerScanQueue_Date_Indexes.sql"

if (Test-Path $containerScanQueueScript) {
    try {
        Invoke-Sqlcmd -ServerInstance $Server -Database $Database -InputFile $containerScanQueueScript
        Write-Host "   ✅ ContainerScanQueues indexes created successfully" -ForegroundColor Green
    } catch {
        Write-Host "   ❌ Error creating ContainerScanQueues indexes: $($_.Exception.Message)" -ForegroundColor Red
    }
} else {
    Write-Host "   ⚠️ Script file not found: $containerScanQueueScript" -ForegroundColor Yellow
}

Write-Host ""

# 2. Run all tables index script
Write-Host "2. Creating indexes for all other tables..." -ForegroundColor Yellow
$allTablesScript = Join-Path $rootDir "scripts\Add_Date_Indexes_All_Tables.sql"

if (Test-Path $allTablesScript) {
    try {
        Invoke-Sqlcmd -ServerInstance $Server -Database $Database -InputFile $allTablesScript
        Write-Host "   ✅ All tables indexes created successfully" -ForegroundColor Green
    } catch {
        Write-Host "   ❌ Error creating all tables indexes: $($_.Exception.Message)" -ForegroundColor Red
    }
} else {
    Write-Host "   ⚠️ Script file not found: $allTablesScript" -ForegroundColor Yellow
}

Write-Host ""
Write-Host "✅ Index creation complete!" -ForegroundColor Green
Write-Host ""
Write-Host "Next Steps:" -ForegroundColor Cyan
Write-Host "1. Monitor SQL Server buffer pool usage" -ForegroundColor White
Write-Host "2. Test queries with: .\scripts\Test-ContainerScanQueue.ps1 -Detailed" -ForegroundColor White
Write-Host "3. Verify performance improvements" -ForegroundColor White


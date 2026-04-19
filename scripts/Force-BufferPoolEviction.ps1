# Force SQL Server Buffer Pool Eviction
# This script helps verify if the code fix is working by clearing SQL Server's buffer pool cache
# WARNING: This will clear ALL cached data from memory - use with caution in production!

param(
    [string]$ServerInstance = "127.0.0.1,1433",
    [string]$Database = "NS_CIS"
)

Write-Host "========================================" -ForegroundColor Cyan
Write-Host "SQL Server Buffer Pool Eviction" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""
Write-Host "WARNING: This will clear ALL cached data from SQL Server's buffer pool!" -ForegroundColor Yellow
Write-Host "This may cause temporary performance degradation as data is reloaded." -ForegroundColor Yellow
Write-Host ""

$confirm = Read-Host "Are you sure you want to proceed? (yes/no)"
if ($confirm -ne "yes") {
    Write-Host "Operation cancelled." -ForegroundColor Yellow
    exit 0
}

Write-Host ""
Write-Host "1. Checking current buffer pool usage..." -ForegroundColor Yellow
Write-Host "-----------------------------------" -ForegroundColor Yellow

$beforeQuery = @"
SELECT 
    OBJECT_SCHEMA_NAME(p.object_id) AS SchemaName,
    OBJECT_NAME(p.object_id) AS TableName,
    COUNT(*) * 8 / 1024 AS BufferPoolMB
FROM sys.dm_os_buffer_descriptors bd
INNER JOIN sys.allocation_units au ON bd.allocation_unit_id = au.allocation_unit_id
INNER JOIN sys.partitions p ON au.container_id = p.partition_id
WHERE bd.database_id = DB_ID('$Database')
    AND p.object_id > 100
    AND OBJECT_NAME(p.object_id) = 'AseScans'
GROUP BY p.object_id;
"@

try {
    $before = Invoke-Sqlcmd -ServerInstance $ServerInstance -Database $Database -Query $beforeQuery
    $beforeMB = if ($before) { $before.BufferPoolMB } else { 0 }
    Write-Host "  AseScans buffer pool before: $beforeMB MB ($([math]::Round($beforeMB / 1024, 2)) GB)" -ForegroundColor White
} catch {
    Write-Host "  Could not check buffer pool: $($_.Exception.Message)" -ForegroundColor Red
}

Write-Host ""
Write-Host "2. Clearing buffer pool cache..." -ForegroundColor Yellow
Write-Host "-------------------------------" -ForegroundColor Yellow

# Clear the specific database's buffer pool
$clearQuery = @"
DBCC DROPCLEANBUFFERS;
"@

try {
    Invoke-Sqlcmd -ServerInstance $ServerInstance -Database $Database -Query $clearQuery
    Write-Host "  Buffer pool cache cleared successfully" -ForegroundColor Green
} catch {
    Write-Host "  Error clearing buffer pool: $($_.Exception.Message)" -ForegroundColor Red
    Write-Host "  Note: You may need to run this as a user with sysadmin privileges" -ForegroundColor Yellow
}

Write-Host ""
Write-Host "3. Checking buffer pool usage after eviction..." -ForegroundColor Yellow
Write-Host "---------------------------------------------" -ForegroundColor Yellow

Start-Sleep -Seconds 2

try {
    $after = Invoke-Sqlcmd -ServerInstance $ServerInstance -Database $Database -Query $beforeQuery
    $afterMB = if ($after) { $after.BufferPoolMB } else { 0 }
    Write-Host "  AseScans buffer pool after: $afterMB MB ($([math]::Round($afterMB / 1024, 2)) GB)" -ForegroundColor White
    
    if ($beforeMB -gt 0) {
        $reduction = $beforeMB - $afterMB
        $reductionPercent = ($reduction / $beforeMB) * 100
        Write-Host "  Reduction: $reduction MB ($([math]::Round($reduction / 1024, 2)) GB) - $([math]::Round($reductionPercent, 2))%" -ForegroundColor $(if ($reductionPercent -gt 50) { "Green" } else { "Yellow" })
    }
} catch {
    Write-Host "  Could not check buffer pool: $($_.Exception.Message)" -ForegroundColor Red
}

Write-Host ""
Write-Host "4. Next Steps:" -ForegroundColor Yellow
Write-Host "--------------" -ForegroundColor Yellow
Write-Host "  - The buffer pool has been cleared" -ForegroundColor White
Write-Host "  - As the API makes new queries, only the last 30 days of AseScans will be loaded" -ForegroundColor White
Write-Host "  - Monitor memory usage over the next few hours to see the reduction" -ForegroundColor White
Write-Host "  - Run Check-SqlServerMemoryUsage.ps1 again to verify the fix is working" -ForegroundColor White
Write-Host ""
Write-Host "========================================" -ForegroundColor Cyan
Write-Host "Eviction Complete" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan


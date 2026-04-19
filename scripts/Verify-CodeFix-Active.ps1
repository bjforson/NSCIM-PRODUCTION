# Verify Code Fix is Active
# Checks if the date filter is actually being used in queries

param(
    [string]$ServerInstance = "127.0.0.1,1433",
    [string]$Database = "NS_CIS"
)

Write-Host "========================================" -ForegroundColor Cyan
Write-Host "Code Fix Verification" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""

Write-Host "1. Checking AseScans table statistics..." -ForegroundColor Yellow
Write-Host "----------------------------------------" -ForegroundColor Yellow

$statsQuery = @"
SELECT 
    COUNT(*) AS TotalRecords,
    COUNT(CASE WHEN ScanTime >= DATEADD(DAY, -30, GETUTCDATE()) THEN 1 END) AS Last30Days,
    COUNT(CASE WHEN ScanTime >= DATEADD(DAY, -7, GETUTCDATE()) THEN 1 END) AS Last7Days,
    MIN(ScanTime) AS OldestScan,
    MAX(ScanTime) AS NewestScan
FROM dbo.AseScans;
"@

try {
    $stats = Invoke-Sqlcmd -ServerInstance $ServerInstance -Database $Database -Query $statsQuery
    Write-Host "  Total AseScans records: $($stats.TotalRecords)" -ForegroundColor White
    Write-Host "  Records in last 30 days: $($stats.Last30Days)" -ForegroundColor White
    Write-Host "  Records in last 7 days: $($stats.Last7Days)" -ForegroundColor White
    Write-Host "  Oldest scan: $($stats.OldestScan)" -ForegroundColor White
    Write-Host "  Newest scan: $($stats.NewestScan)" -ForegroundColor White
    
    $percentRecent = if ($stats.TotalRecords -gt 0) { ($stats.Last30Days / $stats.TotalRecords) * 100 } else { 0 }
    Write-Host ""
    Write-Host "  Expected reduction: $([math]::Round($percentRecent, 2))% of data should be in buffer pool" -ForegroundColor $(if ($percentRecent -lt 20) { "Green" } else { "Yellow" })
} catch {
    Write-Host "  Error: $($_.Exception.Message)" -ForegroundColor Red
}

Write-Host ""
Write-Host "2. Checking for date indexes..." -ForegroundColor Yellow
Write-Host "--------------------------------" -ForegroundColor Yellow

$indexQuery = @"
SELECT 
    i.name AS IndexName,
    i.type_desc AS IndexType,
    COL_NAME(ic.object_id, ic.column_id) AS ColumnName
FROM sys.indexes i
INNER JOIN sys.index_columns ic ON i.object_id = ic.object_id AND i.index_id = ic.index_id
WHERE i.object_id = OBJECT_ID('dbo.AseScans')
    AND COL_NAME(ic.object_id, ic.column_id) IN ('ScanTime', 'CreatedAt')
ORDER BY i.name, ic.key_ordinal;
"@

try {
    $indexes = Invoke-Sqlcmd -ServerInstance $ServerInstance -Database $Database -Query $indexQuery
    if ($indexes) {
        Write-Host "  Date-related indexes found:" -ForegroundColor Green
        foreach ($idx in $indexes) {
            Write-Host "    - $($idx.IndexName) on $($idx.ColumnName) ($($idx.IndexType))" -ForegroundColor White
        }
    } else {
        Write-Host "  WARNING: No date indexes found on ScanTime or CreatedAt!" -ForegroundColor Yellow
        Write-Host "    - Queries may still scan the entire table" -ForegroundColor Yellow
        Write-Host "    - Run Verify-AseScans-Indexes.sql to create indexes" -ForegroundColor Yellow
    }
} catch {
    Write-Host "  Error: $($_.Exception.Message)" -ForegroundColor Red
}

Write-Host ""
Write-Host "3. Checking recent query activity..." -ForegroundColor Yellow
Write-Host "-------------------------------------" -ForegroundColor Yellow

$queryActivityQuery = @"
SELECT TOP 10
    qs.execution_count,
    qs.total_logical_reads / qs.execution_count AS avg_logical_reads,
    qs.last_execution_time,
    SUBSTRING(qt.text, (qs.statement_start_offset/2) + 1,
        ((CASE qs.statement_end_offset
            WHEN -1 THEN DATALENGTH(qt.text)
            ELSE qs.statement_end_offset
        END - qs.statement_start_offset)/2) + 1) AS query_text
FROM sys.dm_exec_query_stats qs
CROSS APPLY sys.dm_exec_sql_text(qs.sql_handle) qt
WHERE qt.text LIKE '%AseScans%'
    AND qt.text NOT LIKE '%sys.dm_exec_query_stats%'
ORDER BY qs.last_execution_time DESC;
"@

try {
    $queries = Invoke-Sqlcmd -ServerInstance $ServerInstance -Database "master" -Query $queryActivityQuery
    if ($queries) {
        Write-Host "  Recent queries involving AseScans:" -ForegroundColor White
        foreach ($q in $queries) {
            $hasDateFilter = $q.query_text -like '*ScanTime*DATEADD*' -or $q.query_text -like '*ScanTime >=*'
            $status = if ($hasDateFilter) { "GOOD (has date filter)" } else { "WARNING (no date filter)" }
            $color = if ($hasDateFilter) { "Green" } else { "Yellow" }
            Write-Host "    - Executions: $($q.execution_count), Avg reads: $($q.avg_logical_reads), Status: $status" -ForegroundColor $color
        }
    } else {
        Write-Host "  No recent query activity found" -ForegroundColor Yellow
    }
} catch {
    Write-Host "  Error: $($_.Exception.Message)" -ForegroundColor Red
    Write-Host "    Note: This query requires VIEW SERVER STATE permission" -ForegroundColor Yellow
}

Write-Host ""
Write-Host "4. Recommendations:" -ForegroundColor Yellow
Write-Host "-------------------" -ForegroundColor Yellow
Write-Host "  - If code fix is deployed, wait 2+ minutes for cache to expire" -ForegroundColor White
Write-Host "  - Check API logs to verify new queries are using date filters" -ForegroundColor White
Write-Host "  - Run Force-BufferPoolEviction.ps1 to clear old cached data" -ForegroundColor White
Write-Host "  - Monitor memory usage over next few hours" -ForegroundColor White
Write-Host ""
Write-Host "========================================" -ForegroundColor Cyan
Write-Host "Verification Complete" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan


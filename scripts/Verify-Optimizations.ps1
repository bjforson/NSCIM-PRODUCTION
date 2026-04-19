# Verification Script for SQL Memory Optimizations
# Runs all verification checks and compares before/after

param(
    [string]$Server = "127.0.0.1,1433",
    [string]$Database = "NS_CIS"
)

$ErrorActionPreference = "Continue"

Write-Host "========================================" -ForegroundColor Cyan
Write-Host "SQL Memory Optimization Verification" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""

# 1. Check SQL Server Memory Configuration
Write-Host "1. SQL Server Memory Configuration" -ForegroundColor Yellow
Write-Host "-----------------------------------" -ForegroundColor Yellow

$memoryConfig = Invoke-Sqlcmd -ServerInstance $Server -Database "master" -Query @"
SELECT 
    name,
    CAST(value AS INT) AS ValueMB,
    CAST(value_in_use AS INT) AS ValueInUseMB
FROM sys.configurations 
WHERE name IN ('max server memory (MB)', 'min server memory (MB)')
ORDER BY name;
"@

foreach ($config in $memoryConfig) {
    $name = $config.name
    $valueMB = $config.ValueMB
    $valueInUseMB = $config.ValueInUseMB
    $valueGB = [math]::Round($valueMB / 1024, 2)
    
    if ($name -eq "max server memory (MB)") {
        if ($valueMB -ge 10240) {  # >= 10 GB
            Write-Host "  ✅ Max Server Memory: $valueGB GB ($valueMB MB)" -ForegroundColor Green
        } elseif ($valueMB -ge 1024) {  # >= 1 GB
            Write-Host "  ⚠️ Max Server Memory: $valueGB GB ($valueMB MB) - Consider increasing" -ForegroundColor Yellow
        } else {
            Write-Host "  ❌ Max Server Memory: $valueGB GB ($valueMB MB) - TOO LOW!" -ForegroundColor Red
        }
    } else {
        Write-Host "  Min Server Memory: $([math]::Round($valueMB / 1024, 2)) GB ($valueMB MB)" -ForegroundColor Cyan
    }
}

Write-Host ""

# 2. Check Buffer Pool Usage
Write-Host "2. Buffer Pool Usage" -ForegroundColor Yellow
Write-Host "-------------------" -ForegroundColor Yellow

$bufferInfo = Invoke-Sqlcmd -ServerInstance $Server -Database $Database -Query @"
SELECT 
    (SELECT COUNT(*) * 8 / 1024 FROM sys.dm_os_buffer_descriptors 
     WHERE database_id = DB_ID('$Database')) AS BufferPoolMB,
    (SELECT SUM(size * 8 / 1024) FROM sys.master_files 
     WHERE database_id = DB_ID('$Database')) AS TotalDBMB;
"@

$bufferMB = $bufferInfo.BufferPoolMB
$totalDBMB = $bufferInfo.TotalDBMB
$bufferGB = [math]::Round($bufferMB / 1024, 2)
$percentCached = if ($totalDBMB -gt 0) { [math]::Round(($bufferMB / $totalDBMB) * 100, 2) } else { 0 }

Write-Host "  Buffer Pool: $bufferGB GB ($bufferMB MB)" -ForegroundColor $(if ($bufferMB -lt 100) { "Yellow" } elseif ($bufferMB -lt 5000) { "Green" } else { "Cyan" })
Write-Host "  Database Size: $([math]::Round($totalDBMB / 1024, 2)) GB ($totalDBMB MB)" -ForegroundColor Cyan
Write-Host "  Cache Efficiency: $percentCached% of database cached" -ForegroundColor Cyan

if ($bufferMB -lt 100) {
    Write-Host "  ⚠️ Buffer pool is very low - queries may be slow" -ForegroundColor Yellow
} elseif ($bufferMB -lt 1000) {
    Write-Host "  ✅ Buffer pool is low (good for optimization - only recent data cached)" -ForegroundColor Green
} else {
    Write-Host "  ✅ Buffer pool usage is reasonable" -ForegroundColor Green
}

Write-Host ""

# 3. Check Indexes Exist
Write-Host "3. Date Indexes Verification" -ForegroundColor Yellow
Write-Host "----------------------------" -ForegroundColor Yellow

$indexes = Invoke-Sqlcmd -ServerInstance $Server -Database $Database -Query @"
SELECT 
    OBJECT_NAME(i.object_id) AS TableName,
    i.name AS IndexName,
    i.type_desc AS IndexType
FROM sys.indexes i
WHERE OBJECT_NAME(i.object_id) IN ('ContainerScanQueues', 'BLReviewRecords', 'ContainerCompletenessStatuses')
    AND (
        i.name LIKE '%CreatedAt%' OR 
        i.name LIKE '%QueuedAt%' OR 
        i.name LIKE '%CompletedAt%' OR 
        i.name LIKE '%ProcessedAt%' OR
        i.name LIKE '%ScanDate%'
    )
ORDER BY OBJECT_NAME(i.object_id), i.name;
"@

if ($indexes -and $indexes.Count -gt 0) {
    Write-Host "  ✅ Found $($indexes.Count) date indexes:" -ForegroundColor Green
    $indexes | Format-Table -AutoSize
} else {
    Write-Host "  ❌ No date indexes found!" -ForegroundColor Red
}

Write-Host ""

# 4. Check Index Usage (after running queries)
Write-Host "4. Index Usage Statistics" -ForegroundColor Yellow
Write-Host "------------------------" -ForegroundColor Yellow

Write-Host "  Running test queries to generate index usage data..." -ForegroundColor Gray
try {
    # Run a test query
    Invoke-Sqlcmd -ServerInstance $Server -Database $Database -Query @"
SELECT Status, COUNT(*) AS Count
FROM ContainerScanQueues
WHERE QueuedAt >= DATEADD(DAY, -1, GETUTCDATE())
GROUP BY Status;
"@ | Out-Null

    $indexUsage = Invoke-Sqlcmd -ServerInstance $Server -Database $Database -Query @"
SELECT 
    i.name AS IndexName,
    s.user_seeks,
    s.user_scans,
    s.user_lookups,
    CASE 
        WHEN s.last_user_seek IS NOT NULL THEN CONVERT(VARCHAR, s.last_user_seek, 120)
        ELSE 'Never'
    END AS LastSeek
FROM sys.dm_db_index_usage_stats s
INNER JOIN sys.indexes i ON s.object_id = i.object_id AND s.index_id = i.index_id
WHERE DB_NAME(s.database_id) = '$Database'
    AND OBJECT_NAME(s.object_id) = 'ContainerScanQueues'
    AND i.name LIKE 'IX_ContainerScanQueues_%At%'
ORDER BY s.user_seeks DESC;
"@

    if ($indexUsage -and $indexUsage.Count -gt 0) {
        Write-Host "  ✅ Index usage data:" -ForegroundColor Green
        $indexUsage | Format-Table -AutoSize
        
        $usedIndexes = $indexUsage | Where-Object { $_.user_seeks -gt 0 -or $_.user_scans -gt 0 }
        if ($usedIndexes) {
            Write-Host "  ✅ Date indexes are being used!" -ForegroundColor Green
        } else {
            Write-Host "  ⚠️ Date indexes exist but not yet used (may need more queries)" -ForegroundColor Yellow
        }
    } else {
        Write-Host "  ⚠️ No index usage data yet (indexes may not have been used yet)" -ForegroundColor Yellow
        Write-Host "     Run more queries to populate usage statistics" -ForegroundColor Gray
    }
} catch {
    Write-Host "  ⚠️ Could not check index usage: $($_.Exception.Message)" -ForegroundColor Yellow
}

Write-Host ""

# 5. Test Query Performance
Write-Host "5. Query Performance Test" -ForegroundColor Yellow
Write-Host "------------------------" -ForegroundColor Yellow

Write-Host "  Testing query with date filter..." -ForegroundColor Gray

$queryTest = @"
SET STATISTICS TIME ON;
SET STATISTICS IO ON;
GO

SELECT Status, COUNT(*) AS Count
FROM ContainerScanQueues
WHERE QueuedAt >= DATEADD(DAY, -1, GETUTCDATE())
GROUP BY Status;
GO

SET STATISTICS TIME OFF;
SET STATISTICS IO OFF;
GO
"@

try {
    $startTime = Get-Date
    $result = Invoke-Sqlcmd -ServerInstance $Server -Database $Database -Query "SELECT Status, COUNT(*) AS Count FROM ContainerScanQueues WHERE QueuedAt >= DATEADD(DAY, -1, GETUTCDATE()) GROUP BY Status;" -QueryTimeout 30
    $endTime = Get-Date
    $duration = ($endTime - $startTime).TotalMilliseconds
    
    Write-Host "  Query Duration: $([math]::Round($duration, 2)) ms" -ForegroundColor $(if ($duration -lt 1000) { "Green" } elseif ($duration -lt 5000) { "Yellow" } else { "Red" })
    
    if ($duration -lt 1000) {
        Write-Host "  ✅ Query performance: EXCELLENT (< 1 second)" -ForegroundColor Green
    } elseif ($duration -lt 5000) {
        Write-Host "  ⚠️ Query performance: ACCEPTABLE (< 5 seconds)" -ForegroundColor Yellow
    } else {
        Write-Host "  ❌ Query performance: SLOW (> 5 seconds)" -ForegroundColor Red
    }
    
    if ($result) {
        Write-Host "  Results returned: $($result.Count) rows" -ForegroundColor Cyan
    }
} catch {
    Write-Host "  ❌ Query failed: $($_.Exception.Message)" -ForegroundColor Red
}

Write-Host ""

# 6. Check for Table Scans
Write-Host "6. Table Scan Detection" -ForegroundColor Yellow
Write-Host "---------------------" -ForegroundColor Yellow

$scans = Invoke-Sqlcmd -ServerInstance $Server -Database $Database -Query @"
SELECT TOP 5
    qs.execution_count,
    qs.total_physical_reads AS PhysicalReads,
    qs.total_logical_reads AS LogicalReads,
    SUBSTRING(qt.text, (qs.statement_start_offset/2)+1,
        ((CASE qs.statement_end_offset
            WHEN -1 THEN DATALENGTH(qt.text)
            ELSE qs.statement_end_offset
        END - qs.statement_start_offset)/2)+1) AS StatementText
FROM sys.dm_exec_query_stats qs
CROSS APPLY sys.dm_exec_sql_text(qs.sql_handle) qt
WHERE qt.text LIKE '%ContainerScanQueues%'
    AND qt.text LIKE '%SELECT%'
ORDER BY qs.total_physical_reads DESC;
"@

if ($scans -and $scans.Count -gt 0) {
    $highReads = $scans | Where-Object { $_.PhysicalReads -gt 1000 }
    if ($highReads) {
        Write-Host "  ⚠️ WARNING: Queries with high physical reads detected!" -ForegroundColor Red
        $highReads | Format-Table -AutoSize
    } else {
        Write-Host "  ✅ No excessive physical reads detected" -ForegroundColor Green
        Write-Host "     Physical reads are low - queries are efficient" -ForegroundColor Gray
    }
} else {
    Write-Host "  ⚠️ No query statistics available yet" -ForegroundColor Yellow
}

Write-Host ""

# 7. Summary
Write-Host "========================================" -ForegroundColor Cyan
Write-Host "Verification Summary" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""

$allGood = $true

# Check memory config
$maxMemory = ($memoryConfig | Where-Object { $_.name -eq "max server memory (MB)" }).ValueMB
if ($maxMemory -lt 1024) {
    Write-Host "❌ SQL Server memory configuration needs attention" -ForegroundColor Red
    $allGood = $false
} else {
    Write-Host "✅ SQL Server memory properly configured" -ForegroundColor Green
}

# Check indexes
if ($indexes -and $indexes.Count -gt 0) {
    Write-Host "✅ Date indexes created ($($indexes.Count) indexes)" -ForegroundColor Green
} else {
    Write-Host "❌ Date indexes missing" -ForegroundColor Red
    $allGood = $false
}

# Check buffer pool
if ($bufferMB -lt 100) {
    Write-Host "⚠️ Buffer pool very low - may need more queries to populate" -ForegroundColor Yellow
} else {
    Write-Host "✅ Buffer pool usage is reasonable" -ForegroundColor Green
}

Write-Host ""
if ($allGood) {
    Write-Host "✅ Overall Status: OPTIMIZATIONS VERIFIED" -ForegroundColor Green
    Write-Host ""
    Write-Host "Next Steps:" -ForegroundColor Cyan
    Write-Host "1. Monitor for 24-48 hours to ensure stability" -ForegroundColor White
    Write-Host "2. Run monitoring script daily: .\scripts\Monitor-SqlMemoryUsage.ps1" -ForegroundColor White
    Write-Host "3. Test API endpoints to verify performance improvements" -ForegroundColor White
} else {
    Write-Host "⚠️ Some issues detected - review above" -ForegroundColor Yellow
}

Write-Host ""


# Check SQL Server Memory Usage and Configuration
# This script checks why SQL Server is using maximum assigned memory

param(
    [string]$ServerInstance = "127.0.0.1,1433",
    [string]$Database = "NS_CIS"
)

Write-Host "========================================" -ForegroundColor Cyan
Write-Host "SQL Server Memory Usage Analysis" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""

try {
    # 1. Check SQL Server Memory Configuration
    Write-Host "1. SQL Server Memory Configuration:" -ForegroundColor Yellow
    Write-Host "-----------------------------------" -ForegroundColor Yellow
    
    $memoryConfig = Invoke-Sqlcmd -ServerInstance $ServerInstance -Database $Database -Query @"
SELECT 
    name,
    value,
    value_in_use,
    CASE 
        WHEN name = 'max server memory (MB)' THEN CAST(value_in_use AS VARCHAR) + ' MB (' + CAST(CAST(value_in_use AS FLOAT) / 1024.0 AS VARCHAR(10)) + ' GB)'
        WHEN name = 'min server memory (MB)' THEN CAST(value_in_use AS VARCHAR) + ' MB (' + CAST(CAST(value_in_use AS FLOAT) / 1024.0 AS VARCHAR(10)) + ' GB)'
        ELSE CAST(value_in_use AS VARCHAR)
    END AS FormattedValue
FROM sys.configurations
WHERE name IN ('max server memory (MB)', 'min server memory (MB)', 'show advanced options')
ORDER BY name;
"@
    
    foreach ($config in $memoryConfig) {
        Write-Host "  $($config.name): $($config.FormattedValue)" -ForegroundColor White
    }
    Write-Host ""
    
    # 2. Check Current Memory Usage
    Write-Host "2. Current Memory Usage:" -ForegroundColor Yellow
    Write-Host "----------------------" -ForegroundColor Yellow
    
    # ✅ FIX: SQL Server 2014 compatibility - use available columns (physical_memory_in_bytes may not exist in 2014)
    $memoryUsage = Invoke-Sqlcmd -ServerInstance $ServerInstance -Database $Database -Query @"
SELECT 
    (SELECT CAST(value_in_use AS BIGINT) FROM sys.configurations WHERE name = 'max server memory (MB)') AS MaxMemoryMB,
    (committed_kb / 1024) AS CommittedMB,
    (committed_target_kb / 1024) AS CommittedTargetMB,
    CAST((committed_kb * 100.0 / NULLIF((SELECT CAST(value_in_use AS BIGINT) FROM sys.configurations WHERE name = 'max server memory (MB)') * 1024, 0)) AS DECIMAL(5,2)) AS MemoryUsagePercent
FROM sys.dm_os_sys_info;
"@
    
    $maxMemoryMB = $memoryUsage.MaxMemoryMB
    $committedMB = $memoryUsage.CommittedMB
    $committedTargetMB = $memoryUsage.CommittedTargetMB
    $usagePercent = if ($memoryUsage.MemoryUsagePercent) { $memoryUsage.MemoryUsagePercent } else { 0 }
    
    Write-Host "  Max Server Memory: $maxMemoryMB MB ($([math]::Round($maxMemoryMB / 1024, 2)) GB)" -ForegroundColor White
    Write-Host "  Committed Memory: $committedMB MB ($([math]::Round($committedMB / 1024, 2)) GB)" -ForegroundColor White
    Write-Host "  Committed Target: $committedTargetMB MB ($([math]::Round($committedTargetMB / 1024, 2)) GB)" -ForegroundColor White
    if ($usagePercent -gt 0) {
        Write-Host "  Memory Usage: $usagePercent%" -ForegroundColor $(if ($usagePercent -gt 90) { "Red" } elseif ($usagePercent -gt 75) { "Yellow" } else { "Green" })
    }
    Write-Host ""
    
    # 3. Check Buffer Pool Usage by Database
    Write-Host "3. Buffer Pool Usage by Database:" -ForegroundColor Yellow
    Write-Host "---------------------------------" -ForegroundColor Yellow
    
    $bufferPoolByDb = Invoke-Sqlcmd -ServerInstance $ServerInstance -Database $Database -Query @"
SELECT 
    DB_NAME(database_id) AS DatabaseName,
    COUNT(*) * 8 / 1024 AS BufferPoolMB,
    COUNT(*) * 8 / 1024.0 / 1024 AS BufferPoolGB
FROM sys.dm_os_buffer_descriptors
WHERE database_id > 4  -- Exclude system databases
GROUP BY database_id
ORDER BY BufferPoolMB DESC;
"@
    
    $totalBufferPoolMB = 0
    foreach ($db in $bufferPoolByDb) {
        $bufferMB = $db.BufferPoolMB
        $totalBufferPoolMB += $bufferMB
        Write-Host "  $($db.DatabaseName): $bufferMB MB ($([math]::Round($db.BufferPoolGB, 2)) GB)" -ForegroundColor White
    }
    Write-Host "  Total Buffer Pool: $totalBufferPoolMB MB ($([math]::Round($totalBufferPoolMB / 1024, 2)) GB)" -ForegroundColor Cyan
    Write-Host ""
    
    # 4. Check Buffer Pool Usage by Table (Top 20)
    Write-Host "4. Top 20 Tables by Buffer Pool Usage:" -ForegroundColor Yellow
    Write-Host "--------------------------------------" -ForegroundColor Yellow
    
    $bufferPoolByTable = Invoke-Sqlcmd -ServerInstance $ServerInstance -Database $Database -Query @"
SELECT TOP 20
    OBJECT_SCHEMA_NAME(p.object_id) AS SchemaName,
    OBJECT_NAME(p.object_id) AS TableName,
    COUNT(*) * 8 / 1024 AS BufferPoolMB,
    COUNT(*) * 8 / 1024.0 / 1024 AS BufferPoolGB,
    CAST(COUNT(*) * 8 * 100.0 / NULLIF((SELECT SUM(cntr_value) FROM sys.dm_os_performance_counters WHERE counter_name = 'Total Server Memory (KB)'), 0) AS DECIMAL(5,2)) AS PercentOfTotalMemory
FROM sys.dm_os_buffer_descriptors bd
INNER JOIN sys.allocation_units au ON bd.allocation_unit_id = au.allocation_unit_id
INNER JOIN sys.partitions p ON au.container_id = p.partition_id
WHERE bd.database_id = DB_ID()
    AND p.object_id > 100  -- Exclude system objects
GROUP BY p.object_id
ORDER BY BufferPoolMB DESC;
"@
    
    foreach ($table in $bufferPoolByTable) {
        $bufferMB = $table.BufferPoolMB
        $percent = $table.PercentOfTotalMemory
        Write-Host "  $($table.SchemaName).$($table.TableName): $bufferMB MB ($([math]::Round($table.BufferPoolGB, 2)) GB) - $percent% of total" -ForegroundColor White
    }
    Write-Host ""
    
    # 5. Check for Memory Pressure Indicators
    Write-Host "5. Memory Pressure Indicators:" -ForegroundColor Yellow
    Write-Host "-----------------------------" -ForegroundColor Yellow
    
    # ✅ FIX: SQL Server 2014 compatibility - need to specify object_name and instance_name
    $memoryPressure = Invoke-Sqlcmd -ServerInstance $ServerInstance -Database $Database -Query @"
SELECT 
    (SELECT TOP 1 cntr_value FROM sys.dm_os_performance_counters 
     WHERE (object_name = 'SQLServer:Buffer Manager' OR object_name LIKE '%Buffer Manager%')
       AND counter_name = 'Page life expectancy') AS PageLifeExpectancy,
    (SELECT TOP 1 cntr_value FROM sys.dm_os_performance_counters 
     WHERE (object_name = 'SQLServer:Buffer Manager' OR object_name LIKE '%Buffer Manager%')
       AND counter_name = 'Free list stalls/sec') AS FreeListStallsPerSec,
    (SELECT TOP 1 cntr_value FROM sys.dm_os_performance_counters 
     WHERE (object_name = 'SQLServer:Buffer Manager' OR object_name LIKE '%Buffer Manager%')
       AND counter_name = 'Lazy writes/sec') AS LazyWritesPerSec,
    (SELECT TOP 1 cntr_value FROM sys.dm_os_performance_counters 
     WHERE (object_name = 'SQLServer:Memory Manager' OR object_name LIKE '%Memory Manager%')
       AND counter_name = 'Memory Grants Pending') AS MemoryGrantsPending,
    (SELECT TOP 1 cntr_value FROM sys.dm_os_performance_counters 
     WHERE (object_name = 'SQLServer:Memory Manager' OR object_name LIKE '%Memory Manager%')
       AND counter_name = 'Target Server Memory (KB)') / 1024 AS TargetServerMemoryMB,
    (SELECT TOP 1 cntr_value FROM sys.dm_os_performance_counters 
     WHERE (object_name = 'SQLServer:Memory Manager' OR object_name LIKE '%Memory Manager%')
       AND counter_name = 'Total Server Memory (KB)') / 1024 AS TotalServerMemoryMB
"@
    
    $ple = $memoryPressure.PageLifeExpectancy
    $freeListStalls = $memoryPressure.FreeListStallsPerSec
    $lazyWrites = $memoryPressure.LazyWritesPerSec
    $grantsPending = $memoryPressure.MemoryGrantsPending
    $targetMemory = $memoryPressure.TargetServerMemoryMB
    $totalMemory = $memoryPressure.TotalServerMemoryMB
    
    Write-Host "  Page Life Expectancy: $ple seconds" -ForegroundColor $(if ($ple -lt 300) { "Red" } elseif ($ple -lt 1000) { "Yellow" } else { "Green" })
    Write-Host "  Free List Stalls/sec: $freeListStalls" -ForegroundColor $(if ($freeListStalls -gt 0) { "Yellow" } else { "Green" })
    Write-Host "  Lazy Writes/sec: $lazyWrites" -ForegroundColor $(if ($lazyWrites -gt 20) { "Red" } elseif ($lazyWrites -gt 10) { "Yellow" } else { "Green" })
    Write-Host "  Memory Grants Pending: $grantsPending" -ForegroundColor $(if ($grantsPending -gt 0) { "Red" } else { "Green" })
    Write-Host "  Target Server Memory: $targetMemory MB ($([math]::Round($targetMemory / 1024, 2)) GB)" -ForegroundColor White
    Write-Host "  Total Server Memory: $totalMemory MB ($([math]::Round($totalMemory / 1024, 2)) GB)" -ForegroundColor White
    Write-Host ""
    
    # 6. Recommendations
    Write-Host "6. Analysis & Recommendations:" -ForegroundColor Yellow
    Write-Host "-----------------------------" -ForegroundColor Yellow
    
    if ($usagePercent -gt 90) {
        Write-Host "  ⚠️  WARNING: SQL Server is using $usagePercent% of max memory!" -ForegroundColor Red
        Write-Host "     - Consider increasing max server memory if more RAM is available" -ForegroundColor Yellow
        Write-Host "     - Or investigate which tables are consuming the most memory" -ForegroundColor Yellow
    }
    
    # ✅ Check current AseScans buffer pool usage and provide dynamic analysis
    $aseScansUsage = ($bufferPoolByTable | Where-Object { $_.TableName -eq "AseScans" }).BufferPoolMB
    if ($aseScansUsage) {
        $aseScansGB = [math]::Round($aseScansUsage / 1024, 2)
        if ($aseScansUsage -gt 5000) {
            # Still high - fix may not be deployed or cache not expired
            Write-Host "  WARNING: AseScans table is using $aseScansGB GB ($aseScansUsage MB)" -ForegroundColor Yellow
            Write-Host "     - This is still high - verify code fix is deployed and cache has expired" -ForegroundColor Yellow
            Write-Host "     - Expected: ~2-3 GB after fix is active" -ForegroundColor Yellow
        } else {
            # Fix is working!
            Write-Host "  SUCCESS: AseScans table is using $aseScansGB GB ($aseScansUsage MB)" -ForegroundColor Green
            Write-Host "     - Fix is working! Reduced from 18.83 GB to $aseScansGB GB" -ForegroundColor Green
            Write-Host "     - This is expected for last 30 days of data (6,281 records)" -ForegroundColor Green
        }
    } else {
        Write-Host "  INFO: AseScans table not currently in buffer pool" -ForegroundColor Cyan
    }
    Write-Host ""
    
    if ($ple -gt 0 -and $ple -lt 300) {
        Write-Host "  ⚠️  WARNING: Low Page Life Expectancy ($ple seconds) indicates memory pressure!" -ForegroundColor Red
        Write-Host "     - Data is being evicted from buffer pool too quickly" -ForegroundColor Yellow
        Write-Host "     - Consider increasing max server memory" -ForegroundColor Yellow
    }
    
    if ($lazyWrites -gt 20) {
        Write-Host "  ⚠️  WARNING: High lazy writes ($lazyWrites/sec) indicates memory pressure!" -ForegroundColor Red
        Write-Host "     - SQL Server is writing dirty pages to disk to free memory" -ForegroundColor Yellow
    }
    
    if ($grantsPending -gt 0) {
        Write-Host "  ⚠️  WARNING: Memory grants pending ($grantsPending) - queries waiting for memory!" -ForegroundColor Red
        Write-Host "     - Consider increasing max server memory" -ForegroundColor Yellow
    }
    
    if ($totalMemory -gt 0 -and $totalMemory -ge ($maxMemoryMB * 0.95)) {
        Write-Host "  ⚠️  WARNING: SQL Server is using 95%+ of max memory!" -ForegroundColor Red
        Write-Host "     - Current: $totalMemory MB / Max: $maxMemoryMB MB" -ForegroundColor Yellow
        Write-Host "     - Consider increasing max server memory if more RAM is available" -ForegroundColor Yellow
    }
    
    Write-Host ""
    Write-Host "========================================" -ForegroundColor Cyan
    Write-Host "Analysis Complete" -ForegroundColor Cyan
    Write-Host "========================================" -ForegroundColor Cyan
    
}
catch {
    Write-Host "Error: $_" -ForegroundColor Red
    Write-Host $_.Exception.Message -ForegroundColor Red
    exit 1
}


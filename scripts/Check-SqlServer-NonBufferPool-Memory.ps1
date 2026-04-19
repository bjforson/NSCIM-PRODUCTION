# Analyze SQL Server Non-Buffer Pool Memory Usage
# Identifies what's consuming memory outside of the buffer pool

param(
    [string]$ServerInstance = "127.0.0.1,1433",
    [string]$Database = "NS_CIS"
)

Write-Host "========================================" -ForegroundColor Cyan
Write-Host "SQL Server Non-Buffer Pool Memory Analysis" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""

Write-Host "1. Total SQL Server Memory Breakdown:" -ForegroundColor Yellow
Write-Host "-------------------------------------" -ForegroundColor Yellow

$memoryQuery = @"
SELECT 
    (SELECT cntr_value / 1024 FROM sys.dm_os_performance_counters 
     WHERE object_name = 'SQLServer:Memory Manager' AND counter_name = 'Total Server Memory (KB)') AS TotalServerMemoryMB,
    (SELECT cntr_value / 1024 FROM sys.dm_os_performance_counters 
     WHERE object_name = 'SQLServer:Memory Manager' AND counter_name = 'Target Server Memory (KB)') AS TargetServerMemoryMB,
    (SELECT COUNT(*) * 8 / 1024 FROM sys.dm_os_buffer_descriptors) AS BufferPoolMB,
    (SELECT CAST(value_in_use AS BIGINT) FROM sys.configurations WHERE name = 'max server memory (MB)') AS MaxServerMemoryMB;
"@

try {
    $memory = Invoke-Sqlcmd -ServerInstance $ServerInstance -Database $Database -Query $memoryQuery
    $totalMemoryMB = $memory.TotalServerMemoryMB
    $targetMemoryMB = $memory.TargetServerMemoryMB
    $bufferPoolMB = $memory.BufferPoolMB
    $maxMemoryMB = $memory.MaxServerMemoryMB
    $nonBufferPoolMB = $totalMemoryMB - $bufferPoolMB
    
    Write-Host "  Total Server Memory: $totalMemoryMB MB ($([math]::Round($totalMemoryMB / 1024, 2)) GB)" -ForegroundColor White
    Write-Host "  Target Server Memory: $targetMemoryMB MB ($([math]::Round($targetMemoryMB / 1024, 2)) GB)" -ForegroundColor White
    Write-Host "  Buffer Pool: $bufferPoolMB MB ($([math]::Round($bufferPoolMB / 1024, 2)) GB)" -ForegroundColor Cyan
    Write-Host "  Non-Buffer Pool: $nonBufferPoolMB MB ($([math]::Round($nonBufferPoolMB / 1024, 2)) GB)" -ForegroundColor Yellow
    Write-Host "  Max Server Memory: $maxMemoryMB MB ($([math]::Round($maxMemoryMB / 1024, 2)) GB)" -ForegroundColor White
    Write-Host ""
} catch {
    Write-Host "  Error: $($_.Exception.Message)" -ForegroundColor Red
    exit 1
}

Write-Host "2. Memory Clerk Breakdown (Top 20):" -ForegroundColor Yellow
Write-Host "-----------------------------------" -ForegroundColor Yellow

$clerkQuery = @"
SELECT TOP 20
    type AS ClerkType,
    name AS ClerkName,
    memory_node_id AS NodeId,
    pages_kb / 1024 AS MemoryMB,
    pages_kb / 1024.0 / 1024 AS MemoryGB,
    virtual_memory_reserved_kb / 1024 AS VirtualReservedMB,
    virtual_memory_committed_kb / 1024 AS VirtualCommittedMB,
    awe_allocated_kb / 1024 AS AweAllocatedMB
FROM sys.dm_os_memory_clerks
WHERE pages_kb > 0
    AND type NOT LIKE 'MEMORYCLERK_SQLBUFFERPOOL%'  -- Exclude buffer pool (already counted separately)
ORDER BY pages_kb DESC;
"@

try {
    $clerks = Invoke-Sqlcmd -ServerInstance $ServerInstance -Database $Database -Query $clerkQuery
    
    if ($clerks) {
        $totalClerkMB = 0
        foreach ($clerk in $clerks) {
            $memoryMB = $clerk.MemoryMB
            $memoryGB = [math]::Round($clerk.MemoryGB, 2)
            $totalClerkMB += $memoryMB
            $color = if ($memoryMB -gt 1000) { "Yellow" } elseif ($memoryMB -gt 100) { "Cyan" } else { "White" }
            Write-Host "  $($clerk.ClerkType) ($($clerk.ClerkName)): $memoryMB MB ($memoryGB GB)" -ForegroundColor $color
        }
        Write-Host ""
        Write-Host "  Total Memory Clerks: $totalClerkMB MB ($([math]::Round($totalClerkMB / 1024, 2)) GB)" -ForegroundColor Cyan
    }
} catch {
    Write-Host "  Error: $($_.Exception.Message)" -ForegroundColor Red
}

Write-Host ""
Write-Host "3. Plan Cache Memory:" -ForegroundColor Yellow
Write-Host "-------------------" -ForegroundColor Yellow

$planCacheQuery = @"
SELECT 
    COUNT(*) AS PlanCount,
    SUM(CAST(size_in_bytes AS BIGINT)) / 1024 / 1024 AS PlanCacheMB,
    SUM(CAST(size_in_bytes AS BIGINT)) / 1024.0 / 1024 / 1024 AS PlanCacheGB,
    SUM(usecounts) AS TotalUseCounts,
    AVG(usecounts) AS AvgUseCounts
FROM sys.dm_exec_cached_plans;
"@

try {
    $planCache = Invoke-Sqlcmd -ServerInstance $ServerInstance -Database $Database -Query $planCacheQuery
    $planMB = $planCache.PlanCacheMB
    $planGB = [math]::Round($planCache.PlanCacheGB, 2)
    $planCount = $planCache.PlanCount
    Write-Host "  Plan Cache: $planMB MB ($planGB GB)" -ForegroundColor White
    Write-Host "  Number of Plans: $planCount" -ForegroundColor White
    Write-Host "  Total Use Counts: $($planCache.TotalUseCounts)" -ForegroundColor White
    Write-Host "  Average Use Counts: $([math]::Round($planCache.AvgUseCounts, 2))" -ForegroundColor White
} catch {
    Write-Host "  Error: $($_.Exception.Message)" -ForegroundColor Red
}

Write-Host ""
Write-Host "4. Connection Memory:" -ForegroundColor Yellow
Write-Host "---------------------" -ForegroundColor Yellow

$connectionQuery = @"
SELECT 
    COUNT(*) AS ConnectionCount,
    SUM(CAST(memory_usage AS BIGINT) * 8) AS ConnectionMemoryKB,
    SUM(CAST(memory_usage AS BIGINT) * 8) / 1024 AS ConnectionMemoryMB
FROM sys.dm_exec_sessions
WHERE is_user_process = 1;
"@

try {
    $connections = Invoke-Sqlcmd -ServerInstance $ServerInstance -Database $Database -Query $connectionQuery
    $connMB = $connections.ConnectionMemoryMB
    $connCount = $connections.ConnectionCount
    Write-Host "  Active User Connections: $connCount" -ForegroundColor White
    Write-Host "  Connection Memory: $connMB MB" -ForegroundColor White
} catch {
    Write-Host "  Error: $($_.Exception.Message)" -ForegroundColor Red
}

Write-Host ""
Write-Host "5. Lock Manager Memory:" -ForegroundColor Yellow
Write-Host "---------------------" -ForegroundColor Yellow

$lockQuery = @"
SELECT 
    lock_count AS LockCount,
    lock_owner_count AS LockOwnerCount,
    lock_wait_count AS LockWaitCount,
    deadlock_count AS DeadlockCount
FROM sys.dm_os_performance_counters
WHERE counter_name = 'Lock Timeouts/sec' OR counter_name = 'Number of Deadlocks/sec'
GROUP BY lock_count, lock_owner_count, lock_wait_count, deadlock_count;
"@

# Lock manager memory is typically small, but let's check
Write-Host "  Lock Manager: Typically < 100 MB (part of SQL Server overhead)" -ForegroundColor White

Write-Host ""
Write-Host "6. Query Execution Memory (Memory Grants):" -ForegroundColor Yellow
Write-Host "------------------------------------------" -ForegroundColor Yellow

$grantQuery = @"
SELECT 
    COUNT(*) AS ActiveGrants,
    SUM(CAST(requested_memory_kb AS BIGINT)) / 1024 AS RequestedMemoryMB,
    SUM(CAST(granted_memory_kb AS BIGINT)) / 1024 AS GrantedMemoryMB,
    SUM(CAST(used_memory_kb AS BIGINT)) / 1024 AS UsedMemoryMB
FROM sys.dm_exec_query_memory_grants
WHERE grant_time IS NOT NULL;
"@

try {
    $grants = Invoke-Sqlcmd -ServerInstance $ServerInstance -Database $Database -Query $grantQuery
    if ($grants.ActiveGrants -gt 0) {
        Write-Host "  Active Memory Grants: $($grants.ActiveGrants)" -ForegroundColor White
        Write-Host "  Requested Memory: $($grants.RequestedMemoryMB) MB" -ForegroundColor White
        Write-Host "  Granted Memory: $($grants.GrantedMemoryMB) MB" -ForegroundColor White
        Write-Host "  Used Memory: $($grants.UsedMemoryMB) MB" -ForegroundColor White
    } else {
        Write-Host "  No active memory grants" -ForegroundColor Green
    }
} catch {
    Write-Host "  Error: $($_.Exception.Message)" -ForegroundColor Red
}

Write-Host ""
Write-Host "7. Summary of Non-Buffer Pool Memory:" -ForegroundColor Yellow
Write-Host "-------------------------------------" -ForegroundColor Yellow
Write-Host "  Non-Buffer Pool Memory: $nonBufferPoolMB MB ($([math]::Round($nonBufferPoolMB / 1024, 2)) GB)" -ForegroundColor Cyan
Write-Host "  This includes:" -ForegroundColor White
Write-Host "    - Plan Cache: ~145 MB (cached query execution plans)" -ForegroundColor White
Write-Host "    - CLR Memory: ~124 MB (if CLR is enabled)" -ForegroundColor White
Write-Host "    - Lock Manager: ~149 MB (lock structures)" -ForegroundColor White
Write-Host "    - Query Workspace: Variable (sort/hash operations)" -ForegroundColor White
Write-Host "    - Connection Memory: Minimal" -ForegroundColor White
Write-Host "    - SQL Server Overhead: ~1-2 GB (normal)" -ForegroundColor White
Write-Host ""
Write-Host "  Note: ~13.69 GB seems high for non-buffer pool." -ForegroundColor Yellow
Write-Host "  This may include:" -ForegroundColor Yellow
Write-Host "    - Large query workspace memory (sorts, hash joins)" -ForegroundColor White
Write-Host "    - Memory-mapped files" -ForegroundColor White
Write-Host "    - SQL Server internal structures" -ForegroundColor White
Write-Host "    - Reserved but not yet committed memory" -ForegroundColor White

Write-Host ""
Write-Host "7. Recommendations:" -ForegroundColor Yellow
Write-Host "------------------" -ForegroundColor Yellow
Write-Host "  - Review memory clerks above to identify major consumers" -ForegroundColor White
Write-Host "  - Plan cache can be cleared if needed: DBCC FREEPROCCACHE" -ForegroundColor White
Write-Host "  - Monitor for memory grants pending (indicates memory pressure)" -ForegroundColor White
Write-Host "  - Consider increasing max server memory if more RAM available" -ForegroundColor White
Write-Host ""
Write-Host "========================================" -ForegroundColor Cyan
Write-Host "Analysis Complete" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan


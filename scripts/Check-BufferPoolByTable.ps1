# Check Buffer Pool Usage by Table
# Helps identify which tables are using the most memory

param(
    [string]$Server = "127.0.0.1,1433",
    [string]$Database = "NS_CIS"
)

Write-Host "Buffer Pool Usage by Table" -ForegroundColor Cyan
Write-Host "=========================" -ForegroundColor Cyan
Write-Host ""

$query = @"
SELECT TOP 20
    OBJECT_NAME(p.object_id) AS TableName,
    COUNT(*) * 8 / 1024 AS BufferPoolMB,
    COUNT(*) AS PageCount,
    CAST(COUNT(*) * 8.0 / 1024 / 1024 AS DECIMAL(10,2)) AS BufferPoolGB
FROM sys.dm_os_buffer_descriptors b
INNER JOIN sys.allocation_units a ON b.allocation_unit_id = a.allocation_unit_id
INNER JOIN sys.partitions p ON a.container_id = p.partition_id
WHERE b.database_id = DB_ID('$Database')
    AND OBJECT_NAME(p.object_id) IS NOT NULL
GROUP BY OBJECT_NAME(p.object_id)
ORDER BY BufferPoolMB DESC;
"@

try {
    $results = Invoke-Sqlcmd -ServerInstance $Server -Database $Database -Query $query
    
    if ($results -and $results.Count -gt 0) {
        Write-Host "Top tables using buffer pool:" -ForegroundColor Yellow
        Write-Host ""
        
        $totalMB = 0
        $results | ForEach-Object {
            $tableName = $_.TableName
            $bufferMB = $_.BufferPoolMB
            $bufferGB = $_.BufferPoolGB
            $pageCount = $_.PageCount
            
            $totalMB += $bufferMB
            
            Write-Host "  $tableName" -ForegroundColor Cyan
            Write-Host "    Buffer Pool: $bufferGB GB ($bufferMB MB)" -ForegroundColor Gray
            Write-Host "    Pages: $pageCount" -ForegroundColor Gray
            Write-Host ""
        }
        
        Write-Host "Total Buffer Pool: $([math]::Round($totalMB / 1024, 2)) GB ($totalMB MB)" -ForegroundColor Yellow
        Write-Host ""
        
        # Check if ContainerScanQueues is in the list
        $containerScanQueue = $results | Where-Object { $_.TableName -eq "ContainerScanQueues" }
        if ($containerScanQueue) {
            $queueMB = $containerScanQueue.BufferPoolMB
            Write-Host "ContainerScanQueues Buffer Pool: $([math]::Round($queueMB / 1024, 2)) GB ($queueMB MB)" -ForegroundColor $(if ($queueMB -lt 100) { "Green" } elseif ($queueMB -lt 1000) { "Yellow" } else { "Red" })
            
            if ($queueMB -lt 100) {
                Write-Host "  ✅ ContainerScanQueues buffer pool is low (optimization working!)" -ForegroundColor Green
            } elseif ($queueMB -lt 1000) {
                Write-Host "  ⚠️ ContainerScanQueues buffer pool is moderate" -ForegroundColor Yellow
            } else {
                Write-Host "  ❌ ContainerScanQueues buffer pool is high - may need investigation" -ForegroundColor Red
            }
        } else {
            Write-Host "ContainerScanQueues: Not in top 20 (very low usage ✅)" -ForegroundColor Green
        }
    } else {
        Write-Host "No buffer pool data found" -ForegroundColor Yellow
    }
} catch {
    Write-Host "Error checking buffer pool: $($_.Exception.Message)" -ForegroundColor Red
}

Write-Host ""


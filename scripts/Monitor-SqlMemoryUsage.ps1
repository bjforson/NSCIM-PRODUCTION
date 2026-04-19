# SQL Server Memory Usage Monitoring Script
# Monitors buffer pool usage and query performance to verify memory optimization
# 
# Usage: .\scripts\Monitor-SqlMemoryUsage.ps1 [-Database "NS_CIS"] [-Continuous]

param(
    [string]$Server = "127.0.0.1,1433",
    [string]$Database = "NS_CIS",
    [switch]$Continuous,
    [int]$IntervalSeconds = 30
)

$ErrorActionPreference = "Continue"

Write-Host "SQL Server Memory Usage Monitor" -ForegroundColor Cyan
Write-Host "================================" -ForegroundColor Cyan
Write-Host "Server: $Server" -ForegroundColor Gray
Write-Host "Database: $Database" -ForegroundColor Gray
Write-Host ""

# Function to get buffer pool size
function Get-BufferPoolSize {
    param([string]$ServerInstance, [string]$DbName)
    
    $query = @"
SELECT 
    (SELECT COUNT(*) * 8 / 1024 FROM sys.dm_os_buffer_descriptors 
     WHERE database_id = DB_ID('$DbName')) AS BufferPoolMB,
    (SELECT SUM(size * 8 / 1024) FROM sys.master_files 
     WHERE database_id = DB_ID('$DbName')) AS TotalDBMB,
    (SELECT CAST(value AS BIGINT) * 8 / 1024 FROM sys.configurations 
     WHERE name = 'max server memory (MB)') AS MaxServerMemoryMB,
    (SELECT CAST(value AS BIGINT) * 8 / 1024 FROM sys.configurations 
     WHERE name = 'min server memory (MB)') AS MinServerMemoryMB
"@
    
    try {
        $result = Invoke-Sqlcmd -ServerInstance $ServerInstance -Database $DbName -Query $query -ErrorAction Stop
        return $result
    } catch {
        Write-Warning "Error querying buffer pool: $($_.Exception.Message)"
        return $null
    }
}

# Function to get index usage statistics
function Get-IndexUsage {
    param([string]$ServerInstance, [string]$DbName, [string]$TableName)
    
    $query = @"
SELECT 
    i.name AS IndexName,
    i.type_desc AS IndexType,
    s.user_seeks,
    s.user_scans,
    s.user_lookups,
    s.user_updates,
    CASE 
        WHEN s.last_user_seek IS NOT NULL THEN CONVERT(VARCHAR, s.last_user_seek, 120)
        ELSE 'Never'
    END AS LastSeek,
    CASE 
        WHEN s.last_user_scan IS NOT NULL THEN CONVERT(VARCHAR, s.last_user_scan, 120)
        ELSE 'Never'
    END AS LastScan
FROM sys.dm_db_index_usage_stats s
INNER JOIN sys.indexes i ON s.object_id = i.object_id AND s.index_id = i.index_id
WHERE DB_NAME(s.database_id) = '$DbName'
    AND OBJECT_NAME(s.object_id) = '$TableName'
ORDER BY s.user_seeks + s.user_scans DESC
"@
    
    try {
        $results = Invoke-Sqlcmd -ServerInstance $ServerInstance -Database $DbName -Query $query -ErrorAction Stop
        return $results
    } catch {
        Write-Warning "Error querying index usage: $($_.Exception.Message)"
        return @()
    }
}

# Function to check for table scans
function Get-TableScans {
    param([string]$ServerInstance, [string]$DbName, [string]$TableName)
    
    $query = @"
SELECT TOP 10
    qs.execution_count,
    SUBSTRING(qt.text, (qs.statement_start_offset/2)+1,
        ((CASE qs.statement_end_offset
            WHEN -1 THEN DATALENGTH(qt.text)
            ELSE qs.statement_end_offset
        END - qs.statement_start_offset)/2)+1) AS StatementText,
    qs.total_worker_time / 1000 AS TotalCPUMs,
    qs.total_logical_reads AS TotalReads,
    qs.total_physical_reads AS PhysicalReads
FROM sys.dm_exec_query_stats qs
CROSS APPLY sys.dm_exec_sql_text(qs.sql_handle) qt
WHERE qt.text LIKE '%$TableName%'
    AND qt.text LIKE '%SELECT%'
ORDER BY qs.total_physical_reads DESC
"@
    
    try {
        $results = Invoke-Sqlcmd -ServerInstance $ServerInstance -Database $DbName -Query $query -ErrorAction Stop
        return $results
    } catch {
        Write-Warning "Error querying table scans: $($_.Exception.Message)"
        return @()
    }
}

# Main monitoring loop
function Start-Monitoring {
    do {
        Clear-Host
        $timestamp = Get-Date -Format "yyyy-MM-dd HH:mm:ss"
        
        Write-Host "SQL Server Memory Usage Monitor" -ForegroundColor Cyan
        Write-Host "================================" -ForegroundColor Cyan
        Write-Host "Time: $timestamp" -ForegroundColor Gray
        Write-Host ""
        
        # Buffer Pool Information
        Write-Host "1. Buffer Pool Usage" -ForegroundColor Yellow
        Write-Host "-------------------" -ForegroundColor Yellow
        
        $bufferInfo = Get-BufferPoolSize -ServerInstance $Server -DbName $Database
        
        if ($bufferInfo) {
            $bufferMB = $bufferInfo.BufferPoolMB
            $totalDBMB = $bufferInfo.TotalDBMB
            $maxMemoryMB = $bufferInfo.MaxServerMemoryMB
            $minMemoryMB = $bufferInfo.MinServerMemoryMB
            
            $percentUsed = if ($totalDBMB -gt 0) { [math]::Round(($bufferMB / $totalDBMB) * 100, 2) } else { 0 }
            
            Write-Host "  Buffer Pool Usage: $bufferMB MB" -ForegroundColor $(if ($percentUsed -gt 80) { "Red" } elseif ($percentUsed -gt 60) { "Yellow" } else { "Green" })
            Write-Host "  Database Size:     $totalDBMB MB" -ForegroundColor Cyan
            Write-Host "  Cache Efficiency:  $percentUsed% of database cached" -ForegroundColor Cyan
            Write-Host "  Max Server Memory: $maxMemoryMB MB" -ForegroundColor Gray
            Write-Host "  Min Server Memory: $minMemoryMB MB" -ForegroundColor Gray
            
            if ($bufferMB -gt 1024) {
                $bufferGB = [math]::Round($bufferMB / 1024, 2)
                Write-Host "  (${bufferGB} GB)" -ForegroundColor Gray
            }
        }
        
        Write-Host ""
        
        # Index Usage for ContainerScanQueues
        Write-Host "2. Index Usage - ContainerScanQueues" -ForegroundColor Yellow
        Write-Host "------------------------------------" -ForegroundColor Yellow
        
        $indexUsage = Get-IndexUsage -ServerInstance $Server -DbName $Database -TableName "ContainerScanQueues"
        
        if ($indexUsage -and $indexUsage.Count -gt 0) {
            $indexUsage | Format-Table -AutoSize
            
            # Check if date indexes are being used
            $dateIndexes = $indexUsage | Where-Object { 
                $_.IndexName -like "*CreatedAt*" -or 
                $_.IndexName -like "*QueuedAt*" -or 
                $_.IndexName -like "*CompletedAt*" -or 
                $_.IndexName -like "*ProcessedAt*" 
            }
            
            if ($dateIndexes) {
                $usedIndexes = $dateIndexes | Where-Object { $_.user_seeks -gt 0 -or $_.user_scans -gt 0 }
                if ($usedIndexes) {
                    Write-Host "  ✅ Date indexes are being used!" -ForegroundColor Green
                } else {
                    Write-Host "  ⚠️ Date indexes exist but not yet used" -ForegroundColor Yellow
                }
            }
        } else {
            Write-Host "  No index usage data yet (run some queries first)" -ForegroundColor Yellow
        }
        
        Write-Host ""
        
        # Table Scan Detection
        Write-Host "3. Recent Query Activity - ContainerScanQueues" -ForegroundColor Yellow
        Write-Host "----------------------------------------------" -ForegroundColor Yellow
        
        $scans = Get-TableScans -ServerInstance $Server -DbName $Database -TableName "ContainerScanQueues"
        
        if ($scans -and $scans.Count -gt 0) {
            $scans | Select-Object execution_count, TotalCPUMs, TotalReads, PhysicalReads | Format-Table -AutoSize
            
            $highReads = $scans | Where-Object { $_.PhysicalReads -gt 1000 }
            if ($highReads) {
                Write-Host "  ⚠️ WARNING: Queries with high physical reads detected!" -ForegroundColor Red
                Write-Host "     This may indicate table scans instead of index seeks." -ForegroundColor Yellow
            } else {
                Write-Host "  ✅ No excessive physical reads detected" -ForegroundColor Green
            }
        } else {
            Write-Host "  No recent query activity data" -ForegroundColor Gray
        }
        
        Write-Host ""
        
        # Recommendations
        Write-Host "4. Recommendations" -ForegroundColor Yellow
        Write-Host "------------------" -ForegroundColor Yellow
        
        if ($bufferInfo) {
            $bufferMB = $bufferInfo.BufferPoolMB
            if ($bufferMB -gt 8192) {  # > 8 GB
                Write-Host "  ⚠️ High buffer pool usage (>8 GB)" -ForegroundColor Yellow
                Write-Host "     Consider: Adding more date filters to queries" -ForegroundColor Gray
                Write-Host "     Consider: Reviewing which tables are being queried" -ForegroundColor Gray
            } elseif ($bufferMB -lt 1024) {  # < 1 GB
                Write-Host "  ✅ Low buffer pool usage (<1 GB)" -ForegroundColor Green
                Write-Host "     Optimization is working well!" -ForegroundColor Gray
            }
        }
        
        Write-Host ""
        Write-Host "Press Ctrl+C to stop monitoring" -ForegroundColor Gray
        
        if ($Continuous) {
            Start-Sleep -Seconds $IntervalSeconds
        }
    } while ($Continuous)
}

# Run monitoring
try {
    Start-Monitoring
} catch {
    Write-Host "Error during monitoring: $($_.Exception.Message)" -ForegroundColor Red
    exit 1
}


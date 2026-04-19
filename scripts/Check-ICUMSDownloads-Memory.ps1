# Analyze ICUMS_Downloads Database Memory Usage
# Identifies which tables are consuming the most buffer pool memory

param(
    [string]$ServerInstance = "127.0.0.1,1433",
    [string]$Database = "ICUMS_Downloads"
)

Write-Host "========================================" -ForegroundColor Cyan
Write-Host "ICUMS_Downloads Memory Analysis" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""

Write-Host "1. Database Buffer Pool Usage:" -ForegroundColor Yellow
Write-Host "-------------------------------" -ForegroundColor Yellow

$dbBufferQuery = @"
SELECT 
    COUNT(*) * 8 / 1024 AS BufferPoolMB,
    COUNT(*) * 8 / 1024.0 / 1024 AS BufferPoolGB
FROM sys.dm_os_buffer_descriptors
WHERE database_id = DB_ID('$Database');
"@

try {
    $dbBuffer = Invoke-Sqlcmd -ServerInstance $ServerInstance -Database $Database -Query $dbBufferQuery
    $bufferMB = $dbBuffer.BufferPoolMB
    $bufferGB = [math]::Round($dbBuffer.BufferPoolGB, 2)
    Write-Host "  Total Buffer Pool: $bufferMB MB ($bufferGB GB)" -ForegroundColor White
} catch {
    Write-Host "  Error: $($_.Exception.Message)" -ForegroundColor Red
    exit 1
}

Write-Host ""
Write-Host "2. Top 20 Tables by Buffer Pool Usage:" -ForegroundColor Yellow
Write-Host "--------------------------------------" -ForegroundColor Yellow

$tableQuery = @"
SELECT TOP 20
    OBJECT_SCHEMA_NAME(p.object_id) AS SchemaName,
    OBJECT_NAME(p.object_id) AS TableName,
    COUNT(*) * 8 / 1024 AS BufferPoolMB,
    COUNT(*) * 8 / 1024.0 / 1024 AS BufferPoolGB,
    CAST(COUNT(*) * 8 * 100.0 / NULLIF((SELECT COUNT(*) * 8 FROM sys.dm_os_buffer_descriptors WHERE database_id = DB_ID('$Database')), 0) AS DECIMAL(5,2)) AS PercentOfDatabase
FROM sys.dm_os_buffer_descriptors bd
INNER JOIN sys.allocation_units au ON bd.allocation_unit_id = au.allocation_unit_id
INNER JOIN sys.partitions p ON au.container_id = p.partition_id
WHERE bd.database_id = DB_ID('$Database')
    AND p.object_id > 100  -- Exclude system objects
GROUP BY p.object_id
ORDER BY BufferPoolMB DESC;
"@

try {
    $tables = Invoke-Sqlcmd -ServerInstance $ServerInstance -Database $Database -Query $tableQuery
    
    if ($tables) {
        foreach ($table in $tables) {
            $bufferMB = $table.BufferPoolMB
            $bufferGB = [math]::Round($table.BufferPoolGB, 2)
            $percent = $table.PercentOfDatabase
            $color = if ($bufferMB -gt 1000) { "Yellow" } elseif ($bufferMB -gt 100) { "Cyan" } else { "White" }
            Write-Host "  $($table.SchemaName).$($table.TableName): $bufferMB MB ($bufferGB GB) - $percent% of database" -ForegroundColor $color
        }
    } else {
        Write-Host "  No tables found in buffer pool" -ForegroundColor Yellow
    }
} catch {
    Write-Host "  Error: $($_.Exception.Message)" -ForegroundColor Red
}

Write-Host ""
Write-Host "3. Table Row Counts (Top 10 Largest Tables):" -ForegroundColor Yellow
Write-Host "--------------------------------------------" -ForegroundColor Yellow

$rowCountQuery = @"
SELECT TOP 10
    t.name AS TableName,
    s.name AS SchemaName,
    p.rows AS [RowCount],
    CAST(SUM(a.total_pages) * 8 / 1024.0 AS DECIMAL(10,2)) AS TotalSizeMB,
    CAST(SUM(a.used_pages) * 8 / 1024.0 AS DECIMAL(10,2)) AS UsedSizeMB
FROM sys.tables t
INNER JOIN sys.schemas s ON t.schema_id = s.schema_id
INNER JOIN sys.indexes i ON t.object_id = i.object_id
INNER JOIN sys.partitions p ON i.object_id = p.object_id AND i.index_id = p.index_id
INNER JOIN sys.allocation_units a ON p.partition_id = a.container_id
WHERE t.is_ms_shipped = 0
GROUP BY t.name, s.name, p.rows
ORDER BY p.rows DESC;
"@

try {
    $rowCounts = Invoke-Sqlcmd -ServerInstance $ServerInstance -Database $Database -Query $rowCountQuery
    
    if ($rowCounts) {
        foreach ($table in $rowCounts) {
            $rowCount = $table.RowCount
            $totalSizeMB = $table.TotalSizeMB
            $usedSizeMB = $table.UsedSizeMB
            Write-Host "  $($table.SchemaName).$($table.TableName): $rowCount rows, $usedSizeMB MB used ($totalSizeMB MB total)" -ForegroundColor White
        }
    }
} catch {
    Write-Host "  Error: $($_.Exception.Message)" -ForegroundColor Red
}

Write-Host ""
Write-Host "4. Check for Date Columns (Potential Optimization Opportunities):" -ForegroundColor Yellow
Write-Host "---------------------------------------------------------------" -ForegroundColor Yellow

$dateColumnQuery = @"
SELECT TOP 20
    t.name AS TableName,
    s.name AS SchemaName,
    c.name AS ColumnName,
    ty.name AS DataType
FROM sys.tables t
INNER JOIN sys.schemas s ON t.schema_id = s.schema_id
INNER JOIN sys.columns c ON t.object_id = c.object_id
INNER JOIN sys.types ty ON c.user_type_id = ty.user_type_id
WHERE t.is_ms_shipped = 0
    AND (
        c.name LIKE '%Date%' OR 
        c.name LIKE '%Time%' OR 
        c.name LIKE '%Created%' OR 
        c.name LIKE '%Updated%' OR
        c.name LIKE '%Download%' OR
        ty.name IN ('datetime', 'datetime2', 'date', 'smalldatetime')
    )
ORDER BY t.name, c.name;
"@

try {
    $dateColumns = Invoke-Sqlcmd -ServerInstance $ServerInstance -Database $Database -Query $dateColumnQuery
    
    if ($dateColumns) {
        Write-Host "  Tables with date/time columns (potential for date filtering):" -ForegroundColor White
        $currentTable = ""
        foreach ($col in $dateColumns) {
            if ($currentTable -ne "$($col.SchemaName).$($col.TableName)") {
                $currentTable = "$($col.SchemaName).$($col.TableName)"
                Write-Host ""
                Write-Host "    $currentTable" -ForegroundColor Cyan
            }
            Write-Host "      - $($col.ColumnName) ($($col.DataType))" -ForegroundColor White
        }
    } else {
        Write-Host "  No date/time columns found" -ForegroundColor Yellow
    }
} catch {
    Write-Host "  Error: $($_.Exception.Message)" -ForegroundColor Red
}

Write-Host ""
Write-Host "5. Recommendations:" -ForegroundColor Yellow
Write-Host "-------------------" -ForegroundColor Yellow
Write-Host "  - Review top tables by buffer pool usage above" -ForegroundColor White
Write-Host "  - Check if queries are loading all data without date filters" -ForegroundColor White
Write-Host "  - Consider adding date filters to queries (similar to AseScans fix)" -ForegroundColor White
Write-Host "  - Verify indexes exist on date columns for efficient filtering" -ForegroundColor White
Write-Host ""
Write-Host "========================================" -ForegroundColor Cyan
Write-Host "Analysis Complete" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan


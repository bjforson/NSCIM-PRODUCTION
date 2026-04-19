# Quick Index Usage Check Script
# Checks if date indexes are being used

param(
    [string]$Server = "127.0.0.1,1433",
    [string]$Database = "NS_CIS"
)

Write-Host "Checking Index Usage for ContainerScanQueues" -ForegroundColor Cyan
Write-Host "=============================================" -ForegroundColor Cyan
Write-Host ""

$query = @"
SELECT 
    i.name AS IndexName,
    s.user_seeks,
    s.user_scans,
    s.user_lookups,
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
WHERE DB_NAME(s.database_id) = '$Database'
    AND OBJECT_NAME(s.object_id) = 'ContainerScanQueues'
    AND i.name LIKE 'IX_ContainerScanQueues_%At%'
ORDER BY s.user_seeks DESC, s.user_scans DESC;
"@

try {
    $results = Invoke-Sqlcmd -ServerInstance $Server -Database $Database -Query $query
    
    if ($results -and $results.Count -gt 0) {
        Write-Host "Index Usage Statistics:" -ForegroundColor Yellow
        $results | Format-Table -AutoSize
        
        Write-Host ""
        
        $usedIndexes = $results | Where-Object { $_.user_seeks -gt 0 -or $_.user_scans -gt 0 }
        $unusedIndexes = $results | Where-Object { $_.user_seeks -eq 0 -and $_.user_scans -eq 0 }
        
        if ($usedIndexes) {
            Write-Host "✅ Indexes Being Used:" -ForegroundColor Green
            $usedIndexes | ForEach-Object {
                Write-Host "   - $($_.IndexName): $($_.user_seeks) seeks, $($_.user_scans) scans" -ForegroundColor Green
            }
        }
        
        if ($unusedIndexes) {
            Write-Host ""
            Write-Host "⚠️ Indexes Not Yet Used (may need more queries):" -ForegroundColor Yellow
            $unusedIndexes | ForEach-Object {
                Write-Host "   - $($_.IndexName)" -ForegroundColor Gray
            }
            Write-Host ""
            Write-Host "   Run more queries to populate usage statistics." -ForegroundColor Gray
        }
    } else {
        Write-Host "⚠️ No index usage data found." -ForegroundColor Yellow
        Write-Host "   This could mean:" -ForegroundColor Gray
        Write-Host "   1. SQL Server was restarted (usage stats reset)" -ForegroundColor Gray
        Write-Host "   2. No queries have used these indexes yet" -ForegroundColor Gray
        Write-Host ""
        Write-Host "   Run some queries first:" -ForegroundColor Cyan
        Write-Host "   .\scripts\Test-ContainerScanQueue.ps1 -Detailed" -ForegroundColor White
    }
} catch {
    Write-Host "❌ Error checking index usage: $($_.Exception.Message)" -ForegroundColor Red
}

Write-Host ""


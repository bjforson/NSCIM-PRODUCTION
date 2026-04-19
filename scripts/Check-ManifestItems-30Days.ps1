# Check how many ManifestItems are in the last 30 days
# Helps estimate memory reduction after the fix

param(
    [string]$ServerInstance = "127.0.0.1,1433",
    [string]$Database = "ICUMS_Downloads"
)

Write-Host "========================================" -ForegroundColor Cyan
Write-Host "ManifestItems 30-Day Analysis" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""

Write-Host "1. Total ManifestItems Statistics:" -ForegroundColor Yellow
Write-Host "----------------------------------" -ForegroundColor Yellow

$totalQuery = @"
SELECT 
    COUNT(*) AS TotalRows,
    COUNT(CASE WHEN CreatedAt >= DATEADD(DAY, -30, GETUTCDATE()) THEN 1 END) AS Last30Days,
    COUNT(CASE WHEN CreatedAt >= DATEADD(DAY, -7, GETUTCDATE()) THEN 1 END) AS Last7Days,
    MIN(CreatedAt) AS OldestRecord,
    MAX(CreatedAt) AS NewestRecord
FROM dbo.ManifestItems;
"@

try {
    $stats = Invoke-Sqlcmd -ServerInstance $ServerInstance -Database $Database -Query $totalQuery
    Write-Host "  Total ManifestItems: $($stats.TotalRows)" -ForegroundColor White
    Write-Host "  Records in last 30 days: $($stats.Last30Days)" -ForegroundColor White
    Write-Host "  Records in last 7 days: $($stats.Last7Days)" -ForegroundColor White
    Write-Host "  Oldest record: $($stats.OldestRecord)" -ForegroundColor White
    Write-Host "  Newest record: $($stats.NewestRecord)" -ForegroundColor White
    
    $percentRecent = if ($stats.TotalRows -gt 0) { ($stats.Last30Days / $stats.TotalRows) * 100 } else { 0 }
    Write-Host ""
    Write-Host "  Expected reduction: $([math]::Round($percentRecent, 2))% of data should be in buffer pool" -ForegroundColor $(if ($percentRecent -lt 20) { "Green" } else { "Yellow" })
    
    $currentGB = 7.00
    $estimatedGB = ($stats.Last30Days / $stats.TotalRows) * $currentGB
    Write-Host "  Current buffer pool: $currentGB GB" -ForegroundColor White
    Write-Host "  Estimated after fix: $([math]::Round($estimatedGB, 2)) GB" -ForegroundColor Green
    Write-Host "  Estimated reduction: $([math]::Round($currentGB - $estimatedGB, 2)) GB ($([math]::Round((1 - $estimatedGB / $currentGB) * 100, 1))%)" -ForegroundColor Green
} catch {
    Write-Host "  Error: $($_.Exception.Message)" -ForegroundColor Red
}

Write-Host ""
Write-Host "2. Check for CreatedAt Index:" -ForegroundColor Yellow
Write-Host "-----------------------------" -ForegroundColor Yellow

$indexQuery = @"
SELECT 
    i.name AS IndexName,
    i.type_desc AS IndexType,
    COL_NAME(ic.object_id, ic.column_id) AS ColumnName
FROM sys.indexes i
INNER JOIN sys.index_columns ic ON i.object_id = ic.object_id AND i.index_id = ic.index_id
WHERE i.object_id = OBJECT_ID('dbo.ManifestItems')
    AND COL_NAME(ic.object_id, ic.column_id) = 'CreatedAt'
ORDER BY i.name, ic.key_ordinal;
"@

try {
    $indexes = Invoke-Sqlcmd -ServerInstance $ServerInstance -Database $Database -Query $indexQuery
    if ($indexes) {
        Write-Host "  Date indexes found:" -ForegroundColor Green
        foreach ($idx in $indexes) {
            Write-Host "    - $($idx.IndexName) on $($idx.ColumnName) ($($idx.IndexType))" -ForegroundColor White
        }
    } else {
        Write-Host "  WARNING: No index found on CreatedAt!" -ForegroundColor Yellow
        Write-Host "    - Queries may still scan the entire table" -ForegroundColor Yellow
        Write-Host "    - Consider creating: CREATE INDEX IX_ManifestItems_CreatedAt ON ManifestItems(CreatedAt)" -ForegroundColor Yellow
    }
} catch {
    Write-Host "  Error: $($_.Exception.Message)" -ForegroundColor Red
}

Write-Host ""
Write-Host "========================================" -ForegroundColor Cyan
Write-Host "Analysis Complete" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan


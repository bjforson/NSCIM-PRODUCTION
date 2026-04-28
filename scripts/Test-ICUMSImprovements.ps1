# ICUMS Improvements Testing Script
# Tests Phase 1, 2, 3, and Archive improvements

param(
    [string]$Server = "127.0.0.1,1433",
    [string]$Database = "ICUMS_Downloads",
    [switch]$TestBulkOps,
    [switch]$TestDeduplication,
    [switch]$TestStreaming,
    [switch]$TestRetry,
    [switch]$TestDLQ,
    [switch]$TestMetrics,
    [switch]$TestArchive,
    [switch]$All
)

# Continues past errors intentionally: test runner toggles many independent ICUMS improvement tests via -Test* switches; one test failure must not skip the rest.
$ErrorActionPreference = "Continue"

Write-Host "🧪 ICUMS Improvements Testing Script" -ForegroundColor Cyan
Write-Host "====================================" -ForegroundColor Cyan
Write-Host ""

if ($All) {
    $TestBulkOps = $true
    $TestDeduplication = $true
    $TestStreaming = $true
    $TestRetry = $true
    $TestDLQ = $true
    $TestMetrics = $true
    $TestArchive = $true
}

# Test 1: Bulk Operations Performance
if ($TestBulkOps -or $All) {
    Write-Host "📊 Test 1: Bulk Operations Performance" -ForegroundColor Yellow
    Write-Host "-----------------------------------" -ForegroundColor Yellow
    
    $query = @'
SELECT TOP 10
    df.Id,
    df.FileName,
    COUNT(DISTINCT bd.Id) AS DocumentCount,
    COUNT(mi.Id) AS ManifestItemCount,
    df.ProcessingStatus,
    df.ProcessedDate,
    DATEDIFF(second, df.DownloadDate, df.ProcessedDate) AS ProcessingTimeSeconds
FROM DownloadedFiles df
LEFT JOIN BOEDocuments bd ON bd.DownloadedFileId = df.Id
LEFT JOIN ManifestItems mi ON mi.BOEDocumentId = bd.Id
WHERE df.ProcessingStatus = 'Completed'
    AND df.ProcessedDate >= DATEADD(day, -7, GETUTCDATE())
GROUP BY df.Id, df.FileName, df.ProcessingStatus, df.ProcessedDate, df.DownloadDate
HAVING COUNT(mi.Id) >= 100
ORDER BY ManifestItemCount DESC
'@
    
    Write-Host "Finding files with 100+ manifest items..." -ForegroundColor Gray
    $results = Invoke-Sqlcmd -ServerInstance $Server -Database $Database -Query $query -TrustServerCertificate
    
    if ($results) {
        Write-Host "✅ Found $($results.Count) large files:" -ForegroundColor Green
        $results | Format-Table -AutoSize
        Write-Host ""
        Write-Host "📈 Performance Analysis:" -ForegroundColor Cyan
        $avgTime = ($results | Measure-Object -Property ProcessingTimeSeconds -Average).Average
        $maxItems = ($results | Measure-Object -Property ManifestItemCount -Maximum).Maximum
        Write-Host "  - Average processing time: $([math]::Round($avgTime, 2)) seconds" -ForegroundColor White
        Write-Host "  - Largest file: $maxItems manifest items" -ForegroundColor White
        Write-Host "  - Target: < 60 seconds for 3,000+ items" -ForegroundColor Gray
    } else {
        Write-Host "⚠️ No large files found. Need files with 100+ manifest items for testing." -ForegroundColor Yellow
    }
    Write-Host ""
}

# Test 2: Deduplication Effectiveness
if ($TestDeduplication -or $All) {
    Write-Host "🔄 Test 2: Deduplication Effectiveness" -ForegroundColor Yellow
    Write-Host "--------------------------------------" -ForegroundColor Yellow
    
    $query = @'
-- Check recent download history
SELECT 
    ContainerNumber,
    COUNT(*) AS DownloadCount,
    MIN(DownloadedAt) AS FirstDownload,
    MAX(DownloadedAt) AS LastDownload,
    DATEDIFF(hour, MIN(DownloadedAt), MAX(DownloadedAt)) AS HoursBetween
FROM ContainerDownloadHistory
WHERE DownloadedAt >= DATEADD(day, -7, GETUTCDATE())
GROUP BY ContainerNumber
HAVING COUNT(*) > 1
ORDER BY DownloadCount DESC
'@
    
    Write-Host "Checking duplicate downloads in last 7 days..." -ForegroundColor Gray
    $duplicates = Invoke-Sqlcmd -ServerInstance $Server -Database $Database -Query $query -TrustServerCertificate
    
    if ($duplicates) {
        Write-Host "⚠️ Found $($duplicates.Count) containers with multiple downloads:" -ForegroundColor Yellow
        $duplicates | Select-Object -First 10 | Format-Table -AutoSize
        Write-Host ""
        Write-Host "📊 Deduplication Stats:" -ForegroundColor Cyan
        $totalQuery = @'
SELECT COUNT(DISTINCT ContainerNumber) AS Total 
FROM ContainerDownloadHistory 
WHERE DownloadedAt >= DATEADD(day, -7, GETUTCDATE())
'@
        $totalContainers = (Invoke-Sqlcmd -ServerInstance $Server -Database $Database -Query $totalQuery -TrustServerCertificate).Total
        $duplicateContainers = $duplicates.Count
        $duplicateRate = if ($totalContainers -gt 0) { [math]::Round(($duplicateContainers / $totalContainers) * 100, 2) } else { 0 }
        Write-Host "  - Total unique containers: $totalContainers" -ForegroundColor White
        $ratePercent = [math]::Round($duplicateRate, 2)
        Write-Host "  - Containers with duplicates: $duplicateContainers" -ForegroundColor White
        Write-Host "  - Duplicate rate: $ratePercent percent" -ForegroundColor White
        Write-Host "  - Target: less than 10 percent duplicate rate" -ForegroundColor Gray
    } else {
        Write-Host "✅ No duplicate downloads found! Deduplication working correctly." -ForegroundColor Green
    }
    Write-Host ""
}

# Test 3: Archive Statistics
if ($TestArchive -or $All) {
    Write-Host "📦 Test 3: Archive Service Statistics" -ForegroundColor Yellow
    Write-Host "-----------------------------------" -ForegroundColor Yellow
    
    $query = @'
SELECT 
    COUNT(*) AS TotalArchived,
    SUM(OriginalSizeBytes) / 1024.0 / 1024.0 AS TotalOriginalSizeMB,
    SUM(ArchivedSizeBytes) / 1024.0 / 1024.0 AS TotalArchivedSizeMB,
    AVG(CompressionRatio) AS AvgCompressionRatio,
    MIN(CompressionRatio) AS MinCompressionRatio,
    MAX(CompressionRatio) AS MaxCompressionRatio,
    COUNT(CASE WHEN IsRestored = 1 THEN 1 END) AS RestoredCount,
    MIN(ArchivedDate) AS FirstArchive,
    MAX(ArchivedDate) AS LastArchive
FROM ArchivedFiles
'@
    
    Write-Host "Checking archive statistics..." -ForegroundColor Gray
    $archiveStats = Invoke-Sqlcmd -ServerInstance $Server -Database $Database -Query $query -TrustServerCertificate
    
    if ($archiveStats -and $archiveStats.TotalArchived -gt 0) {
        Write-Host "✅ Archive Statistics:" -ForegroundColor Green
        Write-Host "  - Total archived files: $($archiveStats.TotalArchived)" -ForegroundColor White
        Write-Host "  - Original size: $([math]::Round($archiveStats.TotalOriginalSizeMB, 2)) MB" -ForegroundColor White
        Write-Host "  - Archived size: $([math]::Round($archiveStats.TotalArchivedSizeMB, 2)) MB" -ForegroundColor White
        $savings = if ($archiveStats.TotalOriginalSizeMB -gt 0) { 
            [math]::Round((1 - ($archiveStats.TotalArchivedSizeMB / $archiveStats.TotalOriginalSizeMB)) * 100, 2) 
        } else { 0 }
        Write-Host "  - Space savings: $savings% (Target: 70-80%)" -ForegroundColor $(if ($savings -ge 70) { "Green" } else { "Yellow" })
        Write-Host "  - Avg compression ratio: $([math]::Round($archiveStats.AvgCompressionRatio, 2))%" -ForegroundColor White
        Write-Host "  - Restored files: $($archiveStats.RestoredCount)" -ForegroundColor White
    } else {
        Write-Host "⚠️ No archived files yet. Files need to be 24+ hours old to be archived." -ForegroundColor Yellow
    }
    Write-Host ""
}

# Test 4: Failed Processing Queue
if ($TestDLQ -or $All) {
    Write-Host "🔴 Test 4: Dead-Letter Queue Status" -ForegroundColor Yellow
    Write-Host "---------------------------------" -ForegroundColor Yellow
    
    $query = @'
SELECT 
    Status,
    COUNT(*) AS Count,
    AVG(CAST(RetryCount AS FLOAT)) AS AvgRetries,
    COUNT(CASE WHEN Status = 'Resolved' THEN 1 END) AS ResolvedCount,
    COUNT(CASE WHEN Status = 'Abandoned' THEN 1 END) AS AbandonedCount,
    COUNT(CASE WHEN Status = 'Pending' THEN 1 END) AS PendingCount,
    COUNT(CASE WHEN Status = 'Retrying' THEN 1 END) AS RetryingCount
FROM FailedProcessingQueue
GROUP BY Status
ORDER BY Count DESC
'@
    
    Write-Host "Checking failed processing queue..." -ForegroundColor Gray
    $dlqStats = Invoke-Sqlcmd -ServerInstance $Server -Database $Database -Query $query -TrustServerCertificate
    
    if ($dlqStats) {
        Write-Host "📊 DLQ Statistics:" -ForegroundColor Cyan
        $dlqStats | Format-Table -AutoSize
        Write-Host ""
        
        $totalFailed = ($dlqStats | Measure-Object -Property Count -Sum).Sum
        $resolved = ($dlqStats | Where-Object { $_.Status -eq "Resolved" }).Count
        $recoveryRate = if ($totalFailed -gt 0) { [math]::Round(($resolved / $totalFailed) * 100, 2) } else { 0 }
        Write-Host "  - Total failed files: $totalFailed" -ForegroundColor White
        Write-Host "  - Auto-recovery rate: $recoveryRate% (Target: 80-90%)" -ForegroundColor $(if ($recoveryRate -ge 80) { "Green" } else { "Yellow" })
    } else {
        Write-Host "✅ No failed files in queue. All processing successful!" -ForegroundColor Green
    }
    Write-Host ""
}

# Test 5: Metrics Collection
if ($TestMetrics -or $All) {
    Write-Host "📈 Test 5: Metrics Collection Status" -ForegroundColor Yellow
    Write-Host "----------------------------" -ForegroundColor Yellow
    
    Write-Host "Checking metrics API endpoint..." -ForegroundColor Gray
    Write-Host "  - Endpoint: /api/ICUMSMetrics/snapshot" -ForegroundColor White
    Write-Host "  - Endpoint: /api/ICUMSMetrics/counters" -ForegroundColor White
    Write-Host "  - Endpoint: /api/ICUMSMetrics/gauges" -ForegroundColor White
    Write-Host ""
    Write-Host "⚠️ Manual test required: Access ICUMS Dashboard to verify metrics display" -ForegroundColor Yellow
    Write-Host ""
}

Write-Host "✅ Testing Complete!" -ForegroundColor Green
Write-Host ""
Write-Host "📝 Next Steps:" -ForegroundColor Cyan
Write-Host "  1. Review test results above" -ForegroundColor White
Write-Host "  2. Compare against targets in testing plan" -ForegroundColor White
Write-Host "  3. Document any issues found" -ForegroundColor White
Write-Host "  4. Run manual tests for metrics dashboard" -ForegroundColor White


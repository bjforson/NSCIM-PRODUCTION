# Diagnostic Script for Container Scan Queue Processing
# Checks service status, queue statistics, and identifies potential issues

Write-Host "==========================================" -ForegroundColor Cyan
Write-Host "Container Scan Queue Processing Diagnostic" -ForegroundColor Cyan
Write-Host "==========================================" -ForegroundColor Cyan
Write-Host ""

# Check if SQL Server module is available
$sqlModule = Get-Module -ListAvailable -Name SqlServer
if (-not $sqlModule) {
    Write-Host "⚠️  SQL Server PowerShell module not found. Install with: Install-Module -Name SqlServer" -ForegroundColor Yellow
    Write-Host "Continuing with basic checks..." -ForegroundColor Yellow
    $useSqlModule = $false
} else {
    $useSqlModule = $true
    Import-Module SqlServer -ErrorAction SilentlyContinue
}

# Connection string - UPDATE THIS with your actual connection string
$connectionString = "Server=localhost;Database=NS_CIS;Integrated Security=True;TrustServerCertificate=True;"

Write-Host "📊 Queue Status Analysis" -ForegroundColor Green
Write-Host "------------------------" -ForegroundColor Green

try {
    if ($useSqlModule) {
        # Query 1: Overall queue status
        Write-Host "`n1. Queue Status Breakdown:" -ForegroundColor Yellow
        $statusQuery = @"
SELECT 
    Status,
    COUNT(*) as Count,
    MIN(QueuedAt) as OldestQueuedAt,
    MAX(QueuedAt) as NewestQueuedAt,
    AVG(CAST(RetryCount as float)) as AvgRetryCount,
    MAX(RetryCount) as MaxRetryCount,
    SUM(CASE WHEN RetryCount >= MaxRetries THEN 1 ELSE 0 END) as ExceededMaxRetries
FROM ContainerScanQueues
GROUP BY Status
ORDER BY Status;
"@
        
        $statusResults = Invoke-Sqlcmd -ConnectionString $connectionString -Query $statusQuery -ErrorAction Stop
        $statusResults | Format-Table -AutoSize
        
        # Query 2: Pending items analysis
        Write-Host "`n2. Pending Items Analysis:" -ForegroundColor Yellow
        $pendingQuery = @"
SELECT 
    COUNT(*) as TotalPending,
    SUM(CASE WHEN RetryCount >= MaxRetries THEN 1 ELSE 0 END) as ExceededMaxRetries,
    SUM(CASE WHEN RetryCount < MaxRetries THEN 1 ELSE 0 END) as EligibleForProcessing,
    MIN(QueuedAt) as OldestPendingQueuedAt,
    MAX(QueuedAt) as NewestPendingQueuedAt,
    AVG(CAST(RetryCount as float)) as AvgRetryCount,
    MAX(RetryCount) as MaxRetryCount,
    AVG(DATEDIFF(MINUTE, QueuedAt, GETUTCDATE())) as AvgWaitMinutes
FROM ContainerScanQueues
WHERE Status = 'Pending';
"@
        
        $pendingResults = Invoke-Sqlcmd -ConnectionString $connectionString -Query $pendingQuery -ErrorAction Stop
        $pendingResults | Format-Table -AutoSize
        
        # Query 3: Processing items (should be 0 or very few)
        Write-Host "`n3. Processing Items (Stuck Check):" -ForegroundColor Yellow
        $processingQuery = @"
SELECT 
    COUNT(*) as ProcessingCount,
    MIN(ProcessedAt) as OldestProcessingSince,
    MAX(ProcessedAt) as NewestProcessingSince,
    AVG(DATEDIFF(MINUTE, ProcessedAt, GETUTCDATE())) as AvgMinutesStuck
FROM ContainerScanQueues
WHERE Status = 'Processing';
"@
        
        $processingResults = Invoke-Sqlcmd -ConnectionString $connectionString -Query $processingQuery -ErrorAction Stop
        $processingResults | Format-Table -AutoSize
        
        # Query 4: Recent completions
        Write-Host "`n4. Recent Completions (Last 24 Hours):" -ForegroundColor Yellow
        $completionsQuery = @"
SELECT 
    COUNT(*) as RecentCompletions,
    MIN(CompletedAt) as FirstCompletion,
    MAX(CompletedAt) as LastCompletion
FROM ContainerScanQueues
WHERE Status = 'Completed'
AND CompletedAt >= DATEADD(HOUR, -24, GETUTCDATE());
"@
        
        $completionsResults = Invoke-Sqlcmd -ConnectionString $connectionString -Query $completionsQuery -ErrorAction Stop
        $completionsResults | Format-Table -AutoSize
        
        # Query 5: Eligible items (should match pending if service is working)
        Write-Host "`n5. Eligible Items for Processing:" -ForegroundColor Yellow
        $eligibleQuery = @"
SELECT 
    COUNT(*) as EligibleCount,
    MIN(QueuedAt) as OldestQueuedAt,
    MAX(QueuedAt) as NewestQueuedAt,
    AVG(CAST(Priority as float)) as AvgPriority
FROM ContainerScanQueues
WHERE Status = 'Pending'
AND RetryCount < MaxRetries;
"@
        
        $eligibleResults = Invoke-Sqlcmd -ConnectionString $connectionString -Query $eligibleQuery -ErrorAction Stop
        $eligibleResults | Format-Table -AutoSize
        
    } else {
        Write-Host "⚠️  SQL queries skipped - SQL Server module not available" -ForegroundColor Yellow
        Write-Host "Run the queries manually from Check-QueueItems.sql" -ForegroundColor Yellow
    }
}
catch {
    Write-Host "❌ Error running SQL queries: $($_.Exception.Message)" -ForegroundColor Red
    Write-Host "Please check your connection string and database access" -ForegroundColor Yellow
}

Write-Host "`n📋 Diagnostic Analysis" -ForegroundColor Green
Write-Host "------------------------" -ForegroundColor Green

if ($useSqlModule -and $pendingResults) {
    $totalPending = [int]$pendingResults.TotalPending
    $eligible = [int]$pendingResults.EligibleForProcessing
    $processingCount = if ($processingResults) { [int]$processingResults.ProcessingCount } else { 0 }
    $recentCompletions = if ($completionsResults) { [int]$completionsResults.RecentCompletions } else { 0 }
    
    Write-Host "`nFindings:" -ForegroundColor Cyan
    
    if ($totalPending -eq 0) {
        Write-Host "✅ No pending items in queue - Queue is clear!" -ForegroundColor Green
    }
    elseif ($eligible -eq 0 -and $totalPending -gt 0) {
        Write-Host "⚠️  ALL pending items have exceeded max retries!" -ForegroundColor Yellow
        Write-Host "   All items have RetryCount >= MaxRetries" -ForegroundColor Yellow
        Write-Host "   Items will NOT be processed automatically" -ForegroundColor Yellow
        Write-Host "   Solution: Reset RetryCount or increase MaxRetries" -ForegroundColor Yellow
    }
    elseif ($eligible -gt 0 -and $recentCompletions -eq 0) {
        Write-Host "❌ PROBLEM: $eligible eligible items but NO completions in last 24 hours!" -ForegroundColor Red
        Write-Host "   This indicates the service is NOT processing the queue" -ForegroundColor Red
        Write-Host "   Possible causes:" -ForegroundColor Yellow
        Write-Host "   1. Service is not running" -ForegroundColor Yellow
        Write-Host "   2. Service is not calling GetNextBatchAsync" -ForegroundColor Yellow
        Write-Host "   3. Service is encountering errors" -ForegroundColor Yellow
        Write-Host "   Action: Check application logs for service activity" -ForegroundColor Yellow
    }
    elseif ($processingCount -gt 10) {
        Write-Host "⚠️  WARNING: $processingCount items stuck in Processing status!" -ForegroundColor Yellow
        Write-Host "   Items may be stuck from a service crash/restart" -ForegroundColor Yellow
        Write-Host "   Recovery service should reset these after 30 minutes" -ForegroundColor Yellow
    }
    elseif ($eligible -gt 0 -and $recentCompletions -gt 0) {
        Write-Host "✅ Service appears to be working!" -ForegroundColor Green
        Write-Host "   $eligible eligible items, $recentCompletions completed in last 24h" -ForegroundColor Green
    }
    
    # Check RetryCount
    if ($pendingResults.AvgRetryCount -eq 0 -and $totalPending -gt 0) {
        Write-Host "`n⚠️  CRITICAL: All pending items have RetryCount = 0" -ForegroundColor Red
        Write-Host "   This means MarkAsProcessingAsync has NEVER been called!" -ForegroundColor Red
        Write-Host "   Items are not being picked up by the service at all" -ForegroundColor Red
        Write-Host "   This strongly suggests the service is not running or not processing queue" -ForegroundColor Red
    }
}

Write-Host "`n📝 Next Steps" -ForegroundColor Green
Write-Host "------------------------" -ForegroundColor Green
Write-Host "1. Check application logs for:" -ForegroundColor Cyan
Write-Host "   - '[CONTAINER-COMPLETENESS] Container Completeness Service started'" -ForegroundColor White
Write-Host "   - '[CONTAINER-COMPLETENESS] 📥 STEP 1: Consuming scans from ContainerScanQueue...'" -ForegroundColor White
Write-Host "   - '[CONTAINER-SCAN-QUEUE] Retrieved {Count} items from queue'" -ForegroundColor White
Write-Host "   - Any error messages or exceptions" -ForegroundColor White
Write-Host ""
Write-Host "2. Verify service is running:" -ForegroundColor Cyan
Write-Host "   - Check if the application is running" -ForegroundColor White
Write-Host "   - Check if ContainerCompletenessService is enabled" -ForegroundColor White
Write-Host ""
Write-Host "3. If service is running but not processing:" -ForegroundColor Cyan
Write-Host "   - Check for database connection issues" -ForegroundColor White
Write-Host "   - Check for exceptions in logs" -ForegroundColor White
Write-Host "   - Verify GetNextBatchAsync is being called" -ForegroundColor White
Write-Host ""
Write-Host "Log locations:" -ForegroundColor Cyan
Write-Host "   - Application logs: Check your logging configuration" -ForegroundColor White
Write-Host "   - Typically in: logs/nickscan-{date}.txt" -ForegroundColor White

Write-Host "`n==========================================" -ForegroundColor Cyan
Write-Host "Diagnostic Complete" -ForegroundColor Cyan
Write-Host "==========================================" -ForegroundColor Cyan


# Test Container Scan Queue - Automated Testing & Monitoring Script
# Tests queue health, processing rates, and provides diagnostics

param(
    [string]$Server = "127.0.0.1,1433",
    [string]$Database = "NS_CIS",
    [switch]$Detailed,
    [switch]$Watch
)

# Continues past errors intentionally: monitoring/test runner loops over independent queue health queries (and supports -Watch continuous mode); per-query errors must not abort the run.
$ErrorActionPreference = "Continue"

Write-Host "Container Scan Queue Testing & Monitoring Tool" -ForegroundColor Cyan
Write-Host "=================================================" -ForegroundColor Cyan
Write-Host ""

# Test 1: Basic Queue Status
Write-Host "Test 1: Queue Status Overview" -ForegroundColor Yellow
Write-Host "------------------------------" -ForegroundColor Yellow

$statusQuery = @"
SELECT 
    Status,
    COUNT(*) AS Count,
    AVG(DATEDIFF(SECOND, QueuedAt, ISNULL(ProcessedAt, GETUTCDATE()))) AS AvgWaitSeconds,
    MAX(DATEDIFF(SECOND, QueuedAt, ISNULL(ProcessedAt, GETUTCDATE()))) AS MaxWaitSeconds
FROM ContainerScanQueues
WHERE QueuedAt >= DATEADD(DAY, -1, GETUTCDATE())
GROUP BY Status
ORDER BY Status;
"@

try {
    $status = Invoke-Sqlcmd -ServerInstance $Server -Database $Database -Query $statusQuery
    if ($status) {
        $status | Format-Table -AutoSize
    } else {
        Write-Host "   No queue items found" -ForegroundColor Yellow
    }
} catch {
    Write-Host "   Error: $($_.Exception.Message)" -ForegroundColor Red
}

Write-Host ""

# Test 2: Queue by Scanner Type
Write-Host "Test 2: Queue by Scanner Type" -ForegroundColor Yellow
Write-Host "------------------------------" -ForegroundColor Yellow

$scannerQuery = @"
SELECT 
    ScannerType,
    Status,
    COUNT(*) AS Count
FROM ContainerScanQueues
WHERE QueuedAt >= DATEADD(DAY, -1, GETUTCDATE())
GROUP BY ScannerType, Status
ORDER BY ScannerType, Status;
"@

try {
    $scannerStats = Invoke-Sqlcmd -ServerInstance $Server -Database $Database -Query $scannerQuery 
    if ($scannerStats) {
        $scannerStats | Format-Table -AutoSize
    } else {
        Write-Host "   No queue items found" -ForegroundColor Yellow
    }
} catch {
    Write-Host "   Error: $($_.Exception.Message)" -ForegroundColor Red
}

Write-Host ""

# Test 3: Recent Queue Items
Write-Host "Test 3: Recent Queue Items (Last 10)" -ForegroundColor Yellow
Write-Host "------------------------------" -ForegroundColor Yellow

$recentQuery = @"
SELECT TOP 10
    Id,
    ContainerNumber,
    ScannerType,
    InspectionId,
    Status,
    Priority,
    RetryCount,
    DATEDIFF(SECOND, QueuedAt, ISNULL(ProcessedAt, GETUTCDATE())) AS WaitSeconds,
    QueuedAt,
    ProcessedAt,
    CompletedAt
FROM ContainerScanQueues
WHERE CreatedAt >= DATEADD(DAY, -1, GETUTCDATE())
ORDER BY CreatedAt DESC;
"@

try {
    $recent = Invoke-Sqlcmd -ServerInstance $Server -Database $Database -Query $recentQuery 
    if ($recent) {
        $recent | Format-Table -AutoSize
    } else {
        Write-Host "   No queue items found" -ForegroundColor Yellow
    }
} catch {
    Write-Host "   Error: $($_.Exception.Message)" -ForegroundColor Red
}

Write-Host ""

# Test 4: Processing Rate (Last Hour)
Write-Host "Test 4: Processing Rate (Last Hour)" -ForegroundColor Yellow
Write-Host "------------------------------" -ForegroundColor Yellow

$processingQuery = @"
SELECT 
    COUNT(*) AS ItemsProcessed,
    AVG(DATEDIFF(SECOND, QueuedAt, CompletedAt)) AS AvgProcessingTimeSeconds,
    MIN(DATEDIFF(SECOND, QueuedAt, CompletedAt)) AS MinProcessingTimeSeconds,
    MAX(DATEDIFF(SECOND, QueuedAt, CompletedAt)) AS MaxProcessingTimeSeconds
FROM ContainerScanQueues
WHERE CompletedAt >= DATEADD(HOUR, -1, GETUTCDATE())
    AND Status = 'Completed';
"@

try {
    $processing = Invoke-Sqlcmd -ServerInstance $Server -Database $Database -Query $processingQuery 
    if ($processing -and $processing.ItemsProcessed -gt 0) {
        Write-Host "  SUCCESS: Items Processed: $($processing.ItemsProcessed)" -ForegroundColor Green
        Write-Host "  Avg Processing Time: $([math]::Round([double]$processing.AvgProcessingTimeSeconds, 2)) seconds" -ForegroundColor Cyan
        Write-Host "  Min Processing Time: $($processing.MinProcessingTimeSeconds) seconds" -ForegroundColor Cyan
        Write-Host "  Max Processing Time: $($processing.MaxProcessingTimeSeconds) seconds" -ForegroundColor Cyan
    } else {
        Write-Host "  WARNING: No items processed in the last hour" -ForegroundColor Yellow
    }
} catch {
    Write-Host "   Error: $($_.Exception.Message)" -ForegroundColor Red
}

Write-Host ""

# Test 5: Failed Items
Write-Host "Test 5: Failed Items (Needing Attention)" -ForegroundColor Yellow
Write-Host "------------------------------" -ForegroundColor Yellow

$failedQuery = @"
SELECT TOP 20
    Id,
    ContainerNumber,
    ScannerType,
    InspectionId,
    RetryCount,
    MaxRetries,
    LEFT(ErrorMessage, 100) AS ErrorMessage,
    CreatedAt
FROM ContainerScanQueues
WHERE Status = 'Failed'
    AND CreatedAt >= DATEADD(DAY, -2, GETUTCDATE())
ORDER BY CreatedAt DESC;
"@

try {
    $failed = Invoke-Sqlcmd -ServerInstance $Server -Database $Database -Query $failedQuery 
    if ($failed) {
        Write-Host "  WARNING: Found $($failed.Count) failed items" -ForegroundColor Red
        if ($Detailed) {
            $failed | Format-Table -AutoSize
        }
    } else {
        Write-Host "  SUCCESS: No failed items" -ForegroundColor Green
    }
} catch {
    Write-Host "   Error: $($_.Exception.Message)" -ForegroundColor Red
}

Write-Host ""

# Test 6: Stuck Items (Processing >30 minutes)
Write-Host "Test 6: Stuck Items (Processing >30 minutes)" -ForegroundColor Yellow
Write-Host "------------------------------" -ForegroundColor Yellow

$stuckQuery = @"
SELECT 
    Id,
    ContainerNumber,
    ScannerType,
    DATEDIFF(MINUTE, ProcessedAt, GETUTCDATE()) AS StuckMinutes,
    ProcessedAt
FROM ContainerScanQueues
WHERE Status = 'Processing'
    AND ProcessedAt < DATEADD(MINUTE, -30, GETUTCDATE());
"@

try {
    $stuck = Invoke-Sqlcmd -ServerInstance $Server -Database $Database -Query $stuckQuery 
    if ($stuck) {
        Write-Host "  WARNING: Found $($stuck.Count) stuck items" -ForegroundColor Red
        if ($Detailed) {
            $stuck | Format-Table -AutoSize
        }
    } else {
        Write-Host "  SUCCESS: No stuck items" -ForegroundColor Green
    }
} catch {
    Write-Host "   Error: $($_.Exception.Message)" -ForegroundColor Red
}

Write-Host ""

# Test 7: Queue Depth & Health
Write-Host "Test 7: Queue Health Summary" -ForegroundColor Yellow
Write-Host "------------------------------" -ForegroundColor Yellow

$healthQuery = @"
SELECT 
    (SELECT COUNT(*) FROM ContainerScanQueues WHERE Status = 'Pending' AND QueuedAt >= DATEADD(DAY, -1, GETUTCDATE())) AS PendingCount,
    (SELECT COUNT(*) FROM ContainerScanQueues WHERE Status = 'Processing' AND QueuedAt >= DATEADD(DAY, -1, GETUTCDATE())) AS ProcessingCount,
    (SELECT COUNT(*) FROM ContainerScanQueues WHERE Status = 'Completed' AND QueuedAt >= DATEADD(DAY, -1, GETUTCDATE())) AS CompletedCount,
    (SELECT COUNT(*) FROM ContainerScanQueues WHERE Status = 'Failed' AND QueuedAt >= DATEADD(DAY, -1, GETUTCDATE())) AS FailedCount,
    (SELECT COUNT(*) FROM ContainerScanQueues WHERE Status = 'Pending' AND DATEDIFF(MINUTE, QueuedAt, GETUTCDATE()) > 5 AND QueuedAt >= DATEADD(DAY, -1, GETUTCDATE())) AS OldPendingCount,
    (SELECT AVG(DATEDIFF(SECOND, QueuedAt, ISNULL(ProcessedAt, GETUTCDATE()))) FROM ContainerScanQueues WHERE Status = 'Pending' AND QueuedAt >= DATEADD(DAY, -1, GETUTCDATE())) AS AvgWaitSeconds;
"@

try {
    $health = Invoke-Sqlcmd -ServerInstance $Server -Database $Database -Query $healthQuery 
    if ($health) {
    Write-Host "  Pending: $($health.PendingCount)" -ForegroundColor $(if ($health.PendingCount -gt 1000) { "Red" } elseif ($health.PendingCount -gt 500) { "Yellow" } else { "Green" })
    Write-Host "  Processing: $($health.ProcessingCount)" -ForegroundColor Cyan
    Write-Host "  Completed: $($health.CompletedCount)" -ForegroundColor Green
    Write-Host "  Failed: $($health.FailedCount)" -ForegroundColor $(if ($health.FailedCount -gt 0) { "Red" } else { "Green" })
    Write-Host "  Old Pending (>5 min): $($health.OldPendingCount)" -ForegroundColor $(if ($health.OldPendingCount -gt 0) { "Yellow" } else { "Green" })
        if ($health.AvgWaitSeconds -and $health.AvgWaitSeconds -ne [DBNull]::Value) {
            Write-Host "   Avg Wait Time: $([math]::Round([double]$health.AvgWaitSeconds, 2)) seconds" -ForegroundColor Cyan
        }
        
        # Health assessment
        Write-Host ""
        if ($health.PendingCount -gt 1000) {
        Write-Host "  WARNING: Queue depth is high (>1000 pending)" -ForegroundColor Red
    } elseif ($health.OldPendingCount -gt 0) {
        Write-Host "  WARNING: Some items have been pending for >5 minutes" -ForegroundColor Yellow
    } elseif ($health.FailedCount -gt 10) {
        Write-Host "  WARNING: Multiple failed items need attention" -ForegroundColor Yellow
    } else {
        Write-Host "  SUCCESS: Queue health: GOOD" -ForegroundColor Green
        }
    }
} catch {
    Write-Host "   Error: $($_.Exception.Message)" -ForegroundColor Red
}

Write-Host ""

# Detailed Test: Recent Completeness Records (if requested)
if ($Detailed) {
    Write-Host "Detailed: Recent Completeness Records (Last 10)" -ForegroundColor Yellow
    Write-Host "------------------------------" -ForegroundColor Yellow
    
    $completenessQuery = @"
    SELECT TOP 10
        ContainerNumber,
        ScannerType,
        InspectionId,
        Status,
        HasScannerData,
        HasICUMSData,
        HasImageData,
        CreatedAt
    FROM ContainerCompletenessStatuses
    WHERE CreatedAt >= DATEADD(DAY, -1, GETUTCDATE())
    ORDER BY CreatedAt DESC;
"@
    
    try {
        $completeness = Invoke-Sqlcmd -ServerInstance $Server -Database $Database -Query $completenessQuery 
        if ($completeness) {
            $completeness | Format-Table -AutoSize
        }
    } catch {
        Write-Host "  ERROR: $($_.Exception.Message)" -ForegroundColor Red
    }
    
    Write-Host ""
}

# Watch mode
if ($Watch) {
    Write-Host "Watch Mode: Refreshing every 10 seconds (Ctrl+C to stop)" -ForegroundColor Cyan
    Write-Host ""
    
    while ($true) {
        Clear-Host
        Write-Host "Container Scan Queue - Live Monitor" -ForegroundColor Cyan
        Write-Host "=====================================" -ForegroundColor Cyan
        Write-Host "Last Update: $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')" -ForegroundColor Gray
        Write-Host ""
        
        try {
            $health = Invoke-Sqlcmd -ServerInstance $Server -Database $Database -Query $healthQuery 
            if ($health) {
                Write-Host "  Pending: $($health.PendingCount)" -ForegroundColor $(if ($health.PendingCount -gt 1000) { "Red" } elseif ($health.PendingCount -gt 500) { "Yellow" } else { "Green" })
                Write-Host "  Processing: $($health.ProcessingCount)" -ForegroundColor Cyan
                Write-Host "  Completed: $($health.CompletedCount)" -ForegroundColor Green
                Write-Host "  Failed: $($health.FailedCount)" -ForegroundColor $(if ($health.FailedCount -gt 0) { "Red" } else { "Green" })
                if ($health.AvgWaitSeconds -and $health.AvgWaitSeconds -ne [DBNull]::Value) {
                    Write-Host "  Avg Wait: $([math]::Round([double]$health.AvgWaitSeconds, 2))s" -ForegroundColor Cyan
                }
            }
        } catch {
            Write-Host "  ERROR: $($_.Exception.Message)" -ForegroundColor Red
        }
        
        Start-Sleep -Seconds 10
    }
}

Write-Host ""
Write-Host "Testing Complete" -ForegroundColor Green
Write-Host ""



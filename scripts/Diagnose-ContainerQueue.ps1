# Diagnostic script to check Container Scan Queue status
# This script helps diagnose why queue items aren't being processed

Write-Host "=== Container Scan Queue Diagnostic Script ===" -ForegroundColor Cyan
Write-Host ""

# Check API endpoint for queue statistics
Write-Host "1. Checking queue statistics from API..." -ForegroundColor Yellow
try {
    $stats = Invoke-RestMethod -Uri "http://10.0.1.254:5205/api/QueueHealth/statistics" -Method Get
    Write-Host "   Total Pending: $($stats.Statistics.TotalPending)" -ForegroundColor Green
    Write-Host "   Total Processing: $($stats.Statistics.TotalProcessing)" -ForegroundColor Green
    Write-Host "   Total Completed: $($stats.Statistics.TotalCompleted)" -ForegroundColor Green
    Write-Host "   Total Failed: $($stats.Statistics.TotalFailed)" -ForegroundColor Green
    Write-Host "   Average Wait Time: $($stats.Statistics.AverageWaitTimeMinutes) minutes" -ForegroundColor Green
    Write-Host "   Oldest Pending: $($stats.Statistics.OldestPendingQueuedAt)" -ForegroundColor Green
    Write-Host ""
}
catch {
    Write-Host "   ERROR: Could not retrieve queue statistics" -ForegroundColor Red
    Write-Host "   Error: $($_.Exception.Message)" -ForegroundColor Red
    Write-Host ""
}

# Instructions for manual database check
Write-Host "2. To check queue items directly in SQL Server, run this query:" -ForegroundColor Yellow
Write-Host ""
Write-Host "   SELECT TOP 20" -ForegroundColor White
Write-Host "       Status," -ForegroundColor White
Write-Host "       RetryCount," -ForegroundColor White
Write-Host "       MaxRetries," -ForegroundColor White
Write-Host "       ContainerNumber," -ForegroundColor White
Write-Host "       ScannerType," -ForegroundColor White
Write-Host "       QueuedAt," -ForegroundColor White
Write-Host "       CASE WHEN RetryCount >= MaxRetries THEN 'EXCEEDED' ELSE 'OK' END AS RetryStatus" -ForegroundColor White
Write-Host "   FROM ContainerScanQueues" -ForegroundColor White
Write-Host "   WHERE Status = 'Pending'" -ForegroundColor White
Write-Host "   ORDER BY QueuedAt" -ForegroundColor White
Write-Host ""

Write-Host "3. Key Findings:" -ForegroundColor Yellow
Write-Host "   - GetNextBatchAsync filters by: Status='Pending' AND RetryCount < MaxRetries" -ForegroundColor White
Write-Host "   - If all items have RetryCount >= MaxRetries (default 3), they won't be retrieved" -ForegroundColor White
Write-Host "   - Service logs show 'STEP 1: Consuming scans from ContainerScanQueue...' but no items retrieved" -ForegroundColor White
Write-Host ""

Write-Host "4. Potential Issues:" -ForegroundColor Yellow
Write-Host "   a) All 105 items may have exceeded max retries (RetryCount >= 3)" -ForegroundColor White
Write-Host "   b) Service registration conflict: Both ContainerCompletenessService and" -ForegroundColor White
Write-Host "      ContainerCompletenessOrchestratorService are registered as hosted services" -ForegroundColor White
Write-Host "   c) Queue items may need manual reset (set RetryCount = 0 or increase MaxRetries)" -ForegroundColor White
Write-Host ""

Write-Host "5. Recommended Actions:" -ForegroundColor Yellow
Write-Host "   1. Check SQL Server to verify RetryCount values for pending items" -ForegroundColor White
Write-Host "   2. If all items exceeded retries, reset them: UPDATE ContainerScanQueues SET RetryCount = 0 WHERE Status = 'Pending'" -ForegroundColor White
Write-Host "   3. Resolve service registration conflict (remove duplicate service registration)" -ForegroundColor White
Write-Host "   4. Check application logs for '[CONTAINER-SCAN-QUEUE] Queue status' messages" -ForegroundColor White
Write-Host ""


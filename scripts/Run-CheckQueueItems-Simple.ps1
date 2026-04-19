# Simple PowerShell script to run Check-QueueItems.sql queries
# Uses .NET SqlClient (works even if sqlcmd is not in PATH)

param(
    [string]$Server = "127.0.0.1,1433",
    [string]$Database = "NS_CIS"
)

$ErrorActionPreference = "Continue"

Write-Host "========================================" -ForegroundColor Cyan
Write-Host "Container Scan Queue Diagnostic Queries" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""
Write-Host "Server: $Server" -ForegroundColor Yellow
Write-Host "Database: $Database" -ForegroundColor Yellow
Write-Host ""

$connectionString = "Server=$Server;Database=$Database;Integrated Security=true;TrustServerCertificate=true;Connection Timeout=30;"

function Invoke-SqlQuery {
    param([string]$Query, [string]$ConnectionString)
    
    try {
        $connection = New-Object System.Data.SqlClient.SqlConnection($ConnectionString)
        $connection.Open()
        
        $command = $connection.CreateCommand()
        $command.CommandText = $Query
        $command.CommandTimeout = 30
        
        $adapter = New-Object System.Data.SqlClient.SqlDataAdapter $command
        $dataset = New-Object System.Data.DataSet
        $adapter.Fill($dataset) | Out-Null
        
        $connection.Close()
        
        return $dataset.Tables[0]
    }
    catch {
        Write-Host "Error: $_" -ForegroundColor Red
        return $null
    }
}

# Query 1: Queue items status and retry counts
Write-Host "=== Query 1: Queue items status and retry counts ===" -ForegroundColor Green
Write-Host ""
$query1 = @"
SELECT TOP 20
    Id,
    Status,
    RetryCount,
    MaxRetries,
    ContainerNumber,
    ScannerType,
    InspectionId,
    QueuedAt,
    ProcessedAt,
    CompletedAt,
    CASE 
        WHEN RetryCount >= MaxRetries THEN 'EXCEEDED' 
        ELSE 'OK' 
    END AS RetryStatus,
    DATEDIFF(MINUTE, QueuedAt, GETUTCDATE()) AS WaitTimeMinutes
FROM ContainerScanQueues
WHERE Status = 'Pending'
ORDER BY QueuedAt
"@

$result1 = Invoke-SqlQuery -Query $query1 -ConnectionString $connectionString
if ($result1) {
    $result1 | Format-Table -AutoSize
} else {
    Write-Host "  No results or error occurred" -ForegroundColor Yellow
}
Write-Host ""

# Query 2: Queue items with completeness status
Write-Host "=== Query 2: Queue items with completeness status records ===" -ForegroundColor Green
Write-Host ""
$query2 = @"
SELECT TOP 20
    q.Id AS QueueId,
    q.ContainerNumber,
    q.ScannerType,
    q.InspectionId,
    q.Status AS QueueStatus,
    q.QueuedAt,
    q.RetryCount,
    q.MaxRetries,
    CASE 
        WHEN c.Id IS NOT NULL THEN 'EXISTS' 
        ELSE 'MISSING' 
    END AS CompletenessStatusExists,
    c.Status AS CompletenessStatus,
    c.HasICUMSData,
    c.LastCheckedAt AS CompletenessLastChecked
FROM ContainerScanQueues q
LEFT JOIN ContainerCompletenessStatuses c 
    ON c.ContainerNumber = q.ContainerNumber 
    AND c.ScannerType = q.ScannerType
    AND (
        (c.InspectionId = q.InspectionId) 
        OR (c.InspectionId IS NULL AND q.InspectionId IS NULL)
    )
WHERE q.Status = 'Pending'
ORDER BY q.QueuedAt
"@

$result2 = Invoke-SqlQuery -Query $query2 -ConnectionString $connectionString
if ($result2) {
    $result2 | Format-Table -AutoSize
} else {
    Write-Host "  No results or error occurred" -ForegroundColor Yellow
}
Write-Host ""

# Query 3: Queue statistics summary
Write-Host "=== Query 3: Queue statistics summary ===" -ForegroundColor Green
Write-Host ""
$query3 = @"
SELECT 
    Status,
    COUNT(*) AS Count,
    MIN(QueuedAt) AS OldestQueued,
    MAX(QueuedAt) AS NewestQueued,
    AVG(DATEDIFF(MINUTE, QueuedAt, GETUTCDATE())) AS AvgWaitMinutes,
    SUM(CASE WHEN RetryCount >= MaxRetries THEN 1 ELSE 0 END) AS ExceededRetries
FROM ContainerScanQueues
GROUP BY Status
ORDER BY Status
"@

$result3 = Invoke-SqlQuery -Query $query3 -ConnectionString $connectionString
if ($result3) {
    $result3 | Format-Table -AutoSize
} else {
    Write-Host "  No results or error occurred" -ForegroundColor Yellow
}
Write-Host ""

# Query 4: Recently completed items
Write-Host "=== Query 4: Recently completed items (last 24 hours) ===" -ForegroundColor Green
Write-Host ""
$query4 = @"
SELECT TOP 20
    Id,
    ContainerNumber,
    ScannerType,
    Status,
    QueuedAt,
    ProcessedAt,
    CompletedAt,
    DATEDIFF(SECOND, QueuedAt, CompletedAt) AS ProcessingTimeSeconds,
    RetryCount
FROM ContainerScanQueues
WHERE Status = 'Completed' 
    AND CompletedAt >= DATEADD(HOUR, -24, GETUTCDATE())
ORDER BY CompletedAt DESC
"@

$result4 = Invoke-SqlQuery -Query $query4 -ConnectionString $connectionString
if ($result4) {
    $result4 | Format-Table -AutoSize
} else {
    Write-Host "  No results or error occurred" -ForegroundColor Yellow
}
Write-Host ""

# Query 5: Count items by retry status
Write-Host "=== Query 5: Count items by retry status ===" -ForegroundColor Green
Write-Host ""
$query5 = @"
SELECT 
    CASE 
        WHEN RetryCount >= MaxRetries THEN 'Exceeded Max Retries'
        WHEN RetryCount > 0 THEN 'Has Retries'
        ELSE 'No Retries'
    END AS RetryCategory,
    COUNT(*) AS Count,
    MIN(QueuedAt) AS OldestQueued,
    MAX(QueuedAt) AS NewestQueued
FROM ContainerScanQueues
WHERE Status = 'Pending'
GROUP BY 
    CASE 
        WHEN RetryCount >= MaxRetries THEN 'Exceeded Max Retries'
        WHEN RetryCount > 0 THEN 'Has Retries'
        ELSE 'No Retries'
    END
"@

$result5 = Invoke-SqlQuery -Query $query5 -ConnectionString $connectionString
if ($result5) {
    $result5 | Format-Table -AutoSize
} else {
    Write-Host "  No results or error occurred" -ForegroundColor Yellow
}
Write-Host ""

Write-Host "========================================" -ForegroundColor Cyan
Write-Host "Diagnostic queries completed" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan


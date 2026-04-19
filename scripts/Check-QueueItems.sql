-- Diagnostic SQL queries to verify Container Scan Queue item status
-- Run these queries in SQL Server Management Studio against the NS_CIS database

-- ============================================
-- Query 1: Check queue items status and retry counts
-- ============================================
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
ORDER BY QueuedAt;

-- ============================================
-- Query 2: Check if queue items have corresponding completeness status records
-- ============================================
SELECT 
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
ORDER BY q.QueuedAt;

-- ============================================
-- Query 3: Queue statistics summary
-- ============================================
SELECT 
    Status,
    COUNT(*) AS Count,
    MIN(QueuedAt) AS OldestQueued,
    MAX(QueuedAt) AS NewestQueued,
    AVG(DATEDIFF(MINUTE, QueuedAt, GETUTCDATE())) AS AvgWaitMinutes,
    SUM(CASE WHEN RetryCount >= MaxRetries THEN 1 ELSE 0 END) AS ExceededRetries
FROM ContainerScanQueues
GROUP BY Status
ORDER BY Status;

-- ============================================
-- Query 4: Check recently completed items
-- ============================================
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
ORDER BY CompletedAt DESC;

-- ============================================
-- Query 5: Count items by retry status
-- ============================================
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
    END;


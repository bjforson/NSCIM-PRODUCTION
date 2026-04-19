-- Diagnostic SQL queries to investigate failed batch downloads
-- Run this script in SQL Server Management Studio or via sqlcmd

-- SECTION 1: OVERALL STATISTICS
SELECT 
    ProcessingStatus,
    COUNT(*) AS FileCount,
    SUM(RecordCount) AS TotalRecords,
    MIN(DownloadDate) AS EarliestDownload,
    MAX(DownloadDate) AS LatestDownload
FROM DownloadedFiles
GROUP BY ProcessingStatus
ORDER BY FileCount DESC;

-- SECTION 2: TOP 20 ERROR MESSAGES
SELECT TOP 20
    ErrorMessage,
    COUNT(*) AS ErrorCount,
    MIN(DownloadDate) AS FirstOccurrence,
    MAX(DownloadDate) AS LastOccurrence
FROM DownloadedFiles
WHERE ProcessingStatus = 'Failed'
    AND ErrorMessage IS NOT NULL
GROUP BY ErrorMessage
ORDER BY ErrorCount DESC;

-- SECTION 3: FAILED FILES BY SOURCE
SELECT 
    DownloadSource,
    COUNT(*) AS FileCount,
    SUM(RecordCount) AS TotalRecords,
    MIN(DownloadDate) AS EarliestDownload,
    MAX(DownloadDate) AS LatestDownload
FROM DownloadedFiles
WHERE ProcessingStatus = 'Failed'
GROUP BY DownloadSource
ORDER BY FileCount DESC;

-- SECTION 4: FAILED FILES BY PATTERN
SELECT 
    CASE 
        WHEN FileName LIKE 'BatchData_%' THEN 'BatchData'
        WHEN FileName LIKE 'Queue_%' THEN 'Queue'
        WHEN FileName LIKE 'OnDemand_%' THEN 'OnDemand'
        ELSE 'Other'
    END AS FilePattern,
    COUNT(*) AS FileCount,
    MIN(DownloadDate) AS EarliestDownload,
    MAX(DownloadDate) AS LatestDownload
FROM DownloadedFiles
WHERE ProcessingStatus = 'Failed'
GROUP BY 
    CASE 
        WHEN FileName LIKE 'BatchData_%' THEN 'BatchData'
        WHEN FileName LIKE 'Queue_%' THEN 'Queue'
        WHEN FileName LIKE 'OnDemand_%' THEN 'OnDemand'
        ELSE 'Other'
    END
ORDER BY FileCount DESC;

-- SECTION 5: FILE NOT FOUND ANALYSIS
SELECT 
    COUNT(*) AS FileNotFoundCount,
    COUNT(DISTINCT FilePath) AS UniquePaths,
    MIN(DownloadDate) AS EarliestDownload,
    MAX(DownloadDate) AS LatestDownload
FROM DownloadedFiles
WHERE ProcessingStatus = 'Failed'
    AND (
        ErrorMessage LIKE '%File not found%'
        OR ErrorMessage LIKE '%does not exist%'
        OR ErrorMessage LIKE '%FileNotFoundException%'
        OR FilePath LIKE 'Queue/%'
        OR FilePath LIKE 'OnDemand/%'
    );

-- SECTION 6: JSON PARSING ERRORS
SELECT 
    COUNT(*) AS JsonErrorCount,
    MIN(DownloadDate) AS EarliestDownload,
    MAX(DownloadDate) AS LatestDownload
FROM DownloadedFiles
WHERE ProcessingStatus = 'Failed'
    AND (
        ErrorMessage LIKE '%JSON%'
        OR ErrorMessage LIKE '%BOEScanDocument%'
        OR ErrorMessage LIKE '%deserialization%'
        OR ErrorMessage LIKE '%parse%'
    );

-- SECTION 7: RECENT FAILURES (Last 24 hours)
SELECT TOP 10
    Id,
    FileName,
    FilePath,
    DownloadSource,
    ProcessingStatus,
    ErrorMessage,
    DownloadDate,
    RecordCount
FROM DownloadedFiles
WHERE ProcessingStatus = 'Failed'
    AND DownloadDate >= DATEADD(HOUR, -24, GETUTCDATE())
ORDER BY DownloadDate DESC;


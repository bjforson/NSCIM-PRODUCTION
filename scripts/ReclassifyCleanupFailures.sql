-- ========================================================================
-- RECLASSIFY CLEANUP-RELATED FAILURES
-- ========================================================================
-- This script reclassifies files that were marked as "Failed" during
-- manual cleanup operations but should actually be "Archived"
-- ========================================================================

USE ICUMS_Downloads;
GO

SET NOCOUNT ON;

PRINT '========================================================================';
PRINT 'RECLASSIFY CLEANUP-RELATED FAILURES';
PRINT '========================================================================';
PRINT '';

-- Show current status before reclassification
PRINT 'CURRENT STATUS (Before Reclassification):';
PRINT '';

SELECT 
    ProcessingStatus,
    COUNT(*) AS FileCount,
    MIN(DownloadDate) AS EarliestDownload,
    MAX(DownloadDate) AS LatestDownload
FROM DownloadedFiles
WHERE ProcessingStatus = 'Failed'
GROUP BY ProcessingStatus;

PRINT '';
PRINT 'Breakdown of Failed files by error message:';
PRINT '';

SELECT 
    ErrorMessage,
    COUNT(*) AS FileCount
FROM DownloadedFiles
WHERE ProcessingStatus = 'Failed'
    AND ErrorMessage IS NOT NULL
GROUP BY ErrorMessage
ORDER BY FileCount DESC;

PRINT '';
PRINT '========================================================================';
PRINT 'RECLASSIFICATION OPERATIONS';
PRINT '========================================================================';
PRINT '';

-- -------------------------------------------------------------------------
-- FIX 1: Reclassify duplicate cleanup failures (2,094 files)
-- -------------------------------------------------------------------------
PRINT 'FIX 1: Reclassifying duplicate cleanup failures...';
PRINT '';

DECLARE @DuplicateCount INT;

BEGIN TRANSACTION;

UPDATE DownloadedFiles
SET 
    ProcessingStatus = 'Archived',
    ErrorMessage = 'Successfully processed - duplicate removed during cleanup',
    UpdatedAt = GETUTCDATE()
WHERE ProcessingStatus = 'Failed'
    AND ErrorMessage = 'File missing after cleanup - likely duplicate that was removed';

SET @DuplicateCount = @@ROWCOUNT;

COMMIT TRANSACTION;

PRINT '✅ Reclassified ' + CAST(@DuplicateCount AS VARCHAR) + ' duplicate cleanup failures → Archived';
PRINT '';

-- -------------------------------------------------------------------------
-- FIX 2: Reclassify old batch file cleanup failures (1,892 files)
-- -------------------------------------------------------------------------
PRINT 'FIX 2: Reclassifying old batch file cleanup failures...';
PRINT '';

DECLARE @BatchCount INT;

BEGIN TRANSACTION;

UPDATE DownloadedFiles
SET 
    ProcessingStatus = 'Archived',
    ErrorMessage = 'Successfully processed - old batch file removed during cleanup',
    UpdatedAt = GETUTCDATE()
WHERE ProcessingStatus = 'Failed'
    AND ErrorMessage = 'File missing after cleanup - old batch file removed';

SET @BatchCount = @@ROWCOUNT;

COMMIT TRANSACTION;

PRINT '✅ Reclassified ' + CAST(@BatchCount AS VARCHAR) + ' old batch file cleanup failures → Archived';
PRINT '';

-- -------------------------------------------------------------------------
-- SUMMARY
-- -------------------------------------------------------------------------
PRINT '';
PRINT '========================================================================';
PRINT 'RECLASSIFICATION SUMMARY';
PRINT '========================================================================';
PRINT '';

SELECT 
    ProcessingStatus,
    COUNT(*) AS FileCount,
    MIN(DownloadDate) AS EarliestDownload,
    MAX(DownloadDate) AS LatestDownload
FROM DownloadedFiles
WHERE ProcessingStatus IN ('Failed', 'Archived')
GROUP BY ProcessingStatus
ORDER BY ProcessingStatus;

PRINT '';
PRINT 'Total reclassified: ' + CAST((@DuplicateCount + @BatchCount) AS VARCHAR) + ' files';
PRINT '  - Duplicate cleanup: ' + CAST(@DuplicateCount AS VARCHAR) + ' files';
PRINT '  - Old batch cleanup: ' + CAST(@BatchCount AS VARCHAR) + ' files';
PRINT '';
PRINT '✅ Reclassification complete!';
PRINT '';


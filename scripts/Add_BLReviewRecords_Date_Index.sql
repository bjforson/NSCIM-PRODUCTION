-- ============================================================================
-- Add Date Index for BLReviewRecords Table
-- Purpose: Enable efficient date-based filtering for GetStatisticsAsync
-- Date: 2025-01-XX
-- ============================================================================

USE [NS_CIS]; -- Change to your database name if different
GO

PRINT 'Adding date index to BLReviewRecords table...';
PRINT '';

-- Index on CreatedAt (for recent reviews filtering)
IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_BLReviewRecords_CreatedAt' AND object_id = OBJECT_ID('BLReviewRecords'))
BEGIN
    PRINT 'Creating index: IX_BLReviewRecords_CreatedAt...';
    CREATE INDEX [IX_BLReviewRecords_CreatedAt] 
    ON [BLReviewRecords]([CreatedAt]);
    PRINT '✅ Index IX_BLReviewRecords_CreatedAt created.';
    PRINT '';
END
ELSE
BEGIN
    PRINT '⚠️ Index IX_BLReviewRecords_CreatedAt already exists.';
    PRINT '';
END
GO

-- Composite index for Status + CreatedAt (for filtering by status and date)
IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_BLReviewRecords_Status_CreatedAt' AND object_id = OBJECT_ID('BLReviewRecords'))
BEGIN
    PRINT 'Creating index: IX_BLReviewRecords_Status_CreatedAt...';
    CREATE INDEX [IX_BLReviewRecords_Status_CreatedAt] 
    ON [BLReviewRecords]([ReviewStatus], [CreatedAt]);
    PRINT '✅ Index IX_BLReviewRecords_Status_CreatedAt created.';
    PRINT '';
END
ELSE
BEGIN
    PRINT '⚠️ Index IX_BLReviewRecords_Status_CreatedAt already exists.';
    PRINT '';
END
GO

PRINT '✅ Index creation script completed!';
GO


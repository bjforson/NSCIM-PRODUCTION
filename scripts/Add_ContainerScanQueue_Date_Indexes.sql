-- ============================================================================
-- Add Missing Date Indexes for ContainerScanQueues Table
-- Purpose: Enable efficient date-based filtering to reduce buffer pool usage
-- Date: 2025-01-XX
-- ============================================================================

USE [NS_CIS]; -- Change to your database name if different
GO

PRINT 'Adding missing date indexes to ContainerScanQueues table...';
PRINT '';

-- Index for filtering by QueuedAt only (most common filter)
-- Used by: Test 1, Test 2, Test 7 (Health Summary)
IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_ContainerScanQueues_QueuedAt' AND object_id = OBJECT_ID('ContainerScanQueues'))
BEGIN
    PRINT 'Creating index: IX_ContainerScanQueues_QueuedAt...';
    CREATE INDEX [IX_ContainerScanQueues_QueuedAt] 
    ON [ContainerScanQueues]([QueuedAt]);
    PRINT '✅ Index IX_ContainerScanQueues_QueuedAt created.';
    PRINT '';
END
ELSE
BEGIN
    PRINT '⚠️ Index IX_ContainerScanQueues_QueuedAt already exists.';
    PRINT '';
END
GO

-- Index for filtering by CreatedAt only (for recent items queries)
-- Used by: Test 3, Test 5 (Recent Items, Failed Items)
IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_ContainerScanQueues_CreatedAt' AND object_id = OBJECT_ID('ContainerScanQueues'))
BEGIN
    PRINT 'Creating index: IX_ContainerScanQueues_CreatedAt...';
    CREATE INDEX [IX_ContainerScanQueues_CreatedAt] 
    ON [ContainerScanQueues]([CreatedAt]);
    PRINT '✅ Index IX_ContainerScanQueues_CreatedAt created.';
    PRINT '';
END
ELSE
BEGIN
    PRINT '⚠️ Index IX_ContainerScanQueues_CreatedAt already exists.';
    PRINT '';
END
GO

-- Index for filtering by CompletedAt (for processing rate queries)
-- Used by: Test 4 (Processing Rate)
IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_ContainerScanQueues_CompletedAt' AND object_id = OBJECT_ID('ContainerScanQueues'))
BEGIN
    PRINT 'Creating index: IX_ContainerScanQueues_CompletedAt...';
    CREATE INDEX [IX_ContainerScanQueues_CompletedAt] 
    ON [ContainerScanQueues]([CompletedAt]);
    PRINT '✅ Index IX_ContainerScanQueues_CompletedAt created.';
    PRINT '';
END
ELSE
BEGIN
    PRINT '⚠️ Index IX_ContainerScanQueues_CompletedAt already exists.';
    PRINT '';
END
GO

-- Composite index for Status + CreatedAt queries (failed items, recent items by status)
-- Used by: Test 5 (Failed Items), queries filtering by Status + CreatedAt
IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_ContainerScanQueues_Status_CreatedAt' AND object_id = OBJECT_ID('ContainerScanQueues'))
BEGIN
    PRINT 'Creating index: IX_ContainerScanQueues_Status_CreatedAt...';
    CREATE INDEX [IX_ContainerScanQueues_Status_CreatedAt] 
    ON [ContainerScanQueues]([Status], [CreatedAt]);
    PRINT '✅ Index IX_ContainerScanQueues_Status_CreatedAt created.';
    PRINT '';
END
ELSE
BEGIN
    PRINT '⚠️ Index IX_ContainerScanQueues_Status_CreatedAt already exists.';
    PRINT '';
END
GO

-- Index for filtering by ProcessedAt (for stuck items queries)
-- Used by: Test 6 (Stuck Items)
IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_ContainerScanQueues_ProcessedAt' AND object_id = OBJECT_ID('ContainerScanQueues'))
BEGIN
    PRINT 'Creating index: IX_ContainerScanQueues_ProcessedAt...';
    CREATE INDEX [IX_ContainerScanQueues_ProcessedAt] 
    ON [ContainerScanQueues]([ProcessedAt]);
    PRINT '✅ Index IX_ContainerScanQueues_ProcessedAt created.';
    PRINT '';
END
ELSE
BEGIN
    PRINT '⚠️ Index IX_ContainerScanQueues_ProcessedAt already exists.';
    PRINT '';
END
GO

-- Verify indexes were created (SQL Server 2014 compatible)
PRINT '========================================';
PRINT 'Verification: List of all indexes on ContainerScanQueues';
PRINT '========================================';
SELECT 
    i.name AS IndexName,
    i.type_desc AS IndexType,
    i.is_unique AS IsUnique,
    c.name AS IndexColumn
FROM sys.indexes i
INNER JOIN sys.index_columns ic ON i.object_id = ic.object_id AND i.index_id = ic.index_id
INNER JOIN sys.columns c ON ic.object_id = c.object_id AND ic.column_id = c.column_id
WHERE i.object_id = OBJECT_ID('ContainerScanQueues')
    AND i.type > 0  -- Exclude heap (type 0)
ORDER BY i.name, ic.key_ordinal;
GO

PRINT '';
PRINT '✅ Index creation script completed!';
PRINT '';
PRINT 'Next Steps:';
PRINT '1. Add date filters to queries (see SQL_MEMORY_OPTIMIZATION_INVESTIGATION.md)';
PRINT '2. Monitor SQL Server buffer pool usage';
PRINT '3. Test query performance with date filters';
GO


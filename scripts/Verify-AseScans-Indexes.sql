-- ============================================================================
-- Verify/Create Date Indexes for AseScans Table
-- Purpose: Enable efficient date-based filtering to reduce buffer pool usage
-- Date: 2026-01-11
-- ============================================================================

USE [NS_CIS]; -- Change to your database name if different
GO

PRINT 'Verifying/Creating date indexes for AseScans table...';
PRINT '';

-- Check existing indexes (SQL Server 2014 compatible)
PRINT 'Existing indexes on AseScans:';
SELECT 
    i.name AS IndexName,
    i.type_desc AS IndexType,
    i.is_unique AS IsUnique
FROM sys.indexes i
WHERE i.object_id = OBJECT_ID('AseScans')
    AND i.name IS NOT NULL
ORDER BY i.name;
GO

PRINT '';

-- Index on ScanTime (for date filtering - most important)
IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_AseScans_ScanTime' AND object_id = OBJECT_ID('AseScans'))
BEGIN
    PRINT 'Creating index: IX_AseScans_ScanTime...';
    CREATE INDEX [IX_AseScans_ScanTime] 
    ON [AseScans]([ScanTime]);
    PRINT '✅ Index IX_AseScans_ScanTime created.';
    PRINT '';
END
ELSE
BEGIN
    PRINT '✅ Index IX_AseScans_ScanTime already exists.';
    PRINT '';
END
GO

-- Index on CreatedAt (for record creation date filtering)
IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_AseScans_CreatedAt' AND object_id = OBJECT_ID('AseScans'))
BEGIN
    PRINT 'Creating index: IX_AseScans_CreatedAt...';
    CREATE INDEX [IX_AseScans_CreatedAt] 
    ON [AseScans]([CreatedAt]);
    PRINT '✅ Index IX_AseScans_CreatedAt created.';
    PRINT '';
END
ELSE
BEGIN
    PRINT '✅ Index IX_AseScans_CreatedAt already exists.';
    PRINT '';
END
GO

-- Composite index for ContainerNumber + ScanTime (for filtered lookups)
IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_AseScans_ContainerNumber_ScanTime' AND object_id = OBJECT_ID('AseScans'))
BEGIN
    PRINT 'Creating index: IX_AseScans_ContainerNumber_ScanTime...';
    CREATE INDEX [IX_AseScans_ContainerNumber_ScanTime] 
    ON [AseScans]([ContainerNumber], [ScanTime]);
    PRINT '✅ Index IX_AseScans_ContainerNumber_ScanTime created.';
    PRINT '';
END
ELSE
BEGIN
    PRINT '✅ Index IX_AseScans_ContainerNumber_ScanTime already exists.';
    PRINT '';
END
GO

PRINT '✅ Index verification/creation completed!';
GO


-- Add CreatedAt index to ManifestItems table for efficient date filtering
-- This index is CRITICAL for the memory optimization fix to work properly
-- Without this index, queries with date filters will still scan the entire table

USE ICUMS_Downloads;
GO

-- Check if index already exists
IF NOT EXISTS (
    SELECT 1 
    FROM sys.indexes 
    WHERE name = 'IX_ManifestItems_CreatedAt' 
    AND object_id = OBJECT_ID('dbo.ManifestItems')
)
BEGIN
    PRINT 'Creating IX_ManifestItems_CreatedAt index...';
    
    CREATE NONCLUSTERED INDEX IX_ManifestItems_CreatedAt
    ON dbo.ManifestItems (CreatedAt)
    INCLUDE (ProcessingStatus, BOEDocumentId, ItemIndex);
    
    PRINT 'Index IX_ManifestItems_CreatedAt created successfully.';
END
ELSE
BEGIN
    PRINT 'Index IX_ManifestItems_CreatedAt already exists.';
END
GO

-- Verify index was created
SELECT 
    i.name AS IndexName,
    i.type_desc AS IndexType,
    COL_NAME(ic.object_id, ic.column_id) AS ColumnName,
    ic.is_included_column AS IsIncluded
FROM sys.indexes i
INNER JOIN sys.index_columns ic ON i.object_id = ic.object_id AND i.index_id = ic.index_id
WHERE i.object_id = OBJECT_ID('dbo.ManifestItems')
    AND i.name = 'IX_ManifestItems_CreatedAt'
ORDER BY ic.key_ordinal, ic.is_included_column;
GO

PRINT '';
PRINT 'Index verification complete.';
PRINT 'The date filter optimization will now use index seeks instead of table scans.';
GO


-- ================================================
-- Add Missing Columns to ManifestItems Table
-- ICUMS_Downloads Database
-- ================================================

USE ICUMS_Downloads;
GO

PRINT '========================================';
PRINT 'Adding Missing Columns to ManifestItems';
PRINT '========================================';
PRINT '';

BEGIN TRANSACTION;

-- Add RawJsonData column
IF COL_LENGTH('ManifestItems', 'RawJsonData') IS NULL
BEGIN
    ALTER TABLE ManifestItems ADD RawJsonData nvarchar(max) NULL;
    PRINT '  ✓ Added RawJsonData column';
END
ELSE
BEGIN
    PRINT '  → RawJsonData column already exists';
END

-- Add UnmappedFieldsCount column
IF COL_LENGTH('ManifestItems', 'UnmappedFieldsCount') IS NULL
BEGIN
    ALTER TABLE ManifestItems ADD UnmappedFieldsCount int NULL;
    PRINT '  ✓ Added UnmappedFieldsCount column';
END
ELSE
BEGIN
    PRINT '  → UnmappedFieldsCount column already exists';
END

-- Add UnmappedFieldsOverflow column
IF COL_LENGTH('ManifestItems', 'UnmappedFieldsOverflow') IS NULL
BEGIN
    ALTER TABLE ManifestItems ADD UnmappedFieldsOverflow bit NOT NULL DEFAULT 0;
    PRINT '  ✓ Added UnmappedFieldsOverflow column';
END
ELSE
BEGIN
    PRINT '  → UnmappedFieldsOverflow column already exists';
END

-- Add UnmappedField columns (1-20, Label and Value for each)
DECLARE @i INT = 1;
WHILE @i <= 20
BEGIN
    DECLARE @labelCol NVARCHAR(50) = 'UnmappedField' + CAST(@i AS NVARCHAR(2)) + 'Label';
    DECLARE @valueCol NVARCHAR(50) = 'UnmappedField' + CAST(@i AS NVARCHAR(2)) + 'Value';
    
    -- Add Label column
    IF COL_LENGTH('ManifestItems', @labelCol) IS NULL
    BEGIN
        DECLARE @sqlLabel NVARCHAR(MAX) = 'ALTER TABLE ManifestItems ADD [' + @labelCol + '] nvarchar(200) NULL';
        EXEC sp_executesql @sqlLabel;
        PRINT '  ✓ Added ' + @labelCol + ' column';
    END
    
    -- Add Value column
    IF COL_LENGTH('ManifestItems', @valueCol) IS NULL
    BEGIN
        DECLARE @sqlValue NVARCHAR(MAX) = 'ALTER TABLE ManifestItems ADD [' + @valueCol + '] nvarchar(4000) NULL';
        EXEC sp_executesql @sqlValue;
        PRINT '  ✓ Added ' + @valueCol + ' column';
    END
    
    SET @i = @i + 1;
END

COMMIT TRANSACTION;

PRINT '';
PRINT '========================================';
PRINT 'All columns added successfully!';
PRINT '========================================';
GO


-- ============================================================================
-- Create IcumManifestItems Table
-- This script safely creates the IcumManifestItems table if it doesn't exist
-- Safe to run multiple times - checks for table existence first
-- ============================================================================
-- Database: ICUMS
-- Table: IcumManifestItems
-- ============================================================================
-- IMPORTANT: Make sure you're connected to the ICUMS database before running!
-- ============================================================================

USE [ICUMS];
GO

PRINT '========================================';
PRINT 'Creating IcumManifestItems Table';
PRINT '========================================';
PRINT '';

-- Check if table already exists
IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'IcumManifestItems')
BEGIN
    PRINT '📝 Creating IcumManifestItems table...';
    
    CREATE TABLE [dbo].[IcumManifestItems](
        [Id] int IDENTITY(1,1) NOT NULL,
        [IcumContainerDataId] int NOT NULL,
        [HouseBl] nvarchar(100) NULL,
        [HsCode] nvarchar(20) NOT NULL,
        [Description] nvarchar(2000) NULL,
        [Quantity] decimal(18,2) NOT NULL,
        [Unit] nvarchar(50) NULL,
        [Weight] decimal(18,2) NOT NULL,
        [ItemFob] decimal(18,2) NOT NULL,
        [ItemDutyPaid] decimal(18,2) NOT NULL,
        [FobCurrency] nvarchar(10) NULL,
        [CountryOfOrigin] nvarchar(10) NULL,
        [ItemNo] int NOT NULL,
        [Cpc] nvarchar(20) NULL,
        [CreatedAt] datetime2 NOT NULL DEFAULT GETUTCDATE(),
        [UpdatedAt] datetime2 NOT NULL DEFAULT GETUTCDATE(),
        CONSTRAINT [PK_IcumManifestItems] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_IcumManifestItems_IcumContainerData_IcumContainerDataId] 
            FOREIGN KEY ([IcumContainerDataId]) REFERENCES [IcumContainerData] ([Id]) ON DELETE CASCADE
    );
    
    PRINT '✅ IcumManifestItems table created successfully';
END
ELSE
BEGIN
    PRINT '⚠️ IcumManifestItems table already exists - skipping creation';
END
GO

PRINT '';
PRINT '========================================';
PRINT 'Creating Indexes';
PRINT '========================================';
PRINT '';

-- Create index on HsCode
IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_IcumManifestItems_HsCode' AND object_id = OBJECT_ID('IcumManifestItems'))
BEGIN
    CREATE INDEX [IX_IcumManifestItems_HsCode] ON [dbo].[IcumManifestItems] ([HsCode]);
    PRINT '✓ Created index: IX_IcumManifestItems_HsCode';
END
ELSE
BEGIN
    PRINT '⊘ Index IX_IcumManifestItems_HsCode already exists';
END
GO

-- Create index on CountryOfOrigin
IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_IcumManifestItems_CountryOfOrigin' AND object_id = OBJECT_ID('IcumManifestItems'))
BEGIN
    CREATE INDEX [IX_IcumManifestItems_CountryOfOrigin] ON [dbo].[IcumManifestItems] ([CountryOfOrigin]);
    PRINT '✓ Created index: IX_IcumManifestItems_CountryOfOrigin';
END
ELSE
BEGIN
    PRINT '⊘ Index IX_IcumManifestItems_CountryOfOrigin already exists';
END
GO

-- Create index on IcumContainerDataId
IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_IcumManifestItems_IcumContainerDataId' AND object_id = OBJECT_ID('IcumManifestItems'))
BEGIN
    CREATE INDEX [IX_IcumManifestItems_IcumContainerDataId] ON [dbo].[IcumManifestItems] ([IcumContainerDataId]);
    PRINT '✓ Created index: IX_IcumManifestItems_IcumContainerDataId';
END
ELSE
BEGIN
    PRINT '⊘ Index IX_IcumManifestItems_IcumContainerDataId already exists';
END
GO

-- Create index on HouseBl (for consolidated cargo support)
IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_IcumManifestItems_HouseBl' AND object_id = OBJECT_ID('IcumManifestItems'))
BEGIN
    CREATE INDEX [IX_IcumManifestItems_HouseBl] ON [dbo].[IcumManifestItems] ([HouseBl]);
    PRINT '✓ Created index: IX_IcumManifestItems_HouseBl';
END
ELSE
BEGIN
    PRINT '⊘ Index IX_IcumManifestItems_HouseBl already exists';
END
GO

PRINT '';
PRINT '========================================';
PRINT '✅ IcumManifestItems Table Setup Complete!';
PRINT '========================================';
PRINT '';
PRINT 'The IcumManifestItems table is now ready for manifest item storage.';
PRINT 'IcumDataTransferService should now work without "Invalid object name" errors.';
PRINT '';


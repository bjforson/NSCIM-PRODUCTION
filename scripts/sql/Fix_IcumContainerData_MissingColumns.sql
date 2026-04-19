-- ============================================================================
-- Fix Missing Columns in IcumContainerData Table
-- This script safely adds missing structured columns if they don't exist
-- Safe to run multiple times - checks for column existence first
-- ============================================================================
-- Database: ICUMS
-- Server: 127.0.0.1,1433 (or your configured server)
-- Table: IcumContainerData
-- ============================================================================
-- IMPORTANT: Make sure you're connected to the ICUMS database before running!
-- ============================================================================

USE [ICUMS];
GO

PRINT '========================================';
PRINT 'Fixing Missing Columns in IcumContainerData';
PRINT '========================================';
PRINT '';

-- Check if table exists
IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'IcumContainerData')
BEGIN
    PRINT 'ERROR: Table IcumContainerData does not exist!';
    PRINT 'Please create the table first using the migration or schema script.';
    RETURN;
END

PRINT '✓ Table IcumContainerData exists';
PRINT '';

-- Function to safely add a column if it doesn't exist
DECLARE @sql NVARCHAR(MAX);

-- Add MasterBlNumber
IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('IcumContainerData') AND name = 'MasterBlNumber')
BEGIN
    ALTER TABLE [IcumContainerData] ADD [MasterBlNumber] nvarchar(100) NULL;
    PRINT '✓ Added column: MasterBlNumber';
END
ELSE
BEGIN
    PRINT '⊘ Column MasterBlNumber already exists';
END

-- Add HouseBl
IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('IcumContainerData') AND name = 'HouseBl')
BEGIN
    ALTER TABLE [IcumContainerData] ADD [HouseBl] nvarchar(100) NULL;
    PRINT '✓ Added column: HouseBl';
END
ELSE
BEGIN
    PRINT '⊘ Column HouseBl already exists';
END

-- Add RotationNumber
IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('IcumContainerData') AND name = 'RotationNumber')
BEGIN
    ALTER TABLE [IcumContainerData] ADD [RotationNumber] nvarchar(50) NULL;
    PRINT '✓ Added column: RotationNumber';
END
ELSE
BEGIN
    PRINT '⊘ Column RotationNumber already exists';
END

-- Add ConsigneeName
IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('IcumContainerData') AND name = 'ConsigneeName')
BEGIN
    ALTER TABLE [IcumContainerData] ADD [ConsigneeName] nvarchar(200) NULL;
    PRINT '✓ Added column: ConsigneeName';
END
ELSE
BEGIN
    PRINT '⊘ Column ConsigneeName already exists';
END

-- Add ShipperName
IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('IcumContainerData') AND name = 'ShipperName')
BEGIN
    ALTER TABLE [IcumContainerData] ADD [ShipperName] nvarchar(200) NULL;
    PRINT '✓ Added column: ShipperName';
END
ELSE
BEGIN
    PRINT '⊘ Column ShipperName already exists';
END

-- Add CountryOfOrigin
IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('IcumContainerData') AND name = 'CountryOfOrigin')
BEGIN
    ALTER TABLE [IcumContainerData] ADD [CountryOfOrigin] nvarchar(100) NULL;
    PRINT '✓ Added column: CountryOfOrigin';
END
ELSE
BEGIN
    PRINT '⊘ Column CountryOfOrigin already exists';
END

-- Add TotalDutyPaid
IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('IcumContainerData') AND name = 'TotalDutyPaid')
BEGIN
    ALTER TABLE [IcumContainerData] ADD [TotalDutyPaid] decimal(18,2) NULL;
    PRINT '✓ Added column: TotalDutyPaid';
END
ELSE
BEGIN
    PRINT '⊘ Column TotalDutyPaid already exists';
END

-- Add CrmsLevel
IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('IcumContainerData') AND name = 'CrmsLevel')
BEGIN
    ALTER TABLE [IcumContainerData] ADD [CrmsLevel] nvarchar(50) NULL;
    PRINT '✓ Added column: CrmsLevel';
END
ELSE
BEGIN
    PRINT '⊘ Column CrmsLevel already exists';
END

-- Add ClearanceType
IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('IcumContainerData') AND name = 'ClearanceType')
BEGIN
    ALTER TABLE [IcumContainerData] ADD [ClearanceType] nvarchar(50) NULL;
    PRINT '✓ Added column: ClearanceType';
END
ELSE
BEGIN
    PRINT '⊘ Column ClearanceType already exists';
END

-- Add DeclarationNumber
IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('IcumContainerData') AND name = 'DeclarationNumber')
BEGIN
    ALTER TABLE [IcumContainerData] ADD [DeclarationNumber] nvarchar(100) NULL;
    PRINT '✓ Added column: DeclarationNumber';
END
ELSE
BEGIN
    PRINT '⊘ Column DeclarationNumber already exists';
END

-- Add ContainerWeight
IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('IcumContainerData') AND name = 'ContainerWeight')
BEGIN
    ALTER TABLE [IcumContainerData] ADD [ContainerWeight] decimal(18,2) NULL;
    PRINT '✓ Added column: ContainerWeight';
END
ELSE
BEGIN
    PRINT '⊘ Column ContainerWeight already exists';
END

-- Add ContainerQuantity
IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('IcumContainerData') AND name = 'ContainerQuantity')
BEGIN
    ALTER TABLE [IcumContainerData] ADD [ContainerQuantity] int NULL;
    PRINT '✓ Added column: ContainerQuantity';
END
ELSE
BEGIN
    PRINT '⊘ Column ContainerQuantity already exists';
END

-- Add ContainerISO
IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('IcumContainerData') AND name = 'ContainerISO')
BEGIN
    ALTER TABLE [IcumContainerData] ADD [ContainerISO] nvarchar(20) NULL;
    PRINT '✓ Added column: ContainerISO';
END
ELSE
BEGIN
    PRINT '⊘ Column ContainerISO already exists';
END

PRINT '';
PRINT '========================================';
PRINT 'Creating Indexes (if they don''t exist)';
PRINT '========================================';
PRINT '';

-- Create indexes if they don't exist
IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_IcumContainerData_MasterBlNumber' AND object_id = OBJECT_ID('IcumContainerData'))
BEGIN
    CREATE INDEX [IX_IcumContainerData_MasterBlNumber] ON [IcumContainerData] ([MasterBlNumber]);
    PRINT '✓ Created index: IX_IcumContainerData_MasterBlNumber';
END

IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_IcumContainerData_HouseBl' AND object_id = OBJECT_ID('IcumContainerData'))
BEGIN
    CREATE INDEX [IX_IcumContainerData_HouseBl] ON [IcumContainerData] ([HouseBl]);
    PRINT '✓ Created index: IX_IcumContainerData_HouseBl';
END

IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_IcumContainerData_RotationNumber' AND object_id = OBJECT_ID('IcumContainerData'))
BEGIN
    CREATE INDEX [IX_IcumContainerData_RotationNumber] ON [IcumContainerData] ([RotationNumber]);
    PRINT '✓ Created index: IX_IcumContainerData_RotationNumber';
END

IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_IcumContainerData_ConsigneeName' AND object_id = OBJECT_ID('IcumContainerData'))
BEGIN
    CREATE INDEX [IX_IcumContainerData_ConsigneeName] ON [IcumContainerData] ([ConsigneeName]);
    PRINT '✓ Created index: IX_IcumContainerData_ConsigneeName';
END

IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_IcumContainerData_ShipperName' AND object_id = OBJECT_ID('IcumContainerData'))
BEGIN
    CREATE INDEX [IX_IcumContainerData_ShipperName] ON [IcumContainerData] ([ShipperName]);
    PRINT '✓ Created index: IX_IcumContainerData_ShipperName';
END

IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_IcumContainerData_CountryOfOrigin' AND object_id = OBJECT_ID('IcumContainerData'))
BEGIN
    CREATE INDEX [IX_IcumContainerData_CountryOfOrigin] ON [IcumContainerData] ([CountryOfOrigin]);
    PRINT '✓ Created index: IX_IcumContainerData_CountryOfOrigin';
END

IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_IcumContainerData_CrmsLevel' AND object_id = OBJECT_ID('IcumContainerData'))
BEGIN
    CREATE INDEX [IX_IcumContainerData_CrmsLevel] ON [IcumContainerData] ([CrmsLevel]);
    PRINT '✓ Created index: IX_IcumContainerData_CrmsLevel';
END

IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_IcumContainerData_ClearanceType' AND object_id = OBJECT_ID('IcumContainerData'))
BEGIN
    CREATE INDEX [IX_IcumContainerData_ClearanceType] ON [IcumContainerData] ([ClearanceType]);
    PRINT '✓ Created index: IX_IcumContainerData_ClearanceType';
END

IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_IcumContainerData_DeclarationNumber' AND object_id = OBJECT_ID('IcumContainerData'))
BEGIN
    CREATE INDEX [IX_IcumContainerData_DeclarationNumber] ON [IcumContainerData] ([DeclarationNumber]);
    PRINT '✓ Created index: IX_IcumContainerData_DeclarationNumber';
END

PRINT '';
PRINT '========================================';
PRINT '✅ Schema Fix Complete!';
PRINT '========================================';
PRINT '';
PRINT 'All missing columns have been added to IcumContainerData table.';
PRINT 'The application should now work without "Invalid column name" errors.';
PRINT '';


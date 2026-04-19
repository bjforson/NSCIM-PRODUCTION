-- ============================================================================
-- Create ApplicationLogs Table
-- This script safely creates the ApplicationLogs table if it doesn't exist
-- Safe to run multiple times - checks for table existence first
-- ============================================================================
-- Database: NS_CIS
-- Table: ApplicationLogs
-- ============================================================================
-- IMPORTANT: Make sure you're connected to the NS_CIS database before running!
-- ============================================================================

USE [NS_CIS];
GO

PRINT '========================================';
PRINT 'Creating ApplicationLogs Table';
PRINT '========================================';
PRINT '';

-- Check if table already exists
IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'ApplicationLogs')
BEGIN
    PRINT '📝 Creating ApplicationLogs table...';
    
    CREATE TABLE [dbo].[ApplicationLogs](
        [Id] [bigint] IDENTITY(1,1) NOT NULL,
        [Timestamp] [datetime2](7) NOT NULL,
        [Level] [nvarchar](50) NOT NULL,
        [ServiceId] [nvarchar](100) NULL,
        [Operation] [nvarchar](200) NULL,
        [Message] [nvarchar](max) NOT NULL,
        [Exception] [nvarchar](max) NULL,
        [Properties] [nvarchar](max) NULL,
        [CreatedAt] [datetime2](7) NOT NULL DEFAULT GETUTCDATE(),
        CONSTRAINT [PK_ApplicationLogs] PRIMARY KEY CLUSTERED ([Id] ASC)
    );
    
    PRINT '✅ ApplicationLogs table created successfully';
END
ELSE
BEGIN
    PRINT '⚠️ ApplicationLogs table already exists - skipping creation';
END
GO

PRINT '';
PRINT '========================================';
PRINT 'Creating Indexes';
PRINT '========================================';
PRINT '';

-- Create index on Timestamp
IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_ApplicationLogs_Timestamp' AND object_id = OBJECT_ID('ApplicationLogs'))
BEGIN
    CREATE NONCLUSTERED INDEX [IX_ApplicationLogs_Timestamp] ON [dbo].[ApplicationLogs]
    (
        [Timestamp] DESC
    );
    PRINT '✓ Created index: IX_ApplicationLogs_Timestamp';
END
ELSE
BEGIN
    PRINT '⊘ Index IX_ApplicationLogs_Timestamp already exists';
END
GO

-- Create index on ServiceId
IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_ApplicationLogs_ServiceId' AND object_id = OBJECT_ID('ApplicationLogs'))
BEGIN
    CREATE NONCLUSTERED INDEX [IX_ApplicationLogs_ServiceId] ON [dbo].[ApplicationLogs]
    (
        [ServiceId] ASC
    );
    PRINT '✓ Created index: IX_ApplicationLogs_ServiceId';
END
ELSE
BEGIN
    PRINT '⊘ Index IX_ApplicationLogs_ServiceId already exists';
END
GO

-- Create index on Level
IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_ApplicationLogs_Level' AND object_id = OBJECT_ID('ApplicationLogs'))
BEGIN
    CREATE NONCLUSTERED INDEX [IX_ApplicationLogs_Level] ON [dbo].[ApplicationLogs]
    (
        [Level] ASC
    );
    PRINT '✓ Created index: IX_ApplicationLogs_Level';
END
ELSE
BEGIN
    PRINT '⊘ Index IX_ApplicationLogs_Level already exists';
END
GO

PRINT '';
PRINT '========================================';
PRINT '✅ ApplicationLogs Table Setup Complete!';
PRINT '========================================';
PRINT '';
PRINT 'The ApplicationLogs table is now ready for structured logging.';
PRINT 'ErrorMonitoringBackgroundService should now work without errors.';
PRINT '';


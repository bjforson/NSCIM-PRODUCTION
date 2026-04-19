-- ============================================================================
-- Create ICUMSDownloadQueues Table
-- This script can be run safely - it checks if the table exists first
-- ============================================================================

USE [NS_CIS]; -- Change to your database name if different
GO

-- Check if table already exists
IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'ICUMSDownloadQueues')
BEGIN
    PRINT '📝 Creating ICUMSDownloadQueues table...';
    
    CREATE TABLE [dbo].[ICUMSDownloadQueues] (
        [Id] int NOT NULL IDENTITY(1,1),
        [ContainerNumber] nvarchar(20) NOT NULL,
        [Status] nvarchar(20) NOT NULL,
        [Priority] int NOT NULL,
        [QueuedAt] datetime2 NOT NULL,
        [FirstAttemptAt] datetime2 NULL,
        [LastAttemptAt] datetime2 NULL,
        [CompletedAt] datetime2 NULL,
        [RetryCount] int NOT NULL,
        [MaxRetries] int NOT NULL,
        [LastErrorMessage] nvarchar(1000) NULL,
        [LastErrorCode] nvarchar(50) NULL,
        [RequestedBy] nvarchar(100) NULL,
        [RequestSource] nvarchar(50) NULL,
        [Metadata] nvarchar(2000) NULL,
        CONSTRAINT [PK_ICUMSDownloadQueues] PRIMARY KEY ([Id])
    );
    
    PRINT '✅ Table created successfully';
END
ELSE
BEGIN
    PRINT '⚠️ Table ICUMSDownloadQueues already exists - skipping creation';
END
GO

-- Create indexes if they don't exist
IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_ICUMSDownloadQueues_ContainerNumber')
BEGIN
    PRINT '📊 Creating index: IX_ICUMSDownloadQueues_ContainerNumber...';
    CREATE INDEX [IX_ICUMSDownloadQueues_ContainerNumber] ON [dbo].[ICUMSDownloadQueues]([ContainerNumber]);
    PRINT '✅ Index created';
END

IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_ICUMSDownloadQueues_Status')
BEGIN
    PRINT '📊 Creating index: IX_ICUMSDownloadQueues_Status...';
    CREATE INDEX [IX_ICUMSDownloadQueues_Status] ON [dbo].[ICUMSDownloadQueues]([Status]);
    PRINT '✅ Index created';
END

IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_ICUMSDownloadQueues_Priority')
BEGIN
    PRINT '📊 Creating index: IX_ICUMSDownloadQueues_Priority...';
    CREATE INDEX [IX_ICUMSDownloadQueues_Priority] ON [dbo].[ICUMSDownloadQueues]([Priority]);
    PRINT '✅ Index created';
END

IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_ICUMSDownloadQueues_QueuedAt')
BEGIN
    PRINT '📊 Creating index: IX_ICUMSDownloadQueues_QueuedAt...';
    CREATE INDEX [IX_ICUMSDownloadQueues_QueuedAt] ON [dbo].[ICUMSDownloadQueues]([QueuedAt]);
    PRINT '✅ Index created';
END

IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_ICUMSDownloadQueues_ContainerNumber_Status')
BEGIN
    PRINT '📊 Creating composite index: IX_ICUMSDownloadQueues_ContainerNumber_Status...';
    CREATE INDEX [IX_ICUMSDownloadQueues_ContainerNumber_Status] ON [dbo].[ICUMSDownloadQueues]([ContainerNumber], [Status]);
    PRINT '✅ Composite index created';
END
GO

-- Update migrations history to mark this migration as applied
IF NOT EXISTS (SELECT * FROM [__EFMigrationsHistory] WHERE [MigrationId] = N'20251018115544_AddICUMSDownloadQueueTable')
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20251018115544_AddICUMSDownloadQueueTable', N'9.0.9');
    PRINT '✅ Migration recorded in history';
END
GO

PRINT '';
PRINT '========================================';
PRINT '✅ ICUMSDownloadQueues table setup complete!';
PRINT '========================================';
PRINT '';
PRINT 'Table Structure:';
PRINT '  - 15 columns';
PRINT '  - 5 performance indexes';
PRINT '  - Primary key on Id (auto-increment)';
PRINT '';
PRINT 'Ready to use! 🚀';
GO


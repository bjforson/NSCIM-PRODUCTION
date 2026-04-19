-- Apply ContainerScanQueue Table Migration
-- Migration: 20260110210159_AddContainerScanQueueTable
-- Date: January 10, 2026

-- Set required options for SQL Server 2014 compatibility
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
GO

-- Check if table already exists
IF NOT EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'[ContainerScanQueues]') AND type in (N'U'))
BEGIN
    PRINT 'Creating ContainerScanQueues table...';
    
    CREATE TABLE [ContainerScanQueues] (
        [Id] int NOT NULL IDENTITY(1,1),
        [ContainerNumber] nvarchar(50) NOT NULL,
        [ScannerType] nvarchar(20) NOT NULL,
        [InspectionId] nvarchar(50) NULL,
        [ScanDate] datetime2 NOT NULL,
        [Status] nvarchar(20) NOT NULL,
        [Priority] int NOT NULL,
        [RetryCount] int NOT NULL,
        [MaxRetries] int NOT NULL,
        [QueuedAt] datetime2 NOT NULL,
        [ProcessedAt] datetime2 NULL,
        [CompletedAt] datetime2 NULL,
        [ErrorMessage] nvarchar(1000) NULL,
        [Metadata] nvarchar(2000) NULL,
        [CreatedAt] datetime2 NOT NULL,
        [UpdatedAt] datetime2 NOT NULL,
        CONSTRAINT [PK_ContainerScanQueues] PRIMARY KEY ([Id])
    );
    
    PRINT 'ContainerScanQueues table created successfully.';
    
    -- Create indexes for performance
    PRINT 'Creating indexes...';
    
    -- Index for queue processing (Status + QueuedAt)
    IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_ContainerScanQueues_Status_QueuedAt' AND object_id = OBJECT_ID('ContainerScanQueues'))
    BEGIN
        CREATE INDEX [IX_ContainerScanQueues_Status_QueuedAt] 
        ON [ContainerScanQueues]([Status], [QueuedAt]);
        PRINT 'Index IX_ContainerScanQueues_Status_QueuedAt created.';
    END
    
    -- Index for container lookups
    IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_ContainerScanQueues_ContainerNumber_ScannerType' AND object_id = OBJECT_ID('ContainerScanQueues'))
    BEGIN
        CREATE INDEX [IX_ContainerScanQueues_ContainerNumber_ScannerType] 
        ON [ContainerScanQueues]([ContainerNumber], [ScannerType]);
        PRINT 'Index IX_ContainerScanQueues_ContainerNumber_ScannerType created.';
    END
    
    -- Index for InspectionId lookups (non-filtered for SQL Server 2014 compatibility)
    IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_ContainerScanQueues_InspectionId' AND object_id = OBJECT_ID('ContainerScanQueues'))
    BEGIN
        CREATE INDEX [IX_ContainerScanQueues_InspectionId] 
        ON [ContainerScanQueues]([InspectionId]);
        PRINT 'Index IX_ContainerScanQueues_InspectionId created.';
    END
    
    -- Index for priority ordering
    IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_ContainerScanQueues_Priority' AND object_id = OBJECT_ID('ContainerScanQueues'))
    BEGIN
        CREATE INDEX [IX_ContainerScanQueues_Priority] 
        ON [ContainerScanQueues]([Priority]);
        PRINT 'Index IX_ContainerScanQueues_Priority created.';
    END
    
    -- Index for scanner type statistics
    IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_ContainerScanQueues_ScannerType' AND object_id = OBJECT_ID('ContainerScanQueues'))
    BEGIN
        CREATE INDEX [IX_ContainerScanQueues_ScannerType] 
        ON [ContainerScanQueues]([ScannerType]);
        PRINT 'Index IX_ContainerScanQueues_ScannerType created.';
    END
    
    -- Index for deduplication checks
    IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_ContainerScanQueues_ContainerNumber_ScannerType_InspectionId' AND object_id = OBJECT_ID('ContainerScanQueues'))
    BEGIN
        CREATE INDEX [IX_ContainerScanQueues_ContainerNumber_ScannerType_InspectionId] 
        ON [ContainerScanQueues]([ContainerNumber], [ScannerType], [InspectionId]);
        PRINT 'Index IX_ContainerScanQueues_ContainerNumber_ScannerType_InspectionId created.';
    END
    
    PRINT 'All indexes created successfully.';
    
    -- Record migration in EF Migrations History (if table exists)
    IF EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'[__EFMigrationsHistory]') AND type in (N'U'))
    BEGIN
        IF NOT EXISTS (SELECT * FROM [__EFMigrationsHistory] WHERE [MigrationId] = '20260110210159_AddContainerScanQueueTable')
        BEGIN
            INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
            VALUES ('20260110210159_AddContainerScanQueueTable', '8.0.0');
            PRINT 'Migration recorded in __EFMigrationsHistory.';
        END
        ELSE
        BEGIN
            PRINT 'Migration already recorded in __EFMigrationsHistory.';
        END
    END
    ELSE
    BEGIN
        PRINT 'Warning: __EFMigrationsHistory table does not exist. Migration not recorded.';
    END
    
    PRINT 'Migration completed successfully!';
END
ELSE
BEGIN
    PRINT 'ContainerScanQueues table already exists. Skipping creation.';
    
    -- Still record migration if not already recorded
    IF EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'[__EFMigrationsHistory]') AND type in (N'U'))
    BEGIN
        IF NOT EXISTS (SELECT * FROM [__EFMigrationsHistory] WHERE [MigrationId] = '20260110210159_AddContainerScanQueueTable')
        BEGIN
            INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
            VALUES ('20260110210159_AddContainerScanQueueTable', '8.0.0');
            PRINT 'Migration recorded in __EFMigrationsHistory.';
        END
    END
END
GO

-- Verify table creation
IF EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'[ContainerScanQueues]') AND type in (N'U'))
BEGIN
    PRINT '';
    PRINT 'Verification: ContainerScanQueues table exists.';
    PRINT 'Column count: ' + CAST((SELECT COUNT(*) FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'ContainerScanQueues') AS VARCHAR);
    PRINT 'Index count: ' + CAST((SELECT COUNT(*) FROM sys.indexes WHERE object_id = OBJECT_ID('ContainerScanQueues')) AS VARCHAR);
END
ELSE
BEGIN
    PRINT 'ERROR: ContainerScanQueues table was not created!';
END
GO


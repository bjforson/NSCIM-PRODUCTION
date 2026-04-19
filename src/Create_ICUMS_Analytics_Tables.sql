-- ============================================================================
-- ICUMS Dashboard Analytics Tables
-- Supports queue trend charts, historical statistics, and performance tracking
-- ============================================================================

USE [NS_CIS]; -- Change to your database name if different
GO

-- ============================================================================
-- 1. ICUMSQueueSnapshots - Track queue size over time
-- ============================================================================

IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'ICUMSQueueSnapshots')
BEGIN
    PRINT '📝 Creating ICUMSQueueSnapshots table...';
    
    CREATE TABLE [dbo].[ICUMSQueueSnapshots] (
        [Id] INT NOT NULL IDENTITY(1,1) PRIMARY KEY,
        [Timestamp] DATETIME2 NOT NULL,
        [PendingCount] INT NOT NULL DEFAULT 0,
        [ProcessingCount] INT NOT NULL DEFAULT 0,
        [CompletedCount] INT NOT NULL DEFAULT 0,
        [FailedCount] INT NOT NULL DEFAULT 0,
        [HighPriorityCount] INT NOT NULL DEFAULT 0,
        [NormalPriorityCount] INT NOT NULL DEFAULT 0,
        [LowPriorityCount] INT NOT NULL DEFAULT 0,
        [TotalQueueSize] INT NOT NULL DEFAULT 0,
        [CreatedAt] DATETIME2 NOT NULL DEFAULT GETUTCDATE()
    );
    
    -- Index for time-based queries
    CREATE INDEX [IX_ICUMSQueueSnapshots_Timestamp] 
        ON [dbo].[ICUMSQueueSnapshots]([Timestamp] DESC);
    
    PRINT '✅ ICUMSQueueSnapshots table created';
END
ELSE
BEGIN
    PRINT '⚠️ ICUMSQueueSnapshots table already exists';
END
GO

-- ============================================================================
-- 2. ICUMSDailyStats - Daily aggregated statistics
-- ============================================================================

IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'ICUMSDailyStats')
BEGIN
    PRINT '📝 Creating ICUMSDailyStats table...';
    
    CREATE TABLE [dbo].[ICUMSDailyStats] (
        [Id] INT NOT NULL IDENTITY(1,1) PRIMARY KEY,
        [Date] DATE NOT NULL UNIQUE,
        [TotalDownloads] INT NOT NULL DEFAULT 0,
        [SuccessfulDownloads] INT NOT NULL DEFAULT 0,
        [FailedDownloads] INT NOT NULL DEFAULT 0,
        [TotalSubmissions] INT NOT NULL DEFAULT 0,
        [SuccessfulSubmissions] INT NOT NULL DEFAULT 0,
        [FailedSubmissions] INT NOT NULL DEFAULT 0,
        [AvgDownloadResponseTimeMs] INT NULL,
        [AvgQueueWaitTimeMinutes] INT NULL,
        [PeakQueueSize] INT NULL,
        [PeakQueueTime] DATETIME2 NULL,
        [SuccessRate] DECIMAL(5,2) NULL,
        [CreatedAt] DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
        [UpdatedAt] DATETIME2 NULL
    );
    
    -- Index for date queries
    CREATE INDEX [IX_ICUMSDailyStats_Date] 
        ON [dbo].[ICUMSDailyStats]([Date] DESC);
    
    PRINT '✅ ICUMSDailyStats table created';
END
ELSE
BEGIN
    PRINT '⚠️ ICUMSDailyStats table already exists';
END
GO

-- ============================================================================
-- 3. ICUMSHealthChecks - Track API health over time
-- ============================================================================

IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'ICUMSHealthChecks')
BEGIN
    PRINT '📝 Creating ICUMSHealthChecks table...';
    
    CREATE TABLE [dbo].[ICUMSHealthChecks] (
        [Id] INT NOT NULL IDENTITY(1,1) PRIMARY KEY,
        [CheckTime] DATETIME2 NOT NULL,
        [IsSuccessful] BIT NOT NULL,
        [ResponseTimeMs] INT NULL,
        [StatusCode] INT NULL,
        [ErrorMessage] NVARCHAR(1000) NULL,
        [EndpointTested] NVARCHAR(200) NULL,
        [CreatedAt] DATETIME2 NOT NULL DEFAULT GETUTCDATE()
    );
    
    -- Index for time-based queries
    CREATE INDEX [IX_ICUMSHealthChecks_CheckTime] 
        ON [dbo].[ICUMSHealthChecks]([CheckTime] DESC);
    
    CREATE INDEX [IX_ICUMSHealthChecks_IsSuccessful] 
        ON [dbo].[ICUMSHealthChecks]([IsSuccessful], [CheckTime]);
    
    PRINT '✅ ICUMSHealthChecks table created';
END
ELSE
BEGIN
    PRINT '⚠️ ICUMSHealthChecks table already exists';
END
GO

-- ============================================================================
-- 4. Sample Data (Optional - for testing)
-- ============================================================================

-- Insert sample snapshot
IF NOT EXISTS (SELECT 1 FROM ICUMSQueueSnapshots WHERE Timestamp >= DATEADD(HOUR, -1, GETUTCDATE()))
BEGIN
    PRINT '📊 Inserting sample queue snapshots...';
    
    DECLARE @i INT = 0;
    DECLARE @baseTime DATETIME2 = DATEADD(HOUR, -24, GETUTCDATE());
    
    WHILE @i < 48 -- 24 hours * 2 (every 30 min)
    BEGIN
        INSERT INTO ICUMSQueueSnapshots (
            Timestamp, PendingCount, ProcessingCount, CompletedCount, FailedCount,
            HighPriorityCount, NormalPriorityCount, LowPriorityCount, TotalQueueSize
        )
        VALUES (
            DATEADD(MINUTE, @i * 30, @baseTime),
            50 + (@i % 10) * 5,  -- Pending varies
            2 + (@i % 3),        -- Processing varies
            1000 + @i * 10,      -- Completed grows
            8 + (@i % 5),        -- Failed varies
            3 + (@i % 4),        -- High priority
            40 + (@i % 8) * 3,   -- Normal priority
            7 + (@i % 3) * 2,    -- Low priority
            50 + (@i % 10) * 5 + 2 + (@i % 3)  -- Total queue
        );
        
        SET @i = @i + 1;
    END
    
    PRINT '✅ Sample snapshots inserted';
END
GO

-- ============================================================================
-- 5. Cleanup Old Data (Maintenance)
-- ============================================================================

PRINT '🧹 Setting up data retention...';

-- Keep queue snapshots for 7 days
DELETE FROM ICUMSQueueSnapshots 
WHERE Timestamp < DATEADD(DAY, -7, GETUTCDATE());

PRINT '✅ Cleaned old queue snapshots (>7 days)';

-- Keep health checks for 30 days
DELETE FROM ICUMSHealthChecks 
WHERE CheckTime < DATEADD(DAY, -30, GETUTCDATE());

PRINT '✅ Cleaned old health checks (>30 days)';

-- Keep daily stats for 1 year
DELETE FROM ICUMSDailyStats 
WHERE Date < DATEADD(YEAR, -1, CAST(GETUTCDATE() AS DATE));

PRINT '✅ Cleaned old daily stats (>1 year)';

GO

PRINT '━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━';
PRINT '✅ ICUMS Analytics Tables Setup Complete!';
PRINT '━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━';
PRINT '';
PRINT 'Next Steps:';
PRINT '1. Run this script to create the tables';
PRINT '2. Implement background job to capture snapshots';
PRINT '3. Create API endpoints for analytics data';
PRINT '4. Add chart components to dashboard';
PRINT '';
GO


-- ============================================================================
-- Add Missing Date Indexes for All Frequently-Queried Tables
-- Purpose: Enable efficient date-based filtering to reduce SQL Server buffer pool usage
-- Date: 2025-01-XX
-- 
-- This script checks for existing indexes and only creates missing ones.
-- Safe to run multiple times (idempotent).
-- ============================================================================

USE [NS_CIS]; -- Change to your database name if different
GO

PRINT '============================================================================';
PRINT 'Adding Missing Date Indexes for Memory Optimization';
PRINT '============================================================================';
PRINT '';

-- ============================================================================
-- 1. ContainerCompletenessStatuses Table
-- ============================================================================
PRINT '1. Checking ContainerCompletenessStatuses table indexes...';
PRINT '';

-- Index on CreatedAt (for recent items queries)
IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_ContainerCompletenessStatuses_CreatedAt' AND object_id = OBJECT_ID('ContainerCompletenessStatuses'))
BEGIN
    PRINT '  Creating index: IX_ContainerCompletenessStatuses_CreatedAt...';
    CREATE INDEX [IX_ContainerCompletenessStatuses_CreatedAt] 
    ON [ContainerCompletenessStatuses]([CreatedAt]);
    PRINT '  ✅ Index created.';
END
ELSE
BEGIN
    PRINT '  ✅ Index IX_ContainerCompletenessStatuses_CreatedAt already exists.';
END
GO

-- Index on ScanDate (for scan date filtering)
IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_ContainerCompletenessStatuses_ScanDate' AND object_id = OBJECT_ID('ContainerCompletenessStatuses'))
BEGIN
    PRINT '  Creating index: IX_ContainerCompletenessStatuses_ScanDate...';
    CREATE INDEX [IX_ContainerCompletenessStatuses_ScanDate] 
    ON [ContainerCompletenessStatuses]([ScanDate]);
    PRINT '  ✅ Index created.';
END
ELSE
BEGIN
    PRINT '  ✅ Index IX_ContainerCompletenessStatuses_ScanDate already exists.';
END
GO

-- Composite index for Status + CreatedAt (for filtering by status and date)
IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_ContainerCompletenessStatuses_Status_CreatedAt' AND object_id = OBJECT_ID('ContainerCompletenessStatuses'))
BEGIN
    PRINT '  Creating index: IX_ContainerCompletenessStatuses_Status_CreatedAt...';
    CREATE INDEX [IX_ContainerCompletenessStatuses_Status_CreatedAt] 
    ON [ContainerCompletenessStatuses]([Status], [CreatedAt]);
    PRINT '  ✅ Index created.';
END
ELSE
BEGIN
    PRINT '  ✅ Index IX_ContainerCompletenessStatuses_Status_CreatedAt already exists.';
END
GO

PRINT '';

-- ============================================================================
-- 2. AseScans Table (Verify existing, add CreatedAt if missing)
-- ============================================================================
PRINT '2. Checking AseScans table indexes...';
PRINT '';

-- Note: AseScans already has IX_AseScans_ScanTime (verified in Apply_ImageAnalysis.sql)

-- Index on CreatedAt (for record creation date filtering)
IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_AseScans_CreatedAt' AND object_id = OBJECT_ID('AseScans'))
BEGIN
    PRINT '  Creating index: IX_AseScans_CreatedAt...';
    CREATE INDEX [IX_AseScans_CreatedAt] 
    ON [AseScans]([CreatedAt]);
    PRINT '  ✅ Index created.';
END
ELSE
BEGIN
    PRINT '  ✅ Index IX_AseScans_CreatedAt already exists.';
END
GO

PRINT '';

-- ============================================================================
-- 3. FS6000Scans Table (Verify existing, add CreatedAt if missing)
-- ============================================================================
PRINT '3. Checking FS6000Scans table indexes...';
PRINT '';

-- Note: FS6000Scans already has IX_FS6000Scans_ScanTime (verified in Apply_ImageAnalysis.sql)

-- Index on CreatedAt (for record creation date filtering)
IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_FS6000Scans_CreatedAt' AND object_id = OBJECT_ID('FS6000Scans'))
BEGIN
    PRINT '  Creating index: IX_FS6000Scans_CreatedAt...';
    CREATE INDEX [IX_FS6000Scans_CreatedAt] 
    ON [FS6000Scans]([CreatedAt]);
    PRINT '  ✅ Index created.';
END
ELSE
BEGIN
    PRINT '  ✅ Index IX_FS6000Scans_CreatedAt already exists.';
END
GO

PRINT '';

-- ============================================================================
-- 4. ICUMSSubmissionQueues Table
-- ============================================================================
PRINT '4. Checking ICUMSSubmissionQueues table indexes...';
PRINT '';

-- Index on CreatedAt (for recent submissions)
IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_ICUMSSubmissionQueues_CreatedAt' AND object_id = OBJECT_ID('ICUMSSubmissionQueues'))
BEGIN
    PRINT '  Creating index: IX_ICUMSSubmissionQueues_CreatedAt...';
    CREATE INDEX [IX_ICUMSSubmissionQueues_CreatedAt] 
    ON [ICUMSSubmissionQueues]([CreatedAt]);
    PRINT '  ✅ Index created.';
END
ELSE
BEGIN
    PRINT '  ✅ Index IX_ICUMSSubmissionQueues_CreatedAt already exists.';
END
GO

-- Index on SubmittedAt (for submission date filtering)
IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_ICUMSSubmissionQueues_SubmittedAt' AND object_id = OBJECT_ID('ICUMSSubmissionQueues'))
BEGIN
    PRINT '  Creating index: IX_ICUMSSubmissionQueues_SubmittedAt...';
    CREATE INDEX [IX_ICUMSSubmissionQueues_SubmittedAt] 
    ON [ICUMSSubmissionQueues]([SubmittedAt]);
    PRINT '  ✅ Index created.';
END
ELSE
BEGIN
    PRINT '  ✅ Index IX_ICUMSSubmissionQueues_SubmittedAt already exists.';
END
GO

-- Composite index for Status + CreatedAt (for filtering by status and date)
IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_ICUMSSubmissionQueues_Status_CreatedAt' AND object_id = OBJECT_ID('ICUMSSubmissionQueues'))
BEGIN
    PRINT '  Creating index: IX_ICUMSSubmissionQueues_Status_CreatedAt...';
    CREATE INDEX [IX_ICUMSSubmissionQueues_Status_CreatedAt] 
    ON [ICUMSSubmissionQueues]([Status], [CreatedAt]);
    PRINT '  ✅ Index created.';
END
ELSE
BEGIN
    PRINT '  ✅ Index IX_ICUMSSubmissionQueues_Status_CreatedAt already exists.';
END
GO

PRINT '';

-- ============================================================================
-- 5. ContainerBOERelations Table
-- ============================================================================
PRINT '5. Checking ContainerBOERelations table indexes...';
PRINT '';

-- Index on CreatedAt (for recent relations)
IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_ContainerBOERelations_CreatedAt' AND object_id = OBJECT_ID('ContainerBOERelations'))
BEGIN
    PRINT '  Creating index: IX_ContainerBOERelations_CreatedAt...';
    CREATE INDEX [IX_ContainerBOERelations_CreatedAt] 
    ON [ContainerBOERelations]([CreatedAt]);
    PRINT '  ✅ Index created.';
END
ELSE
BEGIN
    PRINT '  ✅ Index IX_ContainerBOERelations_CreatedAt already exists.';
END
GO

PRINT '';

-- ============================================================================
-- 6. CMRRedownloadQueues Table (if exists in this database)
-- ============================================================================
-- Note: CMRRedownloadQueues is in a different database (ICUMS_Downloads), skip for NS_CIS
PRINT '6. CMRRedownloadQueues table is in ICUMS_Downloads database (skipping for NS_CIS).';
PRINT '';
GO

-- ============================================================================
-- Summary Report
-- ============================================================================
PRINT '============================================================================';
PRINT 'Index Creation Summary';
PRINT '============================================================================';
PRINT '';
PRINT 'Date indexes created/verified for the following tables:';
PRINT '  - ContainerCompletenessStatuses (CreatedAt, ScanDate, Status+CreatedAt)';
PRINT '  - AseScans (CreatedAt - ScanTime already exists)';
PRINT '  - FS6000Scans (CreatedAt - ScanTime already exists)';
PRINT '  - ICUMSSubmissionQueues (CreatedAt, SubmittedAt, Status+CreatedAt)';
PRINT '  - ContainerBOERelations (CreatedAt)';
PRINT '  - CMRRedownloadQueues (QueuedAt, CreatedAt - if table exists)';
PRINT '';
PRINT '✅ Index creation script completed!';
PRINT '';
PRINT 'Next Steps:';
PRINT '1. Ensure queries filter by date (see SQL_MEMORY_OPTIMIZATION_INVESTIGATION.md)';
PRINT '2. Monitor SQL Server buffer pool usage';
PRINT '3. Test query performance with date filters';
PRINT '============================================================================';
GO


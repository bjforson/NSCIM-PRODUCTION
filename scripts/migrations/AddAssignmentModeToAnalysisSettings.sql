-- ============================================
-- Add AssignmentMode and AutoAssignStrategy to AnalysisSettings
-- Migration: IAS Three-Mode Assignment Support
-- ============================================

USE NS_CIS;
GO

-- Step 1: Add new columns
IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('AnalysisSettings') AND name = 'AssignmentMode')
BEGIN
    ALTER TABLE AnalysisSettings
    ADD AssignmentMode VARCHAR(20) NOT NULL DEFAULT 'Manual',
        AutoAssignStrategy VARCHAR(20) NOT NULL DEFAULT 'RoundRobin';
    
    PRINT '✅ Added AssignmentMode and AutoAssignStrategy columns';
END
ELSE
BEGIN
    PRINT '⚠️ AssignmentMode column already exists';
END
GO

-- Step 2: Migrate existing AutoAssign flag to AssignmentMode
PRINT '';
PRINT '📊 Migrating existing AutoAssign flag to AssignmentMode...';
GO

UPDATE AnalysisSettings
SET AssignmentMode = CASE 
    WHEN AutoAssign = 1 THEN 'Auto'
    ELSE 'Manual'
END,
    AutoAssignStrategy = 'RoundRobin',
    UpdatedAtUtc = SYSUTCDATETIME()
WHERE AssignmentMode IS NULL OR AssignmentMode = '';

PRINT CONCAT('✅ Migrated ', @@ROWCOUNT, ' AnalysisSettings row(s)');
GO

-- Step 3: Verify migration
SELECT 
    Id,
    Enabled,
    AssignmentMode,
    AutoAssignStrategy,
    AutoAssign, -- Keep for backward compatibility
    LeaseMinutes,
    MaxConcurrentPerUser,
    CreatedAtUtc,
    UpdatedAtUtc
FROM AnalysisSettings;

PRINT '';
PRINT '✅ Migration complete!';
PRINT '   AssignmentMode: Auto | Manual | UserClaim';
PRINT '   AutoAssignStrategy: RoundRobin | LeastLoaded';
GO


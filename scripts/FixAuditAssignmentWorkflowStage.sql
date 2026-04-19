-- Fix Audit Assignment Issue: Update WorkflowStage to 'Audit' for AnalystCompleted Groups
-- This fixes the issue where WorkflowStage is 'Completed' instead of 'Audit'

PRINT '========================================';
PRINT 'Fixing WorkflowStage for Audit Assignment';
PRINT '========================================';
PRINT '';

-- Step 1: Check current state
PRINT 'Step 1: Checking current WorkflowStage for AnalystCompleted groups...';
SELECT 
    g.GroupIdentifier,
    g.Status as GroupStatus,
    c.WorkflowStage,
    COUNT(*) as ContainerCount
FROM AnalysisGroups g
INNER JOIN ContainerCompletenessStatuses c ON c.GroupIdentifier = g.GroupIdentifier
WHERE g.Status = 'AnalystCompleted'
GROUP BY g.GroupIdentifier, g.Status, c.WorkflowStage
ORDER BY g.GroupIdentifier, c.WorkflowStage;

-- Step 2: Update WorkflowStage to 'Audit' for AnalystCompleted groups
-- Only update containers that are not already 'Completed' (preserve truly completed ones)
PRINT '';
PRINT 'Step 2: Updating WorkflowStage to ''Audit'' for AnalystCompleted groups...';

-- Update WorkflowStage to 'Audit' for AnalystCompleted groups
-- This is safe because AnalystCompleted groups should be in Audit stage, not Completed
UPDATE c
SET c.WorkflowStage = 'Audit',
    c.UpdatedAt = SYSUTCDATETIME()
FROM ContainerCompletenessStatuses c
INNER JOIN AnalysisGroups g ON c.GroupIdentifier = g.GroupIdentifier
WHERE g.Status = 'AnalystCompleted'
    AND c.WorkflowStage <> 'Audit';  -- Update all containers that aren't already 'Audit'

DECLARE @RowsAffected INT = @@ROWCOUNT;
PRINT 'Updated ' + CAST(@RowsAffected AS VARCHAR(10)) + ' container records to WorkflowStage = ''Audit''';
PRINT '';

-- Step 3: Verify the update
PRINT 'Step 3: Verifying WorkflowStage update...';
SELECT 
    g.GroupIdentifier,
    g.Status as GroupStatus,
    c.WorkflowStage,
    COUNT(*) as ContainerCount
FROM AnalysisGroups g
INNER JOIN ContainerCompletenessStatuses c ON c.GroupIdentifier = g.GroupIdentifier
WHERE g.Status = 'AnalystCompleted'
GROUP BY g.GroupIdentifier, g.Status, c.WorkflowStage
ORDER BY g.GroupIdentifier, c.WorkflowStage;

-- Step 4: Check how many groups are now ready for audit assignment
PRINT '';
PRINT 'Step 4: Checking groups ready for audit assignment...';
SELECT 
    g.GroupIdentifier,
    COUNT(c.Id) as TotalContainers,
    SUM(CASE WHEN c.WorkflowStage = 'Audit' THEN 1 ELSE 0 END) as AuditCount,
    SUM(CASE WHEN c.WorkflowStage = 'Completed' THEN 1 ELSE 0 END) as CompletedCount
FROM AnalysisGroups g
INNER JOIN ContainerCompletenessStatuses c ON c.GroupIdentifier = g.GroupIdentifier
WHERE g.Status = 'AnalystCompleted'
GROUP BY g.GroupIdentifier
HAVING COUNT(c.Id) > 0 
    AND SUM(CASE WHEN c.WorkflowStage = 'Audit' THEN 1 ELSE 0 END) = COUNT(c.Id)  -- All containers in Audit stage
ORDER BY g.GroupIdentifier;

PRINT '';
PRINT '========================================';
PRINT 'Fix Complete!';
PRINT '========================================';
PRINT '';
PRINT 'Next steps:';
PRINT '1. Verify AssignmentMode is set to ''Auto'' in AnalysisSettings';
PRINT '2. Check that Audit users exist and are active';
PRINT '3. Monitor AssignmentWorker logs for audit assignment activity';
PRINT '';

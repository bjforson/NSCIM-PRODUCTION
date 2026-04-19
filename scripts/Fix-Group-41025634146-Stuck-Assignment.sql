-- Fix group 41025634146 stuck in Janalyst's assignments
-- Run against NS_CIS database
--
-- This script:
-- 1. Updates group status to AnalystCompleted (or Completed if fully audited)
-- 2. Releases Active Analyst assignments for this group
-- 3. Updates WorkflowStage to Audit on ContainerCompletenessStatus
--
-- After running: Janalyst may need to refresh in ~45 seconds (GetMyAssignments cache TTL)

DECLARE @BaseGroupId NVARCHAR(50) = '41025634146';

PRINT '========================================';
PRINT 'Fix Group 41025634146 - Stuck Assignment';
PRINT '========================================';

-- 0. Show current state
PRINT '';
PRINT 'BEFORE:';
SELECT 'AnalysisGroups' AS [Table], g.Id, g.GroupIdentifier, g.Status
FROM AnalysisGroups g
WHERE g.GroupIdentifier = @BaseGroupId OR g.GroupIdentifier LIKE @BaseGroupId + '[_]%';

SELECT 'Assignments' AS [Table], a.Id, a.AssignedTo, a.Role, a.State
FROM AnalysisAssignments a
JOIN AnalysisGroups g ON a.GroupId = g.Id
WHERE (g.GroupIdentifier = @BaseGroupId OR g.GroupIdentifier LIKE @BaseGroupId + '[_]%')
  AND a.State = 'Active';

SELECT 'ContainerCompletenessStatus' AS [Table], WorkflowStage, COUNT(*) AS Cnt
FROM ContainerCompletenessStatuses
WHERE GroupIdentifier = @BaseGroupId
GROUP BY WorkflowStage;

-- 1. Update WorkflowStage to Audit for containers still in ImageAnalysis/Pending
UPDATE c
SET c.WorkflowStage = 'Audit', c.UpdatedAt = SYSUTCDATETIME()
FROM ContainerCompletenessStatuses c
WHERE c.GroupIdentifier = @BaseGroupId
  AND (c.WorkflowStage IN ('ImageAnalysis', 'Pending', '') OR c.WorkflowStage IS NULL);

PRINT 'Step 1: Updated ' + CAST(@@ROWCOUNT AS VARCHAR(10)) + ' ContainerCompletenessStatus rows';

-- 2. Update AnalysisGroup Status - include 'Assigned' for legacy values
UPDATE g
SET g.Status = 'AnalystCompleted', g.UpdatedAtUtc = SYSUTCDATETIME()
FROM AnalysisGroups g
WHERE (g.GroupIdentifier = @BaseGroupId OR g.GroupIdentifier LIKE @BaseGroupId + '[_]%')
  AND g.Status IN ('Ready', 'AnalystAssigned', 'Assigned');

PRINT 'Step 2: Updated ' + CAST(@@ROWCOUNT AS VARCHAR(10)) + ' AnalysisGroup rows';

-- 3. Release Active Analyst assignments for this group
UPDATE a
SET a.State = 'Released', a.UpdatedAtUtc = SYSUTCDATETIME()
FROM AnalysisAssignments a
JOIN AnalysisGroups g ON a.GroupId = g.Id
WHERE (g.GroupIdentifier = @BaseGroupId OR g.GroupIdentifier LIKE @BaseGroupId + '[_]%')
  AND a.Role = 'Analyst'
  AND a.State = 'Active';

PRINT 'Step 3: Released ' + CAST(@@ROWCOUNT AS VARCHAR(10)) + ' Analyst assignments';

-- 4. Also release Audit assignments if group is fully completed (optional - uncomment if needed)
-- UPDATE a SET a.State = 'Released', a.UpdatedAtUtc = SYSUTCDATETIME()
-- FROM AnalysisAssignments a JOIN AnalysisGroups g ON a.GroupId = g.Id
-- WHERE (g.GroupIdentifier = @BaseGroupId OR g.GroupIdentifier LIKE @BaseGroupId + '[_]%')
--   AND a.Role = 'Audit' AND a.State = 'Active';

PRINT '';
PRINT 'AFTER:';
SELECT 'Assignments (should be Released)' AS [Table], a.Id, a.AssignedTo, a.Role, a.State
FROM AnalysisAssignments a
JOIN AnalysisGroups g ON a.GroupId = g.Id
WHERE (g.GroupIdentifier = @BaseGroupId OR g.GroupIdentifier LIKE @BaseGroupId + '[_]%')
ORDER BY a.CreatedAtUtc DESC;

PRINT '';
PRINT 'Done. Janalyst: refresh assignments page (cache clears in ~45s).';

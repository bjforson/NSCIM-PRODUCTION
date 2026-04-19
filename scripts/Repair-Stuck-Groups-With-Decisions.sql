-- H2 Repair Script: Fix groups where all containers have ImageAnalysis decisions
-- but WorkflowStage was never updated to 'Audit' (root cause: C1 - date-suffixed GroupIdentifier bug)
--
-- This script:
-- 1. Finds groups where all containers have decisions (Normal/Abnormal) but WorkflowStage is still ImageAnalysis/Pending
-- 2. Updates ContainerCompletenessStatus.WorkflowStage to 'Audit'
-- 3. Updates AnalysisGroup.Status to 'AnalystCompleted' where applicable
-- 4. Releases stale Analyst assignments
--
-- Run against NS_CIS database. Safe to run multiple times (idempotent).

PRINT '========================================';
PRINT 'H2: Repair Stuck Groups With Decisions';
PRINT '========================================';
PRINT '';

-- Step 1: Identify groups where all containers have decisions but WorkflowStage != Audit
PRINT 'Step 1: Identifying affected groups...';

;WITH GroupDecisionStats AS (
    SELECT 
        c.GroupIdentifier,
        COUNT(DISTINCT c.ContainerNumber) AS TotalContainers,
        COUNT(DISTINCT CASE 
            WHEN d.Decision IN ('Normal', 'Abnormal') 
            THEN c.ContainerNumber 
        END) AS DecidedContainers,
        MAX(CASE WHEN c.WorkflowStage IN ('ImageAnalysis', 'Pending', '') OR c.WorkflowStage IS NULL THEN 1 ELSE 0 END) AS HasStuckContainers
    FROM ContainerCompletenessStatuses c
    LEFT JOIN ImageAnalysisDecisions d ON d.ContainerNumber = c.ContainerNumber
        AND (d.ScannerType = c.ScannerType OR (d.ScannerType IS NULL AND c.ScannerType IS NULL))
        AND d.Decision IN ('Normal', 'Abnormal')
    WHERE c.Status LIKE 'Complete%'
        AND c.GroupIdentifier IS NOT NULL
        AND LTRIM(RTRIM(ISNULL(c.GroupIdentifier, ''))) <> ''
    GROUP BY c.GroupIdentifier
    HAVING COUNT(DISTINCT c.ContainerNumber) > 0
        AND COUNT(DISTINCT c.ContainerNumber) = COUNT(DISTINCT CASE 
            WHEN d.Decision IN ('Normal', 'Abnormal') THEN c.ContainerNumber END)
        AND MAX(CASE WHEN c.WorkflowStage IN ('ImageAnalysis', 'Pending', '') OR c.WorkflowStage IS NULL THEN 1 ELSE 0 END) = 1
)
SELECT 
    gds.GroupIdentifier,
    gds.TotalContainers,
    gds.DecidedContainers
INTO #StuckGroups
FROM GroupDecisionStats gds;

DECLARE @StuckCount INT = (SELECT COUNT(*) FROM #StuckGroups);
PRINT 'Found ' + ISNULL(CAST(@StuckCount AS VARCHAR(10)), '0') + ' stuck groups';
IF @StuckCount > 0
    SELECT * FROM #StuckGroups;
PRINT '';

-- Step 2: Update ContainerCompletenessStatus.WorkflowStage to 'Audit'
PRINT 'Step 2: Updating WorkflowStage to ''Audit'' for affected containers...';

UPDATE c
SET 
    c.WorkflowStage = 'Audit',
    c.UpdatedAt = SYSUTCDATETIME()
FROM ContainerCompletenessStatuses c
INNER JOIN #StuckGroups sg ON c.GroupIdentifier = sg.GroupIdentifier
WHERE c.WorkflowStage IN ('ImageAnalysis', 'Pending', '') OR c.WorkflowStage IS NULL;

DECLARE @ContainersUpdated INT = @@ROWCOUNT;
PRINT 'Updated ' + CAST(@ContainersUpdated AS VARCHAR(10)) + ' container records';
PRINT '';

-- Step 3: Update AnalysisGroup.Status to AnalystCompleted
-- Match AnalysisGroups by GroupIdentifier = base OR GroupIdentifier LIKE base + '_%' (date-suffix)
PRINT 'Step 3: Updating AnalysisGroup.Status to ''AnalystCompleted''...';

UPDATE g
SET 
    g.Status = 'AnalystCompleted',
    g.UpdatedAtUtc = SYSUTCDATETIME()
FROM AnalysisGroups g
WHERE g.Status IN ('Ready', 'AnalystAssigned', 'Assigned')  -- 'Assigned' = legacy
    AND EXISTS (
        SELECT 1 FROM #StuckGroups sg 
        WHERE g.GroupIdentifier = sg.GroupIdentifier 
           OR (sg.GroupIdentifier IS NOT NULL AND g.GroupIdentifier LIKE sg.GroupIdentifier + '[_]%')
    );

DECLARE @GroupsUpdated INT = @@ROWCOUNT;
PRINT 'Updated ' + CAST(@GroupsUpdated AS VARCHAR(10)) + ' AnalysisGroup records';
PRINT '';

-- Step 4: Release stale Analyst assignments for these groups
PRINT 'Step 4: Releasing stale Analyst assignments...';

UPDATE a
SET 
    a.State = 'Released',
    a.UpdatedAtUtc = SYSUTCDATETIME()
FROM AnalysisAssignments a
INNER JOIN AnalysisGroups g ON a.GroupId = g.Id
WHERE a.Role = 'Analyst'
    AND a.State = 'Active'
    AND g.Status = 'AnalystCompleted'
    AND EXISTS (
        SELECT 1 FROM #StuckGroups sg 
        WHERE g.GroupIdentifier = sg.GroupIdentifier 
           OR (sg.GroupIdentifier IS NOT NULL AND g.GroupIdentifier LIKE sg.GroupIdentifier + '[_]%')
    );

DECLARE @AssignmentsReleased INT = @@ROWCOUNT;
PRINT 'Released ' + CAST(@AssignmentsReleased AS VARCHAR(10)) + ' assignments';
PRINT '';

-- Cleanup
DROP TABLE #StuckGroups;

PRINT '========================================';
PRINT 'Repair Complete!';
PRINT '  Containers updated: ' + CAST(@ContainersUpdated AS VARCHAR(10));
PRINT '  Groups updated: ' + CAST(@GroupsUpdated AS VARCHAR(10));
PRINT '  Assignments released: ' + CAST(@AssignmentsReleased AS VARCHAR(10));
PRINT '========================================';

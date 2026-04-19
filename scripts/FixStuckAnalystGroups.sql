-- Fix Script: Move stuck AnalystAssigned groups to AnalystCompleted
-- Run this after diagnosing with DiagnoseStuckAnalystGroups.sql

-- Step 1: Find and fix groups where all containers have decisions but status is still AnalystAssigned
-- This handles cases where GroupIdentifier was missing or mismatched

WITH GroupCompletion AS (
    SELECT 
        g.Id AS GroupId,
        g.GroupIdentifier,
        g.Status,
        COUNT(DISTINCT r.ContainerNumber) AS TotalContainers,
        COUNT(DISTINCT CASE 
            WHEN d.Decision IN ('Normal', 'Abnormal') 
            THEN d.ContainerNumber 
        END) AS DecidedContainers,
        -- Also check decisions by matching ContainerNumber + ScannerType (even if GroupIdentifier is missing)
        COUNT(DISTINCT CASE 
            WHEN d2.Decision IN ('Normal', 'Abnormal') 
            THEN d2.ContainerNumber 
        END) AS DecidedContainersByMatch
    FROM AnalysisGroups g
    INNER JOIN AnalysisRecords r ON g.Id = r.GroupId
    -- Check decisions with matching GroupIdentifier
    LEFT JOIN ImageAnalysisDecisions d ON d.GroupIdentifier = g.GroupIdentifier 
        AND d.ContainerNumber = r.ContainerNumber
        AND d.ScannerType = COALESCE(r.ScannerType, g.ScannerType, '')
    -- Also check decisions by ContainerNumber + ScannerType (fallback for missing GroupIdentifier)
    LEFT JOIN ImageAnalysisDecisions d2 ON d2.ContainerNumber = r.ContainerNumber
        AND d2.ScannerType = COALESCE(r.ScannerType, g.ScannerType, '')
        AND d2.Decision IN ('Normal', 'Abnormal')
    WHERE g.Status = 'AnalystAssigned'
    GROUP BY g.Id, g.GroupIdentifier, g.Status
    HAVING COUNT(DISTINCT r.ContainerNumber) > 0
        AND (
            -- All containers have decisions with matching GroupIdentifier
            COUNT(DISTINCT r.ContainerNumber) = COUNT(DISTINCT CASE WHEN d.Decision IN ('Normal', 'Abnormal') THEN d.ContainerNumber END)
            OR
            -- All containers have decisions (even if GroupIdentifier doesn't match)
            COUNT(DISTINCT r.ContainerNumber) = COUNT(DISTINCT CASE WHEN d2.Decision IN ('Normal', 'Abnormal') THEN d2.ContainerNumber END)
        )
)
-- Update groups to AnalystCompleted
UPDATE g
SET 
    g.Status = 'AnalystCompleted',
    g.UpdatedAtUtc = SYSUTCDATETIME()
FROM AnalysisGroups g
INNER JOIN GroupCompletion gc ON g.Id = gc.GroupId
WHERE g.Status = 'AnalystAssigned';

-- Step 2: Release Analyst assignments for these groups
UPDATE a
SET 
    a.State = 'Released',
    a.UpdatedAtUtc = SYSUTCDATETIME()
FROM AnalysisAssignments a
INNER JOIN AnalysisGroups g ON a.GroupId = g.Id
WHERE g.Status = 'AnalystCompleted'
    AND a.Role = 'Analyst'
    AND a.State = 'Active';

-- Step 3: Update WorkflowStage to 'Audit' for containers in these groups
UPDATE c
SET 
    c.WorkflowStage = 'Audit',
    c.UpdatedAt = SYSUTCDATETIME()
FROM ContainerCompletenessStatuses c
INNER JOIN AnalysisRecords r ON c.ContainerNumber = r.ContainerNumber 
    AND c.ScannerType = COALESCE(r.ScannerType, '')
INNER JOIN AnalysisGroups g ON r.GroupId = g.Id
WHERE g.Status = 'AnalystCompleted'
    AND c.WorkflowStage <> 'Audit';

-- Step 4: Fix missing GroupIdentifier in decisions (update decisions to have correct GroupIdentifier)
UPDATE d
SET 
    d.GroupIdentifier = g.GroupIdentifier,
    d.UpdatedAt = SYSUTCDATETIME()
FROM ImageAnalysisDecisions d
INNER JOIN AnalysisRecords r ON d.ContainerNumber = r.ContainerNumber
    AND d.ScannerType = COALESCE(r.ScannerType, '')
INNER JOIN AnalysisGroups g ON r.GroupId = g.Id
WHERE (d.GroupIdentifier IS NULL OR d.GroupIdentifier = '' OR d.GroupIdentifier <> g.GroupIdentifier)
    AND d.Decision IN ('Normal', 'Abnormal')
    AND g.Status IN ('AnalystAssigned', 'AnalystCompleted');

-- Report results
SELECT 
    'Groups Updated' AS Action,
    COUNT(*) AS Count
FROM AnalysisGroups
WHERE Status = 'AnalystCompleted'
    AND UpdatedAtUtc > DATEADD(minute, -5, SYSUTCDATETIME());

SELECT 
    'Assignments Released' AS Action,
    COUNT(*) AS Count
FROM AnalysisAssignments
WHERE State = 'Released'
    AND UpdatedAtUtc > DATEADD(minute, -5, SYSUTCDATETIME());

SELECT 
    'Decisions Fixed' AS Action,
    COUNT(*) AS Count
FROM ImageAnalysisDecisions
WHERE UpdatedAt > DATEADD(minute, -5, SYSUTCDATETIME());


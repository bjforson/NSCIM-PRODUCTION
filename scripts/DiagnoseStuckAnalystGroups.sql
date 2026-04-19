-- Diagnostic Query: Find groups stuck in AnalystAssigned with all containers decided
-- This helps identify why groups aren't moving to AnalystCompleted

-- Step 1: Find groups with Status = AnalystAssigned
SELECT 
    g.Id AS GroupId,
    g.GroupIdentifier,
    g.Status AS GroupStatus,
    g.CreatedAtUtc,
    g.UpdatedAtUtc,
    COUNT(DISTINCT r.ContainerNumber) AS TotalContainers,
    COUNT(DISTINCT d.ContainerNumber) AS DecidedContainers,
    COUNT(DISTINCT CASE WHEN d.Decision IN ('Normal', 'Abnormal') THEN d.ContainerNumber END) AS ValidDecisions,
    STRING_AGG(DISTINCT r.ContainerNumber, ', ') AS AllContainers,
    STRING_AGG(DISTINCT d.ContainerNumber, ', ') AS DecidedContainersList,
    STRING_AGG(DISTINCT d.Decision, ', ') AS Decisions
FROM AnalysisGroups g
LEFT JOIN AnalysisRecords r ON g.Id = r.GroupId
LEFT JOIN ImageAnalysisDecisions d ON d.GroupIdentifier = g.GroupIdentifier 
    AND d.ContainerNumber = r.ContainerNumber
    AND d.ScannerType = COALESCE(r.ScannerType, g.ScannerType)
WHERE g.Status = 'AnalystAssigned'
GROUP BY g.Id, g.GroupIdentifier, g.Status, g.CreatedAtUtc, g.UpdatedAtUtc
HAVING COUNT(DISTINCT r.ContainerNumber) > 0
ORDER BY g.UpdatedAtUtc DESC;

-- Step 2: Find groups where all containers have decisions but status is still AnalystAssigned
SELECT 
    g.Id AS GroupId,
    g.GroupIdentifier,
    g.Status AS GroupStatus,
    COUNT(DISTINCT r.ContainerNumber) AS TotalContainers,
    COUNT(DISTINCT d.ContainerNumber) AS DecidedContainers,
    CASE 
        WHEN COUNT(DISTINCT r.ContainerNumber) = COUNT(DISTINCT CASE WHEN d.Decision IN ('Normal', 'Abnormal') THEN d.ContainerNumber END)
        THEN 'ALL_DECIDED'
        ELSE 'MISSING_DECISIONS'
    END AS CompletionStatus,
    -- Check for missing GroupIdentifier in decisions
    COUNT(DISTINCT CASE WHEN d.GroupIdentifier IS NULL OR d.GroupIdentifier = '' THEN d.ContainerNumber END) AS DecisionsWithoutGroupId,
    -- Check for GroupIdentifier mismatch
    COUNT(DISTINCT CASE WHEN d.GroupIdentifier IS NOT NULL AND d.GroupIdentifier <> g.GroupIdentifier THEN d.ContainerNumber END) AS DecisionsWithMismatchedGroupId
FROM AnalysisGroups g
INNER JOIN AnalysisRecords r ON g.Id = r.GroupId
LEFT JOIN ImageAnalysisDecisions d ON d.ContainerNumber = r.ContainerNumber
    AND d.ScannerType = COALESCE(r.ScannerType, g.ScannerType)
WHERE g.Status = 'AnalystAssigned'
GROUP BY g.Id, g.GroupIdentifier, g.Status
HAVING COUNT(DISTINCT r.ContainerNumber) = COUNT(DISTINCT CASE WHEN d.Decision IN ('Normal', 'Abnormal') THEN d.ContainerNumber END)
ORDER BY g.UpdatedAtUtc DESC;

-- Step 3: Check for decisions with missing or mismatched GroupIdentifier
SELECT 
    d.Id AS DecisionId,
    d.ContainerNumber,
    d.ScannerType,
    d.Decision,
    d.GroupIdentifier AS DecisionGroupId,
    g.GroupIdentifier AS ActualGroupId,
    g.Status AS GroupStatus,
    CASE 
        WHEN d.GroupIdentifier IS NULL OR d.GroupIdentifier = '' THEN 'MISSING_GROUP_ID'
        WHEN d.GroupIdentifier <> g.GroupIdentifier THEN 'MISMATCHED_GROUP_ID'
        ELSE 'OK'
    END AS Issue
FROM ImageAnalysisDecisions d
INNER JOIN AnalysisRecords r ON d.ContainerNumber = r.ContainerNumber 
    AND d.ScannerType = COALESCE(r.ScannerType, '')
INNER JOIN AnalysisGroups g ON r.GroupId = g.Id
WHERE g.Status = 'AnalystAssigned'
    AND d.Decision IN ('Normal', 'Abnormal')
    AND (d.GroupIdentifier IS NULL OR d.GroupIdentifier = '' OR d.GroupIdentifier <> g.GroupIdentifier);

-- Step 4: Check AnalysisAssignments for stuck groups
SELECT 
    a.Id AS AssignmentId,
    a.GroupId,
    g.GroupIdentifier,
    a.AssignedTo,
    a.Role,
    a.State,
    a.LeaseUntilUtc,
    g.Status AS GroupStatus
FROM AnalysisAssignments a
INNER JOIN AnalysisGroups g ON a.GroupId = g.Id
WHERE g.Status = 'AnalystAssigned'
    AND a.Role = 'Analyst'
    AND a.State = 'Active'
ORDER BY a.LeaseUntilUtc DESC;


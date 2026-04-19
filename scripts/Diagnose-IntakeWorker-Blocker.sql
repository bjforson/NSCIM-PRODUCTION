-- IntakeWorker Blocker Diagnostic
-- Orchestrator only processes: Status=Complete, WorkflowStage IN (Pending, null), GroupIdentifier NOT in existing AnalysisGroups

-- 1. AnalysisSettings.Enabled
SELECT Id, [Enabled], AssignmentMode, MaxConcurrentPerUser
FROM AnalysisSettings;

-- 2. Count completeness rows the Orchestrator actually processes (Pending/null only, excluding ImageAnalysis)
SELECT 
    'PendingOrNull' AS WorkflowStage,
    COUNT(*) AS ContainerCount
FROM ContainerCompletenessStatuses
WHERE Status LIKE 'Complete%'
    AND (WorkflowStage = 'Pending' OR WorkflowStage IS NULL OR WorkflowStage = '');

-- 3. Count Pending/null completeness rows whose GroupIdentifier does NOT yet have an AnalysisGroup
SELECT COUNT(*) AS NewGroupsAvailable
FROM ContainerCompletenessStatuses c
WHERE c.Status LIKE 'Complete%'
    AND (c.WorkflowStage = 'Pending' OR c.WorkflowStage IS NULL OR c.WorkflowStage = '')
    AND c.GroupIdentifier IS NOT NULL
    AND c.GroupIdentifier <> ''
    AND NOT EXISTS (
        SELECT 1 FROM AnalysisGroups g 
        WHERE g.GroupIdentifier = c.GroupIdentifier
    );

-- 4. Sample of GroupIdentifiers that are Pending but already have AnalysisGroups (would be excluded)
SELECT TOP 5 c.GroupIdentifier, c.WorkflowStage, c.Status
FROM ContainerCompletenessStatuses c
WHERE c.Status LIKE 'Complete%'
    AND (c.WorkflowStage = 'Pending' OR c.WorkflowStage IS NULL OR c.WorkflowStage = '')
    AND EXISTS (SELECT 1 FROM AnalysisGroups g WHERE g.GroupIdentifier = c.GroupIdentifier);

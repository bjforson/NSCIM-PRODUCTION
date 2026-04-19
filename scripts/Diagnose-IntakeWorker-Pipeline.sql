-- IntakeWorker Pipeline Diagnostic
-- Confirms whether IntakeWorker is picking up ContainerCompletenessStatus and creating AnalysisGroups

PRINT '=== 1. ContainerCompletenessStatus: Eligible for IntakeWorker ===';
PRINT '   (Status LIKE ''Complete%'' AND WorkflowStage IN (''Pending'',''ImageAnalysis'',''''))';
SELECT 
    WorkflowStage,
    COUNT(*) AS ContainerCount,
    COUNT(DISTINCT GroupIdentifier) AS UniqueGroupIdentifiers
FROM ContainerCompletenessStatuses
WHERE Status LIKE 'Complete%'
    AND (WorkflowStage = 'Pending' OR WorkflowStage = 'ImageAnalysis' OR WorkflowStage IS NULL OR WorkflowStage = '')
GROUP BY WorkflowStage
ORDER BY ContainerCount DESC;

PRINT '';
PRINT '=== 2. Summary: Input for IntakeWorker ===';
SELECT 
    COUNT(*) AS EligibleContainers,
    COUNT(DISTINCT GroupIdentifier) AS EligibleGroups
FROM ContainerCompletenessStatuses
WHERE Status LIKE 'Complete%'
    AND (WorkflowStage = 'Pending' OR WorkflowStage = 'ImageAnalysis' OR WorkflowStage IS NULL OR WorkflowStage = '');

PRINT '';
PRINT '=== 3. AnalysisGroups: Recent creation (last 24h) ===';
SELECT COUNT(*) AS GroupsCreatedLast24h
FROM AnalysisGroups
WHERE CreatedAtUtc >= DATEADD(HOUR, -24, GETUTCDATE());

PRINT '';
PRINT '=== 4. AnalysisGroups: Recent creation (last 7 days) ===';
SELECT COUNT(*) AS GroupsCreatedLast7d
FROM AnalysisGroups
WHERE CreatedAtUtc >= DATEADD(DAY, -7, GETUTCDATE());

PRINT '';
PRINT '=== 5. AnalysisGroups: By Status (ready for AssignmentWorker) ===';
SELECT Status, COUNT(*) AS GroupCount
FROM AnalysisGroups
GROUP BY Status
ORDER BY GroupCount DESC;

PRINT '';
PRINT '=== 6. AnalysisGroups: Ready groups (assignment pool) ===';
SELECT COUNT(*) AS ReadyForAssignment
FROM AnalysisGroups
WHERE Status = 'Ready';

PRINT '';
PRINT '=== 7. Recent IntakeWorker activity (groups created/updated in last 2h) ===';
SELECT TOP 10
    GroupIdentifier,
    Status,
    CreatedAtUtc,
    UpdatedAtUtc,
    DATEDIFF(MINUTE, CreatedAtUtc, GETUTCDATE()) AS MinutesSinceCreated
FROM AnalysisGroups
ORDER BY CreatedAtUtc DESC;

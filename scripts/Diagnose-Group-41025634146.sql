-- Diagnose group 41025634146 - why still assigned to Janalyst with 1 image + 1 decision?
-- Run in SSMS against NS_CIS database

DECLARE @GroupId NVARCHAR(50) = '41025634146';

-- 1. AnalysisGroup(s) - may have date suffix
SELECT Id, GroupIdentifier, ScannerType, Status, 
    PartiallyCompletedDate, TotalContainerCount, SubmittedContainerCount, PendingContainerCount
FROM AnalysisGroups
WHERE GroupIdentifier = @GroupId OR GroupIdentifier LIKE @GroupId + '%';

-- 2. Assignments for this group
SELECT a.Id, a.AssignedTo, a.Role, a.State, a.LeaseUntilUtc, g.GroupIdentifier, g.Status AS GroupStatus
FROM AnalysisAssignments a
JOIN AnalysisGroups g ON a.GroupId = g.Id
WHERE g.GroupIdentifier = @GroupId OR g.GroupIdentifier LIKE @GroupId + '%'
ORDER BY a.State, a.CreatedAtUtc DESC;

-- 3. AnalysisRecords (containers in group)
SELECT ar.Id, ar.GroupId, ar.ContainerNumber, g.GroupIdentifier, g.ScannerType
FROM AnalysisRecords ar
JOIN AnalysisGroups g ON ar.GroupId = g.Id
WHERE g.GroupIdentifier = @GroupId OR g.GroupIdentifier LIKE @GroupId + '%';

-- 4. ContainerCompletenessStatus - containers with GroupIdentifier, HasImageData
SELECT Id, ContainerNumber, GroupIdentifier, ScannerType, HasImageData, Status, WorkflowStage
FROM ContainerCompletenessStatuses
WHERE GroupIdentifier = @GroupId OR GroupIdentifier LIKE @GroupId + '%'
ORDER BY ContainerNumber;

-- 5. ImageAnalysisDecisions - what decisions exist?
SELECT iad.Id, iad.ContainerNumber, iad.GroupIdentifier, iad.ScannerType, iad.Decision, iad.CreatedAt
FROM ImageAnalysisDecisions iad
WHERE iad.GroupIdentifier = @GroupId 
   OR iad.GroupIdentifier LIKE @GroupId + '%'
   OR iad.ContainerNumber IN (SELECT ContainerNumber FROM ContainerCompletenessStatuses WHERE GroupIdentifier = @GroupId OR GroupIdentifier LIKE @GroupId + '%')
ORDER BY iad.ContainerNumber;

-- 6. Summary: Containers with images vs decided (for this group)
-- Uses same logic as Move-RecordToAuditWorkflow.ps1
SELECT 
    COUNT(DISTINCT CASE WHEN ccs.HasImageData = 1 THEN ccs.ContainerNumber END) AS ContainersWithImages,
    COUNT(DISTINCT CASE WHEN ccs.HasImageData = 1 AND iad.Decision IN ('Normal','Abnormal') THEN ccs.ContainerNumber END) AS DecidedWithImages
FROM ContainerCompletenessStatuses ccs
LEFT JOIN ImageAnalysisDecisions iad ON iad.ContainerNumber = ccs.ContainerNumber 
    AND (iad.GroupIdentifier = ccs.GroupIdentifier OR iad.GroupIdentifier = @GroupId OR ccs.GroupIdentifier = @GroupId)
    AND iad.Decision IN ('Normal','Abnormal')
WHERE ccs.GroupIdentifier = @GroupId OR ccs.GroupIdentifier LIKE @GroupId + '_%';

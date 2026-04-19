-- Check if audit decision was saved for a specific container
-- Usage: Replace @ContainerNumber with the container number to check

USE [NS_CIS];
GO

DECLARE @ContainerNumber NVARCHAR(50) = 'MSNU8543004';

-- Check AuditDecisions table
SELECT 
    'AuditDecision Found' AS Status,
    a.Id,
    a.ContainerNumber,
    a.GroupIdentifier,
    a.ScannerType,
    a.Decision,
    a.AuditNotes,
    a.AuditedBy,
    a.AuditedAt,
    a.IsCompleted,
    a.CompletedAt,
    a.OverallGroupDecision,
    a.CreatedAt,
    a.UpdatedAt
FROM AuditDecisions a
WHERE a.ContainerNumber = @ContainerNumber
ORDER BY a.AuditedAt DESC;

-- Check related ContainerCompletenessStatus
SELECT 
    'ContainerCompletenessStatus' AS Status,
    c.Id,
    c.ContainerNumber,
    c.ScannerType,
    c.GroupIdentifier,
    c.WorkflowStage,
    c.Status,
    c.IsConsolidated,
    c.CreatedAt,
    c.UpdatedAt
FROM ContainerCompletenessStatuses c
WHERE c.ContainerNumber = @ContainerNumber
ORDER BY c.UpdatedAt DESC;

-- Check ImageAnalysisDecision (original decision)
SELECT 
    'ImageAnalysisDecision' AS Status,
    i.Id,
    i.ContainerNumber,
    i.ScannerType,
    i.GroupIdentifier,
    i.Decision,
    i.ReviewedBy,
    i.ReviewedAt,
    i.Comments,
    i.CreatedAt,
    i.UpdatedAt
FROM ImageAnalysisDecisions i
WHERE i.ContainerNumber = @ContainerNumber
ORDER BY i.ReviewedAt DESC;

-- Check AnalysisGroup status
SELECT 
    'AnalysisGroup' AS Status,
    ag.Id,
    ag.GroupIdentifier,
    ag.ScannerType,
    ag.Status,
    ag.CreatedAtUtc,
    ag.UpdatedAtUtc
FROM AnalysisGroups ag
WHERE ag.GroupIdentifier IN (
    SELECT DISTINCT GroupIdentifier 
    FROM ContainerCompletenessStatuses 
    WHERE ContainerNumber = @ContainerNumber
)
ORDER BY ag.UpdatedAtUtc DESC;

-- Summary: Check if audit decision exists and workflow stage
SELECT 
    @ContainerNumber AS ContainerNumber,
    CASE 
        WHEN EXISTS (SELECT 1 FROM AuditDecisions WHERE ContainerNumber = @ContainerNumber) 
        THEN 'YES' 
        ELSE 'NO' 
    END AS HasAuditDecision,
    CASE 
        WHEN EXISTS (
            SELECT 1 FROM ContainerCompletenessStatuses 
            WHERE ContainerNumber = @ContainerNumber AND WorkflowStage = 'Completed'
        ) 
        THEN 'Completed' 
        WHEN EXISTS (
            SELECT 1 FROM ContainerCompletenessStatuses 
            WHERE ContainerNumber = @ContainerNumber AND WorkflowStage = 'Audit'
        ) 
        THEN 'Audit' 
        WHEN EXISTS (
            SELECT 1 FROM ContainerCompletenessStatuses 
            WHERE ContainerNumber = @ContainerNumber
        ) 
        THEN (SELECT TOP 1 WorkflowStage FROM ContainerCompletenessStatuses WHERE ContainerNumber = @ContainerNumber)
        ELSE 'Not Found' 
    END AS CurrentWorkflowStage,
    (SELECT TOP 1 Decision FROM AuditDecisions WHERE ContainerNumber = @ContainerNumber ORDER BY AuditedAt DESC) AS LatestAuditDecision,
    (SELECT TOP 1 AuditedBy FROM AuditDecisions WHERE ContainerNumber = @ContainerNumber ORDER BY AuditedAt DESC) AS AuditedBy,
    (SELECT TOP 1 AuditedAt FROM AuditDecisions WHERE ContainerNumber = @ContainerNumber ORDER BY AuditedAt DESC) AS AuditedAt;

GO


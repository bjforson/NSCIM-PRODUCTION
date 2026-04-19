-- Diagnose why Janalyst sees 4 assignments on front end but DB has 20+
-- GetMyAssignments filters OUT groups where Status is AnalystCompleted, AuditCompleted, or Completed
-- So only Ready/AnalystAssigned groups show. Run this to see the breakdown.

-- 1. Count by group status (Analyst role filters out AnalystCompleted and beyond)
SELECT 
    g.Status,
    COUNT(*) AS AssignmentCount,
    CASE 
        WHEN g.Status IN ('AnalystCompleted', 'AuditCompleted', 'Completed') THEN 'FILTERED OUT (hidden from UI)'
        ELSE 'SHOWN (visible in My Assignments)'
    END AS Visibility
FROM AnalysisAssignments a
JOIN AnalysisGroups g ON a.GroupId = g.Id
WHERE a.AssignedTo = 'Janalyst'
    AND a.State = 'Active'
    AND (a.LeaseUntilUtc IS NULL OR a.LeaseUntilUtc > GETUTCDATE())
GROUP BY g.Status
ORDER BY 
    CASE 
        WHEN g.Status IN ('AnalystCompleted', 'AuditCompleted', 'Completed') THEN 2 
        ELSE 1 
    END,
    g.Status;

-- 2. Summary
SELECT 
    SUM(CASE WHEN g.Status NOT IN ('AnalystCompleted', 'AuditCompleted', 'Completed') THEN 1 ELSE 0 END) AS ShownInUI,
    SUM(CASE WHEN g.Status IN ('AnalystCompleted', 'AuditCompleted', 'Completed') THEN 1 ELSE 0 END) AS FilteredOut,
    COUNT(*) AS TotalActive
FROM AnalysisAssignments a
JOIN AnalysisGroups g ON a.GroupId = g.Id
WHERE a.AssignedTo = 'Janalyst'
    AND a.State = 'Active'
    AND (a.LeaseUntilUtc IS NULL OR a.LeaseUntilUtc > GETUTCDATE());

-- 3. Expired lease count (also filtered by API)
SELECT COUNT(*) AS ExpiredLeaseCount
FROM AnalysisAssignments
WHERE AssignedTo = 'Janalyst'
    AND State = 'Active'
    AND LeaseUntilUtc IS NOT NULL
    AND LeaseUntilUtc <= GETUTCDATE();

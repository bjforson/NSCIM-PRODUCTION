-- Check current active assignments
-- Run this to see what assignments exist right now

SELECT 
    a.Id AS AssignmentId,
    a.AssignedTo,
    a.Role,
    a.State,
    a.LeaseUntilUtc,
    DATEDIFF(MINUTE, GETUTCDATE(), a.LeaseUntilUtc) AS MinutesUntilExpiry,
    g.GroupIdentifier,
    g.Status AS GroupStatus,
    a.CreatedAtUtc,
    a.LastAccessedAtUtc
FROM AnalysisAssignments a
LEFT JOIN AnalysisGroups g ON a.GroupId = g.Id
WHERE a.State = 'Active'
    AND a.LeaseUntilUtc > GETUTCDATE()
ORDER BY a.AssignedTo, a.CreatedAtUtc DESC;

-- Summary by user
SELECT 
    AssignedTo,
    Role,
    COUNT(*) AS ActiveAssignments,
    MIN(LeaseUntilUtc) AS EarliestExpiry,
    MAX(LeaseUntilUtc) AS LatestExpiry
FROM AnalysisAssignments
WHERE State = 'Active'
    AND LeaseUntilUtc > GETUTCDATE()
GROUP BY AssignedTo, Role
ORDER BY AssignedTo, Role;


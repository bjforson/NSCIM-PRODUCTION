-- Check current assignment status and user readiness
-- Run this to see why assignments aren't being created

-- 1. Check user readiness (should have recent heartbeats)
SELECT 
    Username,
    Role,
    IsReady,
    LastHeartbeat,
    DATEDIFF(MINUTE, LastHeartbeat, GETUTCDATE()) AS MinutesSinceHeartbeat,
    CASE 
        WHEN IsReady = 0 THEN 'NOT READY'
        WHEN DATEDIFF(MINUTE, LastHeartbeat, GETUTCDATE()) > 60 THEN 'HEARTBEAT EXPIRED (>60 min)'
        WHEN DATEDIFF(MINUTE, LastHeartbeat, GETUTCDATE()) <= 60 THEN 'READY'
        ELSE 'UNKNOWN'
    END AS Status
FROM UserReadiness
WHERE Role IN ('Analyst', 'Audit')
ORDER BY Role, LastHeartbeat DESC;

-- 2. Check ready groups (should have groups with Status='Ready' for Analyst, 'AnalystCompleted' for Audit)
SELECT 
    'Analyst' AS Role,
    COUNT(*) AS ReadyGroups
FROM AnalysisGroups
WHERE Status = 'Ready'
    AND WorkflowStage = 'Analysis'

UNION ALL

SELECT 
    'Audit' AS Role,
    COUNT(*) AS ReadyGroups
FROM AnalysisGroups
WHERE Status = 'AnalystCompleted'
    AND WorkflowStage = 'Analysis';

-- 3. Check current active assignments
SELECT 
    Role,
    AssignedTo,
    COUNT(*) AS ActiveAssignments,
    MIN(LeaseUntilUtc) AS EarliestLeaseExpiry,
    MAX(LeaseUntilUtc) AS LatestLeaseExpiry
FROM AnalysisAssignments
WHERE State = 'Active'
    AND LeaseUntilUtc > GETUTCDATE()
GROUP BY Role, AssignedTo
ORDER BY Role, AssignedTo;

-- 4. Check AnalysisSettings
SELECT 
    Enabled,
    AssignmentMode,
    MaxConcurrentPerUser,
    LeaseMinutes,
    AutoAssignStrategy
FROM AnalysisSettings;


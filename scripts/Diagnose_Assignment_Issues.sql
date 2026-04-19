-- =====================================================
-- Diagnostic Queries for Assignment Issues
-- =====================================================

-- =====================================================
-- 1. CHECK ASSIGNMENT SETTINGS
-- =====================================================
SELECT 
    'Assignment Settings' AS CheckType,
    Enabled,
    AssignmentMode,
    MaxConcurrentPerUser,
    LeaseMinutes,
    CASE 
        WHEN Enabled = 0 THEN '❌ SERVICE DISABLED'
        WHEN AssignmentMode != 'Auto' THEN '❌ NOT IN AUTO MODE'
        ELSE '✅ OK'
    END AS Status
FROM AnalysisSettings;

-- =====================================================
-- 2. CHECK USER READINESS STATUS
-- =====================================================
SELECT 
    'User Readiness - Analyst' AS CheckType,
    Username,
    Role,
    IsReady,
    LastHeartbeat,
    DATEDIFF(SECOND, LastHeartbeat, GETUTCDATE()) AS SecondsSinceHeartbeat,
    CASE 
        WHEN IsReady = 0 THEN '❌ NOT READY'
        WHEN LastHeartbeat < DATEADD(MINUTE, -5, GETUTCDATE()) THEN '❌ HEARTBEAT EXPIRED (>5min)'
        ELSE '✅ READY'
    END AS Status
FROM UserReadiness
WHERE Role = 'Analyst'
ORDER BY LastHeartbeat DESC;

-- =====================================================
-- 3. CHECK READY GROUPS AVAILABLE FOR ASSIGNMENT
-- =====================================================
SELECT 
    'Ready Groups' AS CheckType,
    COUNT(*) AS TotalReadyGroups,
    SUM(CASE WHEN EXISTS (
        SELECT 1 
        FROM ContainerCompletenessStatuses ccs
        WHERE ccs.GroupIdentifier = ag.GroupIdentifier
        AND ccs.WorkflowStage = 'ImageAnalysis'
    ) THEN 1 ELSE 0 END) AS GroupsWithImageAnalysisContainers,
    SUM(CASE WHEN NOT EXISTS (
        SELECT 1 
        FROM AnalysisAssignments aa
        WHERE aa.GroupId = ag.Id
        AND aa.State = 'Active'
        AND (aa.LeaseUntilUtc IS NULL OR aa.LeaseUntilUtc > GETUTCDATE())
    ) THEN 1 ELSE 0 END) AS GroupsWithoutActiveAssignments
FROM AnalysisGroups ag
WHERE ag.Status = 'Ready'
AND ag.GroupIdentifier IS NOT NULL
AND ag.GroupIdentifier != '';

-- =====================================================
-- 4. CHECK ACTIVE ASSIGNMENTS BY USER
-- =====================================================
SELECT 
    'Active Assignments' AS CheckType,
    AssignedTo,
    Role,
    COUNT(*) AS ActiveAssignmentCount,
    MAX(LeaseUntilUtc) AS LatestLease,
    CASE 
        WHEN COUNT(*) >= (SELECT MaxConcurrentPerUser FROM AnalysisSettings) THEN '❌ AT CAPACITY'
        ELSE '✅ AVAILABLE'
    END AS Status
FROM AnalysisAssignments
WHERE State = 'Active'
AND (LeaseUntilUtc IS NULL OR LeaseUntilUtc > GETUTCDATE())
GROUP BY AssignedTo, Role
ORDER BY ActiveAssignmentCount DESC;

-- =====================================================
-- 5. CHECK USERS WITH CORRECT ROLE
-- =====================================================
SELECT 
    'Users with Analyst Role' AS CheckType,
    u.Username,
    u.IsActive,
    r.Name AS RoleName,
    r.IsActive AS RoleIsActive,
    CASE 
        WHEN u.IsActive = 0 THEN '❌ USER INACTIVE'
        WHEN r.IsActive = 0 THEN '❌ ROLE INACTIVE'
        WHEN r.Name != 'Analyst' THEN '❌ WRONG ROLE'
        ELSE '✅ OK'
    END AS Status
FROM Users u
LEFT JOIN Roles r ON u.RoleId = r.Id
WHERE r.Name = 'Analyst'
ORDER BY u.Username;

-- =====================================================
-- 6. CHECK GROUPS STATUS BREAKDOWN
-- =====================================================
SELECT 
    'Groups by Status' AS CheckType,
    Status,
    COUNT(*) AS Count,
    MIN(CreatedAtUtc) AS OldestGroup,
    MAX(UpdatedAtUtc) AS LastUpdated
FROM AnalysisGroups
GROUP BY Status
ORDER BY Status;

-- =====================================================
-- 7. COMPREHENSIVE ASSIGNMENT READINESS CHECK
-- =====================================================
SELECT 
    'Comprehensive Check' AS CheckType,
    -- Settings check
    CASE WHEN (SELECT COUNT(*) FROM AnalysisSettings WHERE Enabled = 1 AND AssignmentMode = 'Auto') > 0 
         THEN '✅ Auto mode enabled' 
         ELSE '❌ Auto mode NOT enabled' 
    END AS SettingsStatus,
    -- Ready users check
    (SELECT COUNT(*) 
     FROM UserReadiness 
     WHERE Role = 'Analyst' 
     AND IsReady = 1 
     AND LastHeartbeat >= DATEADD(MINUTE, -5, GETUTCDATE())
    ) AS ReadyAnalystCount,
    -- Available groups check
    (SELECT COUNT(*) 
     FROM AnalysisGroups 
     WHERE Status = 'Ready'
     AND GroupIdentifier IS NOT NULL
     AND GroupIdentifier != ''
    ) AS ReadyGroupsCount,
    -- Active assignments check
    (SELECT COUNT(*) 
     FROM AnalysisAssignments 
     WHERE State = 'Active'
     AND Role = 'Analyst'
     AND (LeaseUntilUtc IS NULL OR LeaseUntilUtc > GETUTCDATE())
    ) AS ActiveAssignmentCount,
    -- Users at capacity
    (SELECT COUNT(DISTINCT AssignedTo)
     FROM AnalysisAssignments aa
     WHERE aa.State = 'Active'
     AND aa.Role = 'Analyst'
     AND (aa.LeaseUntilUtc IS NULL OR aa.LeaseUntilUtc > GETUTCDATE())
     GROUP BY aa.AssignedTo
     HAVING COUNT(*) >= (SELECT MaxConcurrentPerUser FROM AnalysisSettings)
    ) AS UsersAtCapacity;


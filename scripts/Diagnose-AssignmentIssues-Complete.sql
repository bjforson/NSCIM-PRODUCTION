-- =====================================================
-- COMPLETE ASSIGNMENT DIAGNOSTIC SCRIPT
-- Run this to diagnose why assignments are not being made
-- =====================================================

DECLARE @Now DATETIME2 = GETUTCDATE();
DECLARE @MaxIdle DATETIME2 = DATEADD(MINUTE, -2, @Now);
DECLARE @MaxConcurrent INT = (SELECT MaxConcurrentPerUser FROM AnalysisSettings);

PRINT '========================================';
PRINT 'ASSIGNMENT DIAGNOSTIC REPORT';
PRINT 'Generated: ' + CONVERT(VARCHAR(30), @Now, 120);
PRINT '========================================';
PRINT '';

-- =====================================================
-- 1. CHECK ASSIGNMENT SETTINGS
-- =====================================================
PRINT '1. ASSIGNMENT SETTINGS';
PRINT '------------------------';
SELECT 
    Enabled,
    AssignmentMode,
    AutoAssignStrategy,
    MaxConcurrentPerUser,
    LeaseMinutes,
    CASE 
        WHEN Enabled = 0 THEN '❌ SERVICE DISABLED'
        WHEN AssignmentMode != 'Auto' THEN '❌ NOT IN AUTO MODE (MOST COMMON ISSUE)'
        WHEN AssignmentMode = 'Auto' AND Enabled = 1 THEN '✅ OK'
        ELSE '⚠️ CHECK CONFIGURATION'
    END AS Status,
    CreatedAtUtc,
    UpdatedAtUtc
FROM AnalysisSettings;
PRINT '';

-- =====================================================
-- 2. CHECK READY GROUPS (ANALYST)
-- =====================================================
PRINT '2. READY GROUPS (ANALYST)';
PRINT '------------------------';
SELECT 
    COUNT(*) AS ReadyGroupsCount,
    MIN(CreatedAtUtc) AS OldestReady,
    MAX(CreatedAtUtc) AS NewestReady,
    CASE 
        WHEN COUNT(*) = 0 THEN '❌ NO GROUPS - Check IntakeWorkflow'
        ELSE '✅ ' + CAST(COUNT(*) AS VARCHAR) + ' GROUPS'
    END AS Status
FROM AnalysisGroups
WHERE Status = 'Ready';
PRINT '';

-- =====================================================
-- 3. CHECK ANALYST COMPLETED GROUPS (AUDIT)
-- =====================================================
PRINT '3. ANALYST COMPLETED GROUPS (AUDIT)';
PRINT '------------------------';
SELECT 
    COUNT(*) AS AnalystCompletedGroupsCount,
    MIN(CreatedAtUtc) AS OldestCompleted,
    MAX(CreatedAtUtc) AS NewestCompleted,
    CASE 
        WHEN COUNT(*) = 0 THEN '❌ NO GROUPS - Analyst needs to complete work first'
        ELSE '✅ ' + CAST(COUNT(*) AS VARCHAR) + ' GROUPS'
    END AS Status
FROM AnalysisGroups
WHERE Status = 'AnalystCompleted';
PRINT '';

-- =====================================================
-- 4. CHECK WORKFLOWSTAGE DISTRIBUTION FOR READY GROUPS
-- =====================================================
PRINT '4. WORKFLOWSTAGE DISTRIBUTION (READY GROUPS)';
PRINT '------------------------';
SELECT 
    ag.Status AS GroupStatus,
    COUNT(DISTINCT ag.GroupIdentifier) AS GroupCount,
    SUM(CASE WHEN ccs.WorkflowStage = 'Pending' OR ccs.WorkflowStage IS NULL THEN 1 ELSE 0 END) AS PendingCount,
    SUM(CASE WHEN ccs.WorkflowStage = 'ImageAnalysis' THEN 1 ELSE 0 END) AS ImageAnalysisCount,
    SUM(CASE WHEN ccs.WorkflowStage = 'Audit' THEN 1 ELSE 0 END) AS AuditCount,
    SUM(CASE WHEN ccs.WorkflowStage = 'Completed' THEN 1 ELSE 0 END) AS CompletedCount,
    COUNT(DISTINCT ccs.ContainerNumber) AS TotalContainers
FROM AnalysisGroups ag
LEFT JOIN ContainerCompletenessStatuses ccs ON ccs.GroupIdentifier = ag.GroupIdentifier
WHERE ag.Status = 'Ready'
GROUP BY ag.Status;
PRINT '';

-- =====================================================
-- 5. CHECK USER READINESS (DATABASE)
-- =====================================================
PRINT '5. USER READINESS (DATABASE) - ANALYST';
PRINT '------------------------';
SELECT 
    ur.Username,
    ur.Role,
    ur.IsReady,
    ur.LastHeartbeat,
    DATEDIFF(SECOND, ur.LastHeartbeat, @Now) AS SecondsSinceHeartbeat,
    CASE 
        WHEN ur.IsReady = 0 THEN '❌ NOT READY'
        WHEN ur.LastHeartbeat < @MaxIdle THEN '❌ HEARTBEAT EXPIRED (>2min)'
        ELSE '✅ READY (database)'
    END AS ReadinessStatus
FROM UserReadiness ur
WHERE ur.Role = 'Analyst'
ORDER BY ur.LastHeartbeat DESC;
PRINT '';

PRINT '5B. USER READINESS (DATABASE) - AUDIT';
PRINT '------------------------';
SELECT 
    ur.Username,
    ur.Role,
    ur.IsReady,
    ur.LastHeartbeat,
    DATEDIFF(SECOND, ur.LastHeartbeat, @Now) AS SecondsSinceHeartbeat,
    CASE 
        WHEN ur.IsReady = 0 THEN '❌ NOT READY'
        WHEN ur.LastHeartbeat < @MaxIdle THEN '❌ HEARTBEAT EXPIRED (>2min)'
        ELSE '✅ READY (database)'
    END AS ReadinessStatus
FROM UserReadiness ur
WHERE ur.Role = 'Audit'
ORDER BY ur.LastHeartbeat DESC;
PRINT '';

-- =====================================================
-- 6. CHECK USERS WITH ROLE ASSIGNMENT
-- =====================================================
PRINT '6. USERS WITH ROLE ASSIGNMENT';
PRINT '------------------------';
SELECT 
    r.Name AS RoleName,
    COUNT(DISTINCT u.Id) AS UserCount,
    STRING_AGG(u.UserName, ', ') AS Users
FROM AspNetUsers u
INNER JOIN AspNetUserRoles ur ON ur.UserId = u.Id
INNER JOIN AspNetRoles r ON r.Id = ur.RoleId
WHERE u.IsActive = 1
    AND r.IsActive = 1
    AND r.Name IN ('Analyst', 'Audit')
GROUP BY r.Name;
PRINT '';

-- =====================================================
-- 7. CHECK ACTIVE ASSIGNMENTS
-- =====================================================
PRINT '7. ACTIVE ASSIGNMENTS';
PRINT '------------------------';
SELECT 
    aa.Role,
    aa.AssignedTo,
    COUNT(*) AS ActiveAssignmentCount,
    MAX(aa.LeaseUntilUtc) AS LatestLease,
    CASE 
        WHEN COUNT(*) >= @MaxConcurrent THEN '❌ AT CAPACITY'
        ELSE '✅ HAS CAPACITY (' + CAST(COUNT(*) AS VARCHAR) + '/' + CAST(@MaxConcurrent AS VARCHAR) + ')'
    END AS Status
FROM AnalysisAssignments aa
WHERE aa.State = 'Active'
    AND (aa.LeaseUntilUtc IS NULL OR aa.LeaseUntilUtc > @Now)
GROUP BY aa.Role, aa.AssignedTo
ORDER BY aa.Role, ActiveAssignmentCount DESC;
PRINT '';

-- =====================================================
-- 8. CHECK READY GROUPS WITH ACTIVE ASSIGNMENTS (SHOULD NOT HAPPEN)
-- =====================================================
PRINT '8. READY GROUPS WITH ACTIVE ASSIGNMENTS (INCONSISTENCY CHECK)';
PRINT '------------------------';
SELECT 
    ag.GroupIdentifier,
    ag.Status AS GroupStatus,
    aa.AssignedTo,
    aa.State AS AssignmentState,
    aa.LeaseUntilUtc,
    CASE 
        WHEN aa.LeaseUntilUtc < @Now THEN '❌ EXPIRED (should be reclaimed)'
        ELSE '❌ ACTIVE (group should not be Ready)'
    END AS Status
FROM AnalysisGroups ag
INNER JOIN AnalysisAssignments aa ON aa.GroupId = ag.Id
WHERE ag.Status = 'Ready'
    AND aa.State = 'Active'
ORDER BY ag.CreatedAtUtc DESC;
PRINT '';

-- =====================================================
-- 9. COMPREHENSIVE STATUS SUMMARY
-- =====================================================
PRINT '9. COMPREHENSIVE STATUS SUMMARY';
PRINT '------------------------';
SELECT 
    'Settings' AS CheckType,
    CASE 
        WHEN (SELECT COUNT(*) FROM AnalysisSettings WHERE Enabled = 1 AND AssignmentMode = 'Auto') > 0 
        THEN '✅ Auto mode enabled' 
        ELSE '❌ Auto mode NOT enabled' 
    END AS Status
UNION ALL
SELECT 
    'Ready Groups (Analyst)' AS CheckType,
    CASE 
        WHEN (SELECT COUNT(*) FROM AnalysisGroups WHERE Status = 'Ready') > 0
        THEN '✅ ' + CAST((SELECT COUNT(*) FROM AnalysisGroups WHERE Status = 'Ready') AS VARCHAR) + ' groups'
        ELSE '❌ NO GROUPS'
    END AS Status
UNION ALL
SELECT 
    'Ready Groups (Audit)' AS CheckType,
    CASE 
        WHEN (SELECT COUNT(*) FROM AnalysisGroups WHERE Status = 'AnalystCompleted') > 0
        THEN '✅ ' + CAST((SELECT COUNT(*) FROM AnalysisGroups WHERE Status = 'AnalystCompleted') AS VARCHAR) + ' groups'
        ELSE '❌ NO GROUPS'
    END AS Status
UNION ALL
SELECT 
    'Ready Users (Analyst - Database)' AS CheckType,
    CASE 
        WHEN (SELECT COUNT(*) FROM UserReadiness WHERE Role = 'Analyst' AND IsReady = 1 AND LastHeartbeat >= @MaxIdle) > 0
        THEN '✅ ' + CAST((SELECT COUNT(*) FROM UserReadiness WHERE Role = 'Analyst' AND IsReady = 1 AND LastHeartbeat >= @MaxIdle) AS VARCHAR) + ' users'
        ELSE '❌ NO READY USERS (Note: SignalR is primary source, check logs)'
    END AS Status
UNION ALL
SELECT 
    'Ready Users (Audit - Database)' AS CheckType,
    CASE 
        WHEN (SELECT COUNT(*) FROM UserReadiness WHERE Role = 'Audit' AND IsReady = 1 AND LastHeartbeat >= @MaxIdle) > 0
        THEN '✅ ' + CAST((SELECT COUNT(*) FROM UserReadiness WHERE Role = 'Audit' AND IsReady = 1 AND LastHeartbeat >= @MaxIdle) AS VARCHAR) + ' users'
        ELSE '❌ NO READY USERS (Note: SignalR is primary source, check logs)'
    END AS Status
UNION ALL
SELECT 
    'Active Assignments (Analyst)' AS CheckType,
    CAST((SELECT COUNT(*) FROM AnalysisAssignments WHERE State = 'Active' AND Role = 'Analyst' AND (LeaseUntilUtc IS NULL OR LeaseUntilUtc > @Now)) AS VARCHAR) + ' assignments' AS Status
UNION ALL
SELECT 
    'Active Assignments (Audit)' AS CheckType,
    CAST((SELECT COUNT(*) FROM AnalysisAssignments WHERE State = 'Active' AND Role = 'Audit' AND (LeaseUntilUtc IS NULL OR LeaseUntilUtc > @Now)) AS VARCHAR) + ' assignments' AS Status;
PRINT '';

-- =====================================================
-- 10. MOST COMMON ISSUES CHECK
-- =====================================================
PRINT '10. MOST COMMON ISSUES CHECK';
PRINT '------------------------';
SELECT 
    'Issue #1: AssignmentMode' AS Issue,
    CASE 
        WHEN (SELECT AssignmentMode FROM AnalysisSettings) = 'Auto' THEN '✅ OK'
        ELSE '❌ SET TO: ' + (SELECT AssignmentMode FROM AnalysisSettings) + ' (should be Auto)'
    END AS Status
UNION ALL
SELECT 
    'Issue #2: Service Enabled' AS Issue,
    CASE 
        WHEN (SELECT Enabled FROM AnalysisSettings) = 1 THEN '✅ OK'
        ELSE '❌ SERVICE DISABLED'
    END AS Status
UNION ALL
SELECT 
    'Issue #3: Ready Groups Exist' AS Issue,
    CASE 
        WHEN (SELECT COUNT(*) FROM AnalysisGroups WHERE Status = 'Ready') > 0 THEN '✅ OK'
        ELSE '❌ NO READY GROUPS'
    END AS Status
UNION ALL
SELECT 
    'Issue #4: Ready Users Exist (Database)' AS Issue,
    CASE 
        WHEN (SELECT COUNT(*) FROM UserReadiness WHERE Role = 'Analyst' AND IsReady = 1 AND LastHeartbeat >= @MaxIdle) > 0 THEN '✅ OK (Database)'
        ELSE '⚠️ NO DATABASE USERS (Check SignalR in logs - SignalR is primary source)'
    END AS Status;
PRINT '';

PRINT '========================================';
PRINT 'END OF DIAGNOSTIC REPORT';
PRINT '========================================';
PRINT '';
PRINT 'NEXT STEPS:';
PRINT '1. Review Status column for ❌ (red X) indicators';
PRINT '2. Most common issue: AssignmentMode != ''Auto''';
PRINT '3. Fix: UPDATE AnalysisSettings SET AssignmentMode = ''Auto'', UpdatedAtUtc = GETUTCDATE();';
PRINT '4. Check service logs for [ASSIGNMENT] messages';
PRINT '5. Note: SignalR is PRIMARY source for ready users (database is fallback)';


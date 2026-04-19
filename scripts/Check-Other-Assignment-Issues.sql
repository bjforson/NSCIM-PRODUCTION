-- Check Other Assignment Issues (Since Settings Are OK)
-- Run this to find other potential issues

DECLARE @Now DATETIME2 = GETUTCDATE();
DECLARE @MaxIdle DATETIME2 = DATEADD(MINUTE, -2, @Now);

-- 1. Check Ready Groups Count
PRINT '1. READY GROUPS COUNT';
SELECT 
    COUNT(*) AS ReadyGroupsCount,
    CASE 
        WHEN COUNT(*) = 0 THEN 'NO GROUPS - Check IntakeWorkflow'
        ELSE 'OK - ' + CAST(COUNT(*) AS VARCHAR) + ' groups available'
    END AS Status
FROM AnalysisGroups
WHERE Status = 'Ready';
PRINT '';

-- 2. Check Ready Users (Database - Note: SignalR is primary source)
PRINT '2. READY USERS (DATABASE) - Note: SignalR is PRIMARY source';
SELECT 
    Username,
    Role,
    IsReady,
    LastHeartbeat,
    DATEDIFF(SECOND, LastHeartbeat, @Now) AS SecondsSinceHeartbeat,
    CASE 
        WHEN IsReady = 0 THEN 'NOT READY'
        WHEN LastHeartbeat < @MaxIdle THEN 'HEARTBEAT EXPIRED (>2min)'
        ELSE 'READY (database)'
    END AS Status
FROM UserReadiness
WHERE Role = 'Analyst'
ORDER BY LastHeartbeat DESC;
PRINT '';

-- 3. Check Users with Analyst Role
PRINT '3. USERS WITH ANALYST ROLE';
SELECT 
    u.Username,
    u.Email,
    r.Name AS RoleName,
    u.IsActive AS UserActive,
    r.IsActive AS RoleActive
FROM Users u
INNER JOIN Roles r ON r.Id = u.RoleId
WHERE r.Name = 'Analyst'
    AND u.IsActive = 1
    AND r.IsActive = 1
ORDER BY u.Username;
PRINT '';

-- 4. Check Active Assignments
PRINT '4. ACTIVE ASSIGNMENTS';
SELECT 
    AssignedTo,
    Role,
    COUNT(*) AS ActiveCount,
    CASE 
        WHEN COUNT(*) >= 5 THEN 'AT CAPACITY'
        ELSE 'HAS CAPACITY (' + CAST(COUNT(*) AS VARCHAR) + '/5)'
    END AS Status
FROM AnalysisAssignments
WHERE State = 'Active'
    AND (LeaseUntilUtc IS NULL OR LeaseUntilUtc > @Now)
GROUP BY AssignedTo, Role;
PRINT '';

-- 5. Check WorkflowStage Distribution for Ready Groups
PRINT '5. WORKFLOWSTAGE DISTRIBUTION (Ready Groups)';
SELECT 
    ccs.WorkflowStage,
    COUNT(DISTINCT ag.GroupIdentifier) AS GroupCount,
    COUNT(DISTINCT ccs.ContainerNumber) AS ContainerCount
FROM AnalysisGroups ag
INNER JOIN ContainerCompletenessStatuses ccs ON ccs.GroupIdentifier = ag.GroupIdentifier
WHERE ag.Status = 'Ready'
GROUP BY ccs.WorkflowStage
ORDER BY ccs.WorkflowStage;
PRINT '';

-- 6. Comprehensive Status Summary
PRINT '6. COMPREHENSIVE STATUS SUMMARY';
SELECT 
    'Settings' AS CheckType,
    'OK' AS Status,
    'AssignmentMode=Auto, Enabled=1' AS Details
UNION ALL
SELECT 
    'Ready Groups (Analyst)' AS CheckType,
    CASE 
        WHEN (SELECT COUNT(*) FROM AnalysisGroups WHERE Status = 'Ready') > 0
        THEN 'OK'
        ELSE 'NO GROUPS'
    END AS Status,
    CAST((SELECT COUNT(*) FROM AnalysisGroups WHERE Status = 'Ready') AS VARCHAR) + ' groups' AS Details
UNION ALL
SELECT 
    'Ready Users (Database)' AS CheckType,
    CASE 
        WHEN (SELECT COUNT(*) FROM UserReadiness WHERE Role = 'Analyst' AND IsReady = 1 AND LastHeartbeat >= @MaxIdle) > 0
        THEN 'OK (Note: SignalR is PRIMARY source)'
        ELSE 'NO DATABASE USERS (Check SignalR in logs)'
    END AS Status,
    CAST((SELECT COUNT(*) FROM UserReadiness WHERE Role = 'Analyst' AND IsReady = 1 AND LastHeartbeat >= @MaxIdle) AS VARCHAR) + ' users (database)' AS Details
UNION ALL
SELECT 
    'Users with Role' AS CheckType,
    CASE 
        WHEN (SELECT COUNT(*) FROM Users u INNER JOIN Roles r ON r.Id = u.RoleId WHERE r.Name = 'Analyst' AND u.IsActive = 1 AND r.IsActive = 1) > 0
        THEN 'OK'
        ELSE 'NO USERS'
    END AS Status,
    CAST((SELECT COUNT(*) FROM Users u INNER JOIN Roles r ON r.Id = u.RoleId WHERE r.Name = 'Analyst' AND u.IsActive = 1 AND r.IsActive = 1) AS VARCHAR) + ' users' AS Details
UNION ALL
SELECT 
    'Active Assignments' AS CheckType,
    'INFO' AS Status,
    CAST((SELECT COUNT(*) FROM AnalysisAssignments WHERE State = 'Active' AND Role = 'Analyst' AND (LeaseUntilUtc IS NULL OR LeaseUntilUtc > @Now)) AS VARCHAR) + ' assignments' AS Details;


-- Check Assignment Workflow Status
-- Verifies if assignments should be created and why they might not be

DECLARE @Now DATETIME2 = GETUTCDATE();

-- 1. Check Settings
PRINT '1. ANALYSIS SETTINGS';
SELECT 
    Enabled,
    AssignmentMode,
    MaxConcurrentPerUser,
    LeaseMinutes,
    AutoAssignStrategy,
    CASE 
        WHEN Enabled = 0 THEN 'SERVICE DISABLED'
        WHEN AssignmentMode != 'Auto' THEN 'NOT IN AUTO MODE'
        ELSE 'OK'
    END AS Status
FROM AnalysisSettings;
PRINT '';

-- 2. Check Ready Groups Count
PRINT '2. READY GROUPS COUNT';
SELECT 
    COUNT(*) AS ReadyGroupsCount,
    CASE 
        WHEN COUNT(*) = 0 THEN 'NO GROUPS - Cannot assign'
        ELSE 'OK - ' + CAST(COUNT(*) AS VARCHAR) + ' groups available'
    END AS Status
FROM AnalysisGroups
WHERE Status = 'Ready';
PRINT '';

-- 3. Check Ready Users (Database)
PRINT '3. READY USERS (DATABASE)';
DECLARE @MaxIdleMinutes INT = 60;
DECLARE @MaxIdle DATETIME2 = DATEADD(MINUTE, -@MaxIdleMinutes, @Now);

SELECT 
    COUNT(*) AS ReadyUsersCount,
    CASE 
        WHEN COUNT(*) = 0 THEN 'NO READY USERS - Cannot assign'
        ELSE 'OK - ' + CAST(COUNT(*) AS VARCHAR) + ' users ready'
    END AS Status
FROM UserReadiness
WHERE Role = 'Analyst'
    AND IsReady = 1
    AND LastHeartbeat >= @MaxIdle;
PRINT '';

-- 4. Show Ready Users Details
PRINT '4. READY USERS DETAILS';
SELECT 
    Username,
    Role,
    IsReady,
    LastHeartbeat,
    DATEDIFF(MINUTE, LastHeartbeat, @Now) AS MinutesSinceHeartbeat,
    CASE 
        WHEN IsReady = 1 AND LastHeartbeat >= @MaxIdle THEN 'READY'
        WHEN IsReady = 0 THEN 'NOT READY'
        ELSE 'HEARTBEAT EXPIRED'
    END AS Status
FROM UserReadiness
WHERE Role = 'Analyst'
ORDER BY LastHeartbeat DESC;
PRINT '';

-- 5. Check Active Assignments
PRINT '5. ACTIVE ASSIGNMENTS';
SELECT 
    COUNT(*) AS ActiveAssignmentsCount,
    COUNT(DISTINCT AssignedTo) AS UniqueUsersWithAssignments,
    CASE 
        WHEN COUNT(*) = 0 THEN 'NO ACTIVE ASSIGNMENTS'
        ELSE 'OK - ' + CAST(COUNT(*) AS VARCHAR) + ' active assignments'
    END AS Status
FROM AnalysisAssignments
WHERE Role = 'Analyst'
    AND State = 'Active'
    AND (LeaseUntilUtc IS NULL OR LeaseUntilUtc > @Now);
PRINT '';

-- 6. Check Users with Analyst Role
PRINT '6. USERS WITH ANALYST ROLE';
SELECT 
    COUNT(*) AS AnalystUsersCount,
    CASE 
        WHEN COUNT(*) = 0 THEN 'NO USERS WITH ROLE'
        ELSE 'OK - ' + CAST(COUNT(*) AS VARCHAR) + ' users with role'
    END AS Status
FROM Users u
INNER JOIN Roles r ON r.Id = u.RoleId
WHERE r.Name = 'Analyst'
    AND u.IsActive = 1
    AND r.IsActive = 1;
PRINT '';

-- 7. Summary - Why assignments might not be created
PRINT '7. DIAGNOSTIC SUMMARY';
SELECT 
    'Settings' AS CheckType,
    CASE 
        WHEN (SELECT Enabled FROM AnalysisSettings) = 0 THEN 'ISSUE: Service disabled'
        WHEN (SELECT AssignmentMode FROM AnalysisSettings) != 'Auto' THEN 'ISSUE: Not in Auto mode'
        ELSE 'OK'
    END AS Status
UNION ALL
SELECT 
    'Ready Groups' AS CheckType,
    CASE 
        WHEN (SELECT COUNT(*) FROM AnalysisGroups WHERE Status = 'Ready') = 0 THEN 'ISSUE: No ready groups'
        ELSE 'OK'
    END AS Status
UNION ALL
SELECT 
    'Ready Users' AS CheckType,
    CASE 
        WHEN (SELECT COUNT(*) FROM UserReadiness WHERE Role = 'Analyst' AND IsReady = 1 AND LastHeartbeat >= @MaxIdle) = 0 THEN 'ISSUE: No ready users'
        ELSE 'OK'
    END AS Status
UNION ALL
SELECT 
    'Active Assignments' AS CheckType,
    CASE 
        WHEN (SELECT COUNT(*) FROM AnalysisAssignments WHERE Role = 'Analyst' AND State = 'Active' AND (LeaseUntilUtc IS NULL OR LeaseUntilUtc > @Now)) = 0 THEN 'INFO: No assignments yet (may be normal)'
        ELSE 'OK'
    END AS Status;

PRINT '';
PRINT 'Next Steps:';
PRINT '1. If "Ready Users" shows ISSUE: Update heartbeats (already done)';
PRINT '2. If all checks show OK: Check service logs for [ASSIGNMENT] messages';
PRINT '3. If service is not running: Start the service';
PRINT '4. Check if assignment workflow is executing (look for [ASSIGNMENT-POLLING] logs)';


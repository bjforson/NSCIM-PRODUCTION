-- Check Why No Assignments Are Being Created
-- Comprehensive diagnostic to identify the exact issue

DECLARE @Now DATETIME2 = GETUTCDATE();
DECLARE @MaxIdleMinutes INT = 60;
DECLARE @MaxIdle DATETIME2 = DATEADD(MINUTE, -@MaxIdleMinutes, @Now);

-- Step 1: Verify Settings
PRINT '=== STEP 1: SETTINGS ===';
SELECT 
    'Settings' AS CheckType,
    Enabled,
    AssignmentMode,
    MaxConcurrentPerUser,
    CASE 
        WHEN Enabled = 0 THEN '❌ SERVICE DISABLED'
        WHEN AssignmentMode != 'Auto' THEN '❌ NOT IN AUTO MODE'
        ELSE '✅ OK'
    END AS Status
FROM AnalysisSettings;
PRINT '';

-- Step 2: Count Ready Groups
PRINT '=== STEP 2: READY GROUPS ===';
DECLARE @ReadyGroupsCount INT;
SELECT @ReadyGroupsCount = COUNT(*) 
FROM AnalysisGroups 
WHERE Status = 'Ready';

SELECT 
    'Ready Groups' AS CheckType,
    @ReadyGroupsCount AS Count,
    CASE 
        WHEN @ReadyGroupsCount = 0 THEN '❌ NO GROUPS'
        ELSE '✅ OK - ' + CAST(@ReadyGroupsCount AS VARCHAR) + ' groups'
    END AS Status;
PRINT '';

-- Step 3: Check Ready Users (Database Query - Matches Code)
PRINT '=== STEP 3: READY USERS (DATABASE - MATCHES CODE LOGIC) ===';
DECLARE @ReadyUsersCount INT;

SELECT @ReadyUsersCount = COUNT(DISTINCT Username)
FROM UserReadiness
WHERE Role = 'Analyst'
    AND IsReady = 1
    AND LastHeartbeat >= @MaxIdle;

SELECT 
    'Ready Users (Database)' AS CheckType,
    @ReadyUsersCount AS Count,
    CASE 
        WHEN @ReadyUsersCount = 0 THEN '❌ NO READY USERS'
        ELSE '✅ OK - ' + CAST(@ReadyUsersCount AS VARCHAR) + ' users'
    END AS Status;

-- Show details
SELECT 
    ur.Username,
    ur.IsReady,
    ur.LastHeartbeat,
    DATEDIFF(MINUTE, ur.LastHeartbeat, @Now) AS MinutesSinceHeartbeat,
    CASE 
        WHEN ur.IsReady = 1 AND ur.LastHeartbeat >= @MaxIdle THEN '✅ READY'
        WHEN ur.IsReady = 0 THEN '❌ NOT READY'
        ELSE '❌ HEARTBEAT EXPIRED'
    END AS Status
FROM UserReadiness ur
WHERE ur.Role = 'Analyst'
ORDER BY ur.LastHeartbeat DESC;
PRINT '';

-- Step 4: Verify Users Have Correct Role (Code Verification Step)
PRINT '=== STEP 4: USERS WITH ANALYST ROLE (CODE VERIFICATION) ===';

-- Get Analyst role ID
DECLARE @AnalystRoleId INT;
SELECT @AnalystRoleId = Id 
FROM Roles 
WHERE Name = 'Analyst' AND IsActive = 1;

SELECT 
    'Analyst Role ID' AS CheckType,
    @AnalystRoleId AS RoleId,
    CASE 
        WHEN @AnalystRoleId IS NULL THEN '❌ ROLE NOT FOUND'
        ELSE '✅ OK'
    END AS Status;

-- Check which ready users have the Analyst role
SELECT 
    u.Username,
    u.IsActive AS UserActive,
    u.RoleId,
    CASE 
        WHEN u.RoleId = @AnalystRoleId THEN '✅ HAS ROLE'
        WHEN u.RoleId IS NULL THEN '❌ NO ROLE'
        ELSE '❌ WRONG ROLE'
    END AS Status
FROM UserReadiness ur
INNER JOIN Users u ON u.Username = ur.Username
WHERE ur.Role = 'Analyst'
    AND ur.IsReady = 1
    AND ur.LastHeartbeat >= @MaxIdle
    AND u.IsActive = 1;
PRINT '';

-- Step 5: Count Users That Should Be Ready (Final Verification)
PRINT '=== STEP 5: FINAL VERIFICATION (USERS THAT WILL PASS CODE CHECKS) ===';

-- Final verification - count users that will pass code checks
SELECT 
    COUNT(DISTINCT u.Username) AS FinalReadyUsersCount,
    CASE 
        WHEN COUNT(DISTINCT u.Username) = 0 THEN '❌ NO USERS WILL PASS CODE VERIFICATION'
        ELSE '✅ OK - ' + CAST(COUNT(DISTINCT u.Username) AS VARCHAR) + ' users will pass'
    END AS Status
FROM UserReadiness ur
INNER JOIN Users u ON u.Username = ur.Username
INNER JOIN Roles r ON r.Id = u.RoleId
WHERE ur.Role = 'Analyst'
    AND ur.IsReady = 1
    AND ur.LastHeartbeat >= @MaxIdle
    AND u.IsActive = 1
    AND u.RoleId = @AnalystRoleId
    AND r.IsActive = 1;

-- Show users that will pass code checks
SELECT 
    u.Username,
    u.RoleId,
    r.Name AS RoleName,
    '✅ WILL PASS' AS Status
FROM UserReadiness ur
INNER JOIN Users u ON u.Username = ur.Username
INNER JOIN Roles r ON r.Id = u.RoleId
WHERE ur.Role = 'Analyst'
    AND ur.IsReady = 1
    AND ur.LastHeartbeat >= @MaxIdle
    AND u.IsActive = 1
    AND u.RoleId = @AnalystRoleId
    AND r.IsActive = 1
ORDER BY u.Username;
PRINT '';

-- Step 6: Check Active Assignments
PRINT '=== STEP 6: ACTIVE ASSIGNMENTS ===';
SELECT 
    COUNT(*) AS ActiveAssignmentsCount,
    CASE 
        WHEN COUNT(*) = 0 THEN '⚠️ NO ACTIVE ASSIGNMENTS (may be normal if workflow not running)'
        ELSE '✅ OK - ' + CAST(COUNT(*) AS VARCHAR) + ' active assignments'
    END AS Status
FROM AnalysisAssignments
WHERE Role = 'Analyst'
    AND State = 'Active'
    AND (LeaseUntilUtc IS NULL OR LeaseUntilUtc > @Now);
PRINT '';

-- Step 7: Summary
PRINT '=== SUMMARY ===';
SELECT 
    (SELECT CASE WHEN Enabled = 1 AND AssignmentMode = 'Auto' THEN '✅' ELSE '❌' END FROM AnalysisSettings) AS Settings,
    (SELECT CASE WHEN @ReadyGroupsCount > 0 THEN '✅' ELSE '❌' END) AS ReadyGroups,
    (SELECT CASE WHEN @ReadyUsersCount > 0 THEN '✅' ELSE '❌' END) AS ReadyUsers,
    (SELECT CASE WHEN @AnalystRoleId IS NOT NULL THEN '✅' ELSE '❌' END) AS AnalystRole,
    (SELECT CASE WHEN COUNT(*) > 0 THEN '✅' ELSE '⚠️' END FROM AnalysisAssignments WHERE Role = 'Analyst' AND State = 'Active' AND (LeaseUntilUtc IS NULL OR LeaseUntilUtc > @Now)) AS ActiveAssignments;

PRINT '';
PRINT 'If all show ✅ but no assignments:';
PRINT '  1. Check service logs for [ASSIGNMENT] messages';
PRINT '  2. Check if service is running';
PRINT '  3. Check if workflow is executing';
PRINT '  4. Look for errors in logs';


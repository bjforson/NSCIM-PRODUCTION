-- Update User Readiness Heartbeats (All Roles)
-- Sets LastHeartbeat to current time for users with IsReady=1
-- This makes them immediately ready for assignment (within 60-minute window)

DECLARE @UpdatedCount INT;
DECLARE @AnalystCount INT;
DECLARE @AuditCount INT;

-- Update heartbeats for Analyst role
UPDATE UserReadiness 
SET LastHeartbeat = GETUTCDATE(),
    LastChangedAt = GETUTCDATE()
WHERE Role = 'Analyst' 
    AND IsReady = 1
    AND LastHeartbeat < DATEADD(MINUTE, -60, GETUTCDATE());

SET @AnalystCount = @@ROWCOUNT;

-- Update heartbeats for Audit role
UPDATE UserReadiness 
SET LastHeartbeat = GETUTCDATE(),
    LastChangedAt = GETUTCDATE()
WHERE Role = 'Audit' 
    AND IsReady = 1
    AND LastHeartbeat < DATEADD(MINUTE, -60, GETUTCDATE());

SET @AuditCount = @@ROWCOUNT;

SET @UpdatedCount = @AnalystCount + @AuditCount;

-- Summary
SELECT 
    'Summary' AS ReportType,
    @UpdatedCount AS TotalUpdated,
    @AnalystCount AS AnalystUpdated,
    @AuditCount AS AuditUpdated,
    'Heartbeats updated to current time' AS Status;

PRINT '========================================';
PRINT 'User Readiness Heartbeat Update Summary';
PRINT '========================================';
PRINT 'Total Updated: ' + CAST(@UpdatedCount AS VARCHAR);
PRINT 'Analyst: ' + CAST(@AnalystCount AS VARCHAR);
PRINT 'Audit: ' + CAST(@AuditCount AS VARCHAR);
PRINT '';

-- Show updated records for Analyst
PRINT 'Analyst Role - Updated Records:';
SELECT 
    Username,
    IsReady,
    LastHeartbeat,
    DATEDIFF(MINUTE, LastHeartbeat, GETUTCDATE()) AS MinutesSinceHeartbeat,
    'READY' AS Status
FROM UserReadiness
WHERE Role = 'Analyst'
    AND IsReady = 1
ORDER BY LastHeartbeat DESC;

-- Show updated records for Audit
PRINT 'Audit Role - Updated Records:';
SELECT 
    Username,
    IsReady,
    LastHeartbeat,
    DATEDIFF(MINUTE, LastHeartbeat, GETUTCDATE()) AS MinutesSinceHeartbeat,
    'READY' AS Status
FROM UserReadiness
WHERE Role = 'Audit'
    AND IsReady = 1
ORDER BY LastHeartbeat DESC;

PRINT '';
PRINT '✅ User readiness heartbeats updated successfully';
PRINT '   Users with IsReady=1 now have current heartbeat';
PRINT '   These users are ready for assignment (60-minute window)';


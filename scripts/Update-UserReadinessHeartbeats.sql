-- Update User Readiness Heartbeats
-- Sets LastHeartbeat to current time for users with IsReady=1
-- This makes them immediately ready for assignment (within 60-minute window)

DECLARE @UpdatedCount INT;

-- Update heartbeats for Analyst role
UPDATE UserReadiness 
SET LastHeartbeat = GETUTCDATE(),
    LastChangedAt = GETUTCDATE()
WHERE Role = 'Analyst' 
    AND IsReady = 1
    AND LastHeartbeat < DATEADD(MINUTE, -60, GETUTCDATE());

SET @UpdatedCount = @@ROWCOUNT;

SELECT 
    'Analyst' AS Role,
    @UpdatedCount AS UpdatedCount,
    'Heartbeats updated to current time' AS Status;

-- Show updated records
SELECT 
    Username,
    Role,
    IsReady,
    LastHeartbeat,
    DATEDIFF(MINUTE, LastHeartbeat, GETUTCDATE()) AS MinutesSinceHeartbeat,
    CASE 
        WHEN IsReady = 1 AND DATEDIFF(MINUTE, LastHeartbeat, GETUTCDATE()) <= 60 THEN 'READY'
        WHEN IsReady = 0 THEN 'NOT READY'
        ELSE 'HEARTBEAT EXPIRED'
    END AS Status
FROM UserReadiness
WHERE Role = 'Analyst'
    AND IsReady = 1
ORDER BY LastHeartbeat DESC;

PRINT '✅ User readiness heartbeats updated successfully';
PRINT '   Updated ' + CAST(@UpdatedCount AS VARCHAR) + ' records';
PRINT '   Users with IsReady=1 now have current heartbeat';


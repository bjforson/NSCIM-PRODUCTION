-- Check UserReadiness heartbeats to see why users aren't ready

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

-- Summary
SELECT 
    Role,
    COUNT(*) AS TotalUsers,
    SUM(CASE WHEN IsReady = 1 THEN 1 ELSE 0 END) AS ReadyUsers,
    SUM(CASE WHEN IsReady = 1 AND DATEDIFF(MINUTE, LastHeartbeat, GETUTCDATE()) <= 60 THEN 1 ELSE 0 END) AS ReadyWithin60Min,
    SUM(CASE WHEN IsReady = 1 AND DATEDIFF(MINUTE, LastHeartbeat, GETUTCDATE()) > 60 THEN 1 ELSE 0 END) AS ReadyButExpired
FROM UserReadiness
WHERE Role IN ('Analyst', 'Audit')
GROUP BY Role;


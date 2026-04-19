-- =====================================================
-- Fix Stale User Readiness Records
-- =====================================================
-- This script cleans up expired user readiness records
-- that are blocking assignments
-- =====================================================

-- 1. Clean up expired user readiness records
UPDATE UserReadiness 
SET IsReady = 0, LastChangedAt = GETUTCDATE()
WHERE IsReady = 1 
AND LastHeartbeat < DATEADD(MINUTE, -5, GETUTCDATE());

-- 2. Verify cleanup (should show all IsReady = 0 for expired heartbeats)
SELECT 
    Username, 
    Role, 
    IsReady, 
    LastHeartbeat, 
    DATEDIFF(MINUTE, LastHeartbeat, GETUTCDATE()) AS MinutesSinceHeartbeat,
    CASE 
        WHEN IsReady = 0 THEN '✅ NOT READY (cleaned up)'
        WHEN LastHeartbeat < DATEADD(MINUTE, -5, GETUTCDATE()) THEN '❌ EXPIRED BUT STILL MARKED READY'
        ELSE '✅ READY (valid heartbeat)'
    END AS Status
FROM UserReadiness 
WHERE Role = 'Analyst'
ORDER BY LastHeartbeat DESC;

-- 3. Check current active assignments (user might be at capacity)
SELECT 
    AssignedTo,
    Role,
    COUNT(*) AS ActiveAssignmentCount,
    (SELECT MaxConcurrentPerUser FROM AnalysisSettings) AS MaxCapacity,
    CASE 
        WHEN COUNT(*) >= (SELECT MaxConcurrentPerUser FROM AnalysisSettings) THEN '❌ AT CAPACITY'
        ELSE CONCAT('✅ AVAILABLE (', (SELECT MaxConcurrentPerUser FROM AnalysisSettings) - COUNT(*), ' slots remaining)')
    END AS Status
FROM AnalysisAssignments
WHERE State = 'Active'
AND (LeaseUntilUtc IS NULL OR LeaseUntilUtc > GETUTCDATE())
GROUP BY AssignedTo, Role
ORDER BY ActiveAssignmentCount DESC;


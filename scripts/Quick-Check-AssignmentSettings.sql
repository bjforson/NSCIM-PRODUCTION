-- Quick Check: Assignment Settings (Most Common Issue)
-- Run this in SQL Server Management Studio to check the most common issue

SELECT 
    'Assignment Settings' AS CheckType,
    Enabled,
    AssignmentMode,
    AutoAssignStrategy,
    MaxConcurrentPerUser,
    LeaseMinutes,
    CASE 
        WHEN Enabled = 0 THEN 'SERVICE DISABLED'
        WHEN AssignmentMode != 'Auto' THEN 'NOT IN AUTO MODE (MOST COMMON ISSUE)'
        WHEN AssignmentMode = 'Auto' AND Enabled = 1 THEN 'OK'
        ELSE 'CHECK CONFIGURATION'
    END AS Status
FROM AnalysisSettings;

-- If AssignmentMode != 'Auto', fix with:
-- UPDATE AnalysisSettings SET AssignmentMode = 'Auto', UpdatedAtUtc = GETUTCDATE();


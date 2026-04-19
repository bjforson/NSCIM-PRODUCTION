-- Clear expired assignments (set State = 'Expired')
-- These are Active assignments whose LeaseUntilUtc has passed.
-- Safe to run: only affects already-expired leases.

-- Preview: count what will be cleared
SELECT COUNT(*) AS WillExpire
FROM AnalysisAssignments
WHERE State = 'Active'
    AND LeaseUntilUtc IS NOT NULL
    AND LeaseUntilUtc <= GETUTCDATE();

-- Execute: mark as Expired
UPDATE AnalysisAssignments
SET State = 'Expired', UpdatedAtUtc = GETUTCDATE()
WHERE State = 'Active'
    AND LeaseUntilUtc IS NOT NULL
    AND LeaseUntilUtc <= GETUTCDATE();

-- Verify: show updated count
SELECT @@ROWCOUNT AS RowsExpired;

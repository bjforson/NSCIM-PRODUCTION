# Quick check for recent assignments
# Run this after waiting 1-2 minutes

param(
    [string]$Server = "127.0.0.1,1433",
    [string]$Database = "NS_CIS",
    [int]$MinutesAgo = 10
)

# Continues past errors intentionally: read-only diagnostic listing recent assignments; report continues even if a query returns no rows or errors.
$ErrorActionPreference = "Continue"

Write-Host "Check Recent Assignments (Last $MinutesAgo Minutes)" -ForegroundColor Cyan
Write-Host "===================================================" -ForegroundColor Cyan
Write-Host ""

$query = @"
SELECT TOP 20
    aa.Id,
    aa.AssignedTo,
    aa.Role,
    aa.State,
    aa.CreatedAtUtc,
    DATEDIFF(second, aa.CreatedAtUtc, GETUTCDATE()) AS SecondsAgo,
    ag.GroupIdentifier
FROM AnalysisAssignments aa
INNER JOIN AnalysisGroups ag ON ag.Id = aa.GroupId
WHERE aa.Role = 'Analyst'
    AND aa.CreatedAtUtc > DATEADD(minute, -$MinutesAgo, GETUTCDATE())
ORDER BY aa.CreatedAtUtc DESC
"@

$assignments = Invoke-Sqlcmd -ServerInstance $Server -Database $Database -Query $query

if ($assignments) {
    Write-Host "SUCCESS: Found $($assignments.Count) assignment(s) in last $MinutesAgo minutes!" -ForegroundColor Green
    Write-Host ""
    $assignments | Format-Table -Property AssignedTo, GroupIdentifier, State, SecondsAgo -AutoSize
    Write-Host ""
    Write-Host "Assignments are being created! Check the Image Analysis page UI." -ForegroundColor Green
} else {
    Write-Host "ISSUE: No assignments created in last $MinutesAgo minutes" -ForegroundColor Red
    Write-Host ""
    Write-Host "This means AssignmentWorker is not creating assignments." -ForegroundColor Yellow
    Write-Host ""
    Write-Host "Check:" -ForegroundColor Cyan
    Write-Host "  1. Application logs for [ASSIGNMENT-WORKER] or [AUTO-ASSIGN] messages" -ForegroundColor White
    Write-Host "  2. AssignmentWorker service is running" -ForegroundColor White
    Write-Host "  3. No errors in AssignmentWorker logs" -ForegroundColor White
    Write-Host "  4. Analyst heartbeat is updating (should update every 30 seconds)" -ForegroundColor White
}

Write-Host ""


# Fix Expired Assignments - Clean up expired assignments blocking new ones
# This is blocking AssignmentWorker from creating new assignments

param(
    [string]$Server = "127.0.0.1,1433",
    [string]$Database = "NS_CIS",
    [switch]$DryRun
)

$ErrorActionPreference = "Stop"  # 2026-04-28: was "Continue" — silent failures masked breakage. Wrap genuinely tolerated steps in try/catch.

Write-Host "Fix Expired Assignments" -ForegroundColor Cyan
Write-Host "======================" -ForegroundColor Cyan
Write-Host ""

# Check expired assignments
$expiredQuery = @"
SELECT 
    COUNT(*) AS ExpiredCount,
    MIN(LeaseUntilUtc) AS OldestExpired,
    MAX(LeaseUntilUtc) AS NewestExpired
FROM AnalysisAssignments
WHERE State = 'Active'
    AND LeaseUntilUtc < GETUTCDATE()
"@

$expired = Invoke-Sqlcmd -ServerInstance $Server -Database $Database -Query $expiredQuery

Write-Host "Expired Assignments:" -ForegroundColor Yellow
Write-Host "  Count: $($expired.ExpiredCount)" -ForegroundColor White
Write-Host "  Oldest: $($expired.OldestExpired)" -ForegroundColor White
Write-Host "  Newest: $($expired.NewestExpired)" -ForegroundColor White
Write-Host ""

if ($expired.ExpiredCount -eq 0) {
    Write-Host "SUCCESS: No expired assignments to clean up" -ForegroundColor Green
    exit 0
}

if ($DryRun) {
    Write-Host "DRY RUN: Would update $($expired.ExpiredCount) expired assignments" -ForegroundColor Yellow
    Write-Host "  Run without -DryRun to actually update" -ForegroundColor Yellow
    exit 0
}

Write-Host "Updating expired assignments..." -ForegroundColor Yellow

# Update expired assignments to 'Expired' state
$updateQuery = @"
UPDATE AnalysisAssignments
SET 
    State = 'Expired',
    UpdatedAtUtc = GETUTCDATE()
WHERE State = 'Active'
    AND LeaseUntilUtc < GETUTCDATE()
"@

try {
    Invoke-Sqlcmd -ServerInstance $Server -Database $Database -Query $updateQuery
    Write-Host "SUCCESS: Updated expired assignments to 'Expired' state" -ForegroundColor Green
    Write-Host ""
    
    # Verify
    $verifyQuery = "SELECT COUNT(*) AS RemainingExpired FROM AnalysisAssignments WHERE State = 'Active' AND LeaseUntilUtc < GETUTCDATE()"
    $remaining = Invoke-Sqlcmd -ServerInstance $Server -Database $Database -Query $verifyQuery
    
    if ($remaining.RemainingExpired -eq 0) {
        Write-Host "VERIFIED: All expired assignments cleaned up" -ForegroundColor Green
    } else {
        Write-Host "WARNING: $($remaining.RemainingExpired) expired assignments still remain" -ForegroundColor Yellow
    }
} catch {
    Write-Host "ERROR: Failed to update expired assignments" -ForegroundColor Red
    Write-Host "  Error: $($_.Exception.Message)" -ForegroundColor Red
}

Write-Host ""


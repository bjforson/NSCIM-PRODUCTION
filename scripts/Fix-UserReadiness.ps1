# Fix UserReadiness - Set analyst as ready and update heartbeat
# This manually fixes the readiness status

param(
    [string]$Server = "127.0.0.1,1433",
    [string]$Database = "NS_CIS",
    [string]$Username = "analyst"  # Change to the actual username
)

$ErrorActionPreference = "Stop"  # 2026-04-28: was "Continue" — silent failures masked breakage. Wrap genuinely tolerated steps in try/catch.

Write-Host "Fix UserReadiness for Analyst" -ForegroundColor Cyan
Write-Host "=============================" -ForegroundColor Cyan
Write-Host ""

Write-Host "Updating UserReadiness for: $Username" -ForegroundColor Yellow
Write-Host ""

$updateQuery = @"
UPDATE UserReadiness
SET 
    IsReady = 1,
    LastHeartbeat = GETUTCDATE(),
    LastChangedAt = GETUTCDATE(),
    ChangedBy = '$Username'
WHERE Username = '$Username' AND Role = 'Analyst'
"@

try {
    Invoke-Sqlcmd -ServerInstance $Server -Database $Database -Query $updateQuery
    Write-Host "SUCCESS: Updated UserReadiness" -ForegroundColor Green
    Write-Host ""
    
    # Verify
    $verifyQuery = @"
SELECT 
    Username,
    Role,
    IsReady,
    LastHeartbeat,
    DATEDIFF(second, LastHeartbeat, GETUTCDATE()) AS SecondsAgo
FROM UserReadiness
WHERE Username = '$Username' AND Role = 'Analyst'
"@
    
    $result = Invoke-Sqlcmd -ServerInstance $Server -Database $Database -Query $verifyQuery
    
    Write-Host "Updated Status:" -ForegroundColor Cyan
    $result | Format-Table -AutoSize
    
    if ($result.IsReady -eq $true -and $result.SecondsAgo -lt 120) {
        Write-Host ""
        Write-Host "SUCCESS: User is now READY for assignments!" -ForegroundColor Green
        Write-Host "  Heartbeat will need to be updated by the page (every 30 seconds)" -ForegroundColor Yellow
        Write-Host "  If heartbeat doesn't update, check browser console for API errors" -ForegroundColor Yellow
    }
} catch {
    Write-Host "ERROR: Failed to update UserReadiness" -ForegroundColor Red
    Write-Host "  Error: $($_.Exception.Message)" -ForegroundColor Red
}

Write-Host ""


# Check if user has a UserReadiness record and create one if needed
# This is required for heartbeats to work

param(
    [string]$Server = "127.0.0.1,1433",
    [string]$Database = "NS_CIS",
    [string]$Username = ""  # If empty, will check all analysts
)

$ErrorActionPreference = "Continue"

Write-Host "Check UserReadiness Records" -ForegroundColor Cyan
Write-Host "===========================" -ForegroundColor Cyan
Write-Host ""

# Get all analysts
$analystsQuery = @"
SELECT 
    u.Id,
    u.UserName,
    u.Email,
    r.Id AS RoleId,
    r.Name AS RoleName
FROM Users u
INNER JOIN Roles r ON r.Id = u.RoleId
WHERE r.Name = 'Analyst' AND u.IsActive = 1
"@

$analysts = Invoke-Sqlcmd -ServerInstance $Server -Database $Database -Query $analystsQuery

if (-not $analysts) {
    Write-Host "No analysts found" -ForegroundColor Red
    exit 0
}

Write-Host "Analysts:" -ForegroundColor Yellow
$analysts | Format-Table -Property UserName, Email, RoleName -AutoSize
Write-Host ""

# Check UserReadiness records for each analyst
foreach ($analyst in $analysts) {
    if (-not [string]::IsNullOrEmpty($Username) -and $analyst.UserName -ne $Username) {
        continue
    }
    
    Write-Host "Checking: $($analyst.UserName)" -ForegroundColor Cyan
    Write-Host "------------------------" -ForegroundColor Cyan
    
    $readinessQuery = @"
SELECT 
    Username,
    Role,
    IsReady,
    LastHeartbeat,
    DATEDIFF(second, LastHeartbeat, GETUTCDATE()) AS SecondsAgo
FROM UserReadiness
WHERE Username = '$($analyst.UserName)' AND Role = 'Analyst'
"@
    
    $readiness = Invoke-Sqlcmd -ServerInstance $Server -Database $Database -Query $readinessQuery
    
    if ($readiness) {
        Write-Host "SUCCESS: UserReadiness record exists" -ForegroundColor Green
        Write-Host "  IsReady: $($readiness.IsReady)" -ForegroundColor White
        Write-Host "  LastHeartbeat: $($readiness.LastHeartbeat)" -ForegroundColor White
        Write-Host "  Seconds Ago: $($readiness.SecondsAgo)" -ForegroundColor White
        
        if ($readiness.SecondsAgo -lt 120 -and $readiness.IsReady -eq $true) {
            Write-Host "  Status: READY" -ForegroundColor Green
        } else {
            Write-Host "  Status: NOT READY" -ForegroundColor Red
            if ($readiness.IsReady -eq $false) {
                Write-Host "    Reason: IsReady = false" -ForegroundColor Yellow
            }
            if ($readiness.SecondsAgo -ge 120) {
                Write-Host "    Reason: Heartbeat stale ($($readiness.SecondsAgo) seconds ago)" -ForegroundColor Yellow
            }
        }
    } else {
        Write-Host "ISSUE: No UserReadiness record found" -ForegroundColor Red
        Write-Host "  This is why heartbeats aren't updating!" -ForegroundColor Yellow
        Write-Host ""
        Write-Host "  To fix, the user needs to:" -ForegroundColor Cyan
        Write-Host "    1. Be on the Image Analysis page" -ForegroundColor White
        Write-Host "    2. Call POST /api/image-analysis/user/ready with Role='Analyst', IsReady=true" -ForegroundColor White
        Write-Host "    3. This creates the UserReadiness record" -ForegroundColor White
        Write-Host "    4. Then heartbeats will update it" -ForegroundColor White
        Write-Host ""
        Write-Host "  OR create manually via SQL:" -ForegroundColor Cyan
        Write-Host "    INSERT INTO UserReadiness (Username, Role, IsReady, LastHeartbeat, LastChangedAt, ChangedBy)" -ForegroundColor White
        Write-Host "    VALUES ('$($analyst.UserName)', 'Analyst', 1, GETUTCDATE(), GETUTCDATE(), '$($analyst.UserName)')" -ForegroundColor White
    }
    Write-Host ""
}

Write-Host "========================================" -ForegroundColor Cyan
Write-Host "SOLUTION" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""
Write-Host "If no UserReadiness record exists, the user needs to:" -ForegroundColor Yellow
Write-Host "  1. Refresh the Image Analysis page" -ForegroundColor White
Write-Host "  2. The page should call SetReadyForAssignment(true) on load" -ForegroundColor White
Write-Host "  3. This creates the UserReadiness record" -ForegroundColor White
Write-Host "  4. Then heartbeats will update it every 30 seconds" -ForegroundColor White
Write-Host ""
Write-Host "Check browser console for errors calling /api/image-analysis/user/ready" -ForegroundColor Cyan
Write-Host ""


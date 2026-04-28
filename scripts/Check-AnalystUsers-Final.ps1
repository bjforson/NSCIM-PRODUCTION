# Check for users with Analyst role - Final Check
# System uses Users.RoleId to link to Roles table

param(
    [string]$Server = "127.0.0.1,1433",
    [string]$Database = "NS_CIS"
)

# Continues past errors intentionally: read-only listing of analyst users; report continues even if a Format-Table or per-row enrichment hits a transient issue.
$ErrorActionPreference = "Continue"

Write-Host "Final Check: Users with Analyst Role" -ForegroundColor Cyan
Write-Host "====================================" -ForegroundColor Cyan
Write-Host ""

# Get Analyst role ID
$roleQuery = "SELECT Id, Name FROM Roles WHERE Name = 'Analyst' AND IsActive = 1"
$analystRole = Invoke-Sqlcmd -ServerInstance $Server -Database $Database -Query $roleQuery

if (-not $analystRole) {
    Write-Host "ERROR: Analyst role not found or not active" -ForegroundColor Red
    exit 1
}

$roleId = $analystRole.Id
Write-Host "Analyst Role:" -ForegroundColor Green
Write-Host "  ID: $roleId" -ForegroundColor White
Write-Host "  Name: $($analystRole.Name)" -ForegroundColor White
Write-Host ""

# Find users with Analyst role
$analystsQuery = @"
SELECT 
    u.Id,
    u.UserName,
    u.Email,
    u.RoleId,
    r.Name AS RoleName,
    u.IsActive
FROM Users u
INNER JOIN Roles r ON r.Id = u.RoleId
WHERE r.Name = 'Analyst' AND u.IsActive = 1
ORDER BY u.UserName
"@

$analysts = Invoke-Sqlcmd -ServerInstance $Server -Database $Database -Query $analystsQuery

Write-Host "Users with Analyst Role:" -ForegroundColor Yellow
Write-Host "----------------------" -ForegroundColor Yellow

if ($analysts) {
    Write-Host "FOUND: $($analysts.Count) user(s) with Analyst role:" -ForegroundColor Green
    Write-Host ""
    $analysts | Format-Table -Property UserName, Email, RoleName, IsActive -AutoSize
    Write-Host ""
    Write-Host "CONFIRMED: Users with Analyst role exist" -ForegroundColor Green
    Write-Host ""
    Write-Host "These users should be able to receive assignments." -ForegroundColor Cyan
} else {
    Write-Host "CONFIRMED: NO users have Analyst role assigned" -ForegroundColor Red
    Write-Host ""
    Write-Host "This is the PRIMARY BLOCKER preventing assignments." -ForegroundColor Yellow
    Write-Host ""
    Write-Host "To assign Analyst role to a user, run:" -ForegroundColor Cyan
    Write-Host "  UPDATE Users SET RoleId = $roleId WHERE UserName = 'username' AND IsActive = 1" -ForegroundColor White
    Write-Host ""
    Write-Host "Available users:" -ForegroundColor Cyan
    $allUsersQuery = "SELECT TOP 10 Id, UserName, Email, RoleId, IsActive FROM Users WHERE IsActive = 1 ORDER BY UserName"
    $allUsers = Invoke-Sqlcmd -ServerInstance $Server -Database $Database -Query $allUsersQuery
    if ($allUsers) {
        $allUsers | Format-Table -AutoSize
    }
}

Write-Host ""


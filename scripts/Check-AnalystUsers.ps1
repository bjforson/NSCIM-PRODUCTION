# Check for users with Analyst role
# Direct verification query

param(
    [string]$Server = "127.0.0.1,1433",
    [string]$Database = "NS_CIS"
)

$ErrorActionPreference = "Stop"  # 2026-04-27: was "Continue" — silent failures masked migration breakage in 56 scripts. Use try/catch where you genuinely want to continue past a step.

Write-Host "Checking for users with Analyst role..." -ForegroundColor Cyan
Write-Host "=======================================" -ForegroundColor Cyan
Write-Host ""

# Check 1: Verify Analyst role exists
Write-Host "Check 1: Analyst Role" -ForegroundColor Yellow
Write-Host "-------------------" -ForegroundColor Yellow

$roleQuery = "SELECT Id, Name FROM AspNetRoles WHERE Name = 'Analyst'"
try {
    $analystRole = Invoke-Sqlcmd -ServerInstance $Server -Database $Database -Query $roleQuery
    
    if ($analystRole) {
        Write-Host "SUCCESS: Analyst role exists" -ForegroundColor Green
        Write-Host "  Role ID: $($analystRole.Id)" -ForegroundColor White
        Write-Host "  Role Name: $($analystRole.Name)" -ForegroundColor White
        $roleId = $analystRole.Id
    } else {
        Write-Host "WARNING: Analyst role does not exist" -ForegroundColor Yellow
        Write-Host "  Need to create the role first" -ForegroundColor Yellow
        exit 0
    }
} catch {
    Write-Host "ERROR: Could not query AspNetRoles table" -ForegroundColor Red
    Write-Host "  Error: $($_.Exception.Message)" -ForegroundColor Red
    Write-Host "  This may indicate a database connection or schema issue" -ForegroundColor Yellow
    exit 1
}
Write-Host ""

# Check 2: Find all users with Analyst role
Write-Host "Check 2: Users with Analyst Role" -ForegroundColor Yellow
Write-Host "-------------------------------" -ForegroundColor Yellow

$analystsQuery = @"
SELECT 
    u.Id AS UserId,
    u.UserName,
    u.Email,
    u.NormalizedUserName,
    r.Id AS RoleId,
    r.Name AS RoleName
FROM AspNetUsers u
INNER JOIN AspNetUserRoles ur ON ur.UserId = u.Id
INNER JOIN AspNetRoles r ON r.Id = ur.RoleId
WHERE r.Name = 'Analyst'
ORDER BY u.UserName
"@

try {
    $analysts = Invoke-Sqlcmd -ServerInstance $Server -Database $Database -Query $analystsQuery
    
    if ($analysts) {
        Write-Host "FOUND: $($analysts.Count) user(s) with Analyst role:" -ForegroundColor Green
        Write-Host ""
        $analysts | Format-Table -Property UserName, Email, RoleName -AutoSize
        Write-Host ""
        Write-Host "CONFIRMED: Users with Analyst role exist" -ForegroundColor Green
    } else {
        Write-Host "CONFIRMED: NO users have Analyst role assigned" -ForegroundColor Red
        Write-Host ""
        Write-Host "This is why assignments are not being created." -ForegroundColor Yellow
        Write-Host "AssignmentWorker needs at least one user with Analyst role to assign groups to." -ForegroundColor Yellow
    }
} catch {
    Write-Host "ERROR: Could not query for Analyst users" -ForegroundColor Red
    Write-Host "  Error: $($_.Exception.Message)" -ForegroundColor Red
    Write-Host "  This may indicate a database connection or schema issue" -ForegroundColor Yellow
}

Write-Host ""

# Check 3: List all available users (for reference)
Write-Host "Check 3: All Available Users" -ForegroundColor Yellow
Write-Host "---------------------------" -ForegroundColor Yellow

$allUsersQuery = "SELECT TOP 20 Id, UserName, Email FROM AspNetUsers ORDER BY UserName"
try {
    $allUsers = Invoke-Sqlcmd -ServerInstance $Server -Database $Database -Query $allUsersQuery
    
    if ($allUsers) {
        Write-Host "Available users (first 20):" -ForegroundColor Cyan
        $allUsers | Format-Table -AutoSize
        Write-Host ""
        Write-Host "To assign Analyst role to a user, use:" -ForegroundColor Yellow
        Write-Host "  INSERT INTO AspNetUserRoles (UserId, RoleId)" -ForegroundColor White
        Write-Host "  SELECT u.Id, r.Id" -ForegroundColor White
        Write-Host "  FROM AspNetUsers u, AspNetRoles r" -ForegroundColor White
        Write-Host "  WHERE u.UserName = 'username' AND r.Name = 'Analyst'" -ForegroundColor White
    } else {
        Write-Host "WARNING: No users found in database" -ForegroundColor Yellow
    }
} catch {
    Write-Host "ERROR: Could not query AspNetUsers table" -ForegroundColor Red
    Write-Host "  Error: $($_.Exception.Message)" -ForegroundColor Red
}

Write-Host ""


# Check for users with Analyst role - Using custom table names
# Database uses Roles/Users instead of AspNetRoles/AspNetUsers

param(
    [string]$Server = "127.0.0.1,1433",
    [string]$Database = "NS_CIS"
)

# Continues past errors intentionally: probes multiple schema/table-name combinations; per-attempt failures are expected and trigger fallbacks.
$ErrorActionPreference = "Continue"

Write-Host "Checking for users with Analyst role (Custom Schema)..." -ForegroundColor Cyan
Write-Host "=====================================================" -ForegroundColor Cyan
Write-Host ""

# Check 1: Verify Analyst role exists
Write-Host "Check 1: Analyst Role" -ForegroundColor Yellow
Write-Host "-------------------" -ForegroundColor Yellow

$roleQuery = "SELECT TOP 1 Id, Name FROM Roles WHERE Name = 'Analyst'"
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
    }
} catch {
    Write-Host "ERROR: Could not query Roles table" -ForegroundColor Red
    Write-Host "  Error: $($_.Exception.Message)" -ForegroundColor Red
    Write-Host ""
    Write-Host "Let's check the Roles table structure:" -ForegroundColor Cyan
    $structureQuery = "SELECT TOP 5 * FROM Roles"
    try {
        $roles = Invoke-Sqlcmd -ServerInstance $Server -Database $Database -Query $structureQuery
        if ($roles) {
            Write-Host "Sample roles:" -ForegroundColor Green
            $roles | Format-Table -AutoSize
        }
    } catch {
        Write-Host "Could not query Roles table structure" -ForegroundColor Red
    }
    exit 1
}
Write-Host ""

# Check 2: Find all users with Analyst role
Write-Host "Check 2: Users with Analyst Role" -ForegroundColor Yellow
Write-Host "-------------------------------" -ForegroundColor Yellow

# Try different possible join structures
$queries = @(
    @{ Name = "Users + UserRoles + Roles"; Query = @"
SELECT 
    u.Id AS UserId,
    u.UserName,
    u.Email,
    r.Id AS RoleId,
    r.Name AS RoleName
FROM Users u
INNER JOIN UserRoles ur ON ur.UserId = u.Id
INNER JOIN Roles r ON r.Id = ur.RoleId
WHERE r.Name = 'Analyst'
ORDER BY u.UserName
"@ },
    @{ Name = "Check UserRoles table exists"; Query = "SELECT TOP 1 * FROM UserRoles" }
)

$foundAnalysts = $false
foreach ($q in $queries) {
    try {
        Write-Host "Trying: $($q.Name)..." -ForegroundColor Gray
        $result = Invoke-Sqlcmd -ServerInstance $Server -Database $Database -Query $q.Query
        
        if ($result -and $q.Name -like "*Users*") {
            Write-Host "FOUND: $($result.Count) user(s) with Analyst role:" -ForegroundColor Green
            Write-Host ""
            $result | Format-Table -Property UserName, Email, RoleName -AutoSize
            Write-Host ""
            Write-Host "CONFIRMED: Users with Analyst role exist" -ForegroundColor Green
            $foundAnalysts = $true
            break
        } elseif ($result -and $q.Name -like "*Check*") {
            Write-Host "UserRoles table exists, checking for Analyst assignments..." -ForegroundColor Cyan
        }
    } catch {
        Write-Host "  Query failed: $($_.Exception.Message)" -ForegroundColor Gray
    }
}

if (-not $foundAnalysts) {
    Write-Host ""
    Write-Host "CONFIRMED: NO users have Analyst role assigned" -ForegroundColor Red
    Write-Host ""
    Write-Host "This is why assignments are not being created." -ForegroundColor Yellow
    Write-Host "AssignmentWorker needs at least one user with Analyst role to assign groups to." -ForegroundColor Yellow
}

Write-Host ""

# Check 3: List all available users (for reference)
Write-Host "Check 3: All Available Users" -ForegroundColor Yellow
Write-Host "---------------------------" -ForegroundColor Yellow

$allUsersQuery = "SELECT TOP 20 Id, UserName, Email FROM Users ORDER BY UserName"
try {
    $allUsers = Invoke-Sqlcmd -ServerInstance $Server -Database $Database -Query $allUsersQuery
    
    if ($allUsers) {
        Write-Host "Available users (first 20):" -ForegroundColor Cyan
        $allUsers | Format-Table -AutoSize
        Write-Host ""
        Write-Host "To assign Analyst role, first check the UserRoles table structure:" -ForegroundColor Yellow
        Write-Host "  SELECT TOP 5 * FROM UserRoles" -ForegroundColor White
    } else {
        Write-Host "WARNING: No users found in database" -ForegroundColor Yellow
    }
} catch {
    Write-Host "ERROR: Could not query Users table" -ForegroundColor Red
    Write-Host "  Error: $($_.Exception.Message)" -ForegroundColor Red
}

Write-Host ""


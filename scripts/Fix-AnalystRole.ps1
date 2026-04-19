# Fix Analyst Role Assignment
# Step 2 of Image Analyst Assignment Fix

param(
    [string]$Server = "127.0.0.1,1433",
    [string]$Database = "NS_CIS",
    [string[]]$UserNames = @()  # If empty, will list all users for selection
)

$ErrorActionPreference = "Continue"

Write-Host "Fix 2: Assign Analyst Role" -ForegroundColor Cyan
Write-Host "===========================" -ForegroundColor Cyan
Write-Host ""

# Check if Analyst role exists
$checkRoleQuery = "SELECT Id, Name FROM AspNetRoles WHERE Name = 'Analyst'"
$analystRole = Invoke-Sqlcmd -ServerInstance $Server -Database $Database -Query $checkRoleQuery

if (-not $analystRole) {
    Write-Host "Creating 'Analyst' role..." -ForegroundColor Yellow
    
    $createRoleQuery = @"
INSERT INTO AspNetRoles (Id, Name, NormalizedName, ConcurrencyStamp)
VALUES (NEWID(), 'Analyst', 'ANALYST', NEWID())
"@
    
    Invoke-Sqlcmd -ServerInstance $Server -Database $Database -Query $createRoleQuery
    Write-Host "Analyst role created" -ForegroundColor Green
    
    # Re-fetch the role
    $analystRole = Invoke-Sqlcmd -ServerInstance $Server -Database $Database -Query $checkRoleQuery
}

$roleId = $analystRole.Id
Write-Host "Analyst Role ID: $roleId" -ForegroundColor Gray
Write-Host ""

# List all users
$usersQuery = "SELECT Id, UserName, Email FROM AspNetUsers ORDER BY UserName"
$allUsers = Invoke-Sqlcmd -ServerInstance $Server -Database $Database -Query $usersQuery

Write-Host "Available users:" -ForegroundColor Cyan
$allUsers | Format-Table -AutoSize
Write-Host ""

# Check current analyst assignments
$currentAnalystsQuery = @"
SELECT u.UserName, u.Email
FROM AspNetUsers u
INNER JOIN AspNetUserRoles ur ON ur.UserId = u.Id
INNER JOIN AspNetRoles r ON r.Id = ur.RoleId
WHERE r.Name = 'Analyst'
"@
$currentAnalysts = Invoke-Sqlcmd -ServerInstance $Server -Database $Database -Query $currentAnalystsQuery

if ($currentAnalysts) {
    Write-Host "Current Analysts:" -ForegroundColor Green
    $currentAnalysts | Format-Table -AutoSize
    Write-Host ""
} else {
    Write-Host "WARNING: No users currently have 'Analyst' role" -ForegroundColor Yellow
    Write-Host ""
}

# If no usernames provided, prompt for selection
if ($UserNames.Count -eq 0) {
    Write-Host "To assign Analyst role, run this script with -UserNames parameter:" -ForegroundColor Yellow
    Write-Host "  .\Fix-AnalystRole.ps1 -UserNames @('username1', 'username2')" -ForegroundColor White
    Write-Host ""
    Write-Host "Or assign manually via SQL:" -ForegroundColor Yellow
    Write-Host "  INSERT INTO AspNetUserRoles (UserId, RoleId)" -ForegroundColor White
    Write-Host "  SELECT u.Id, r.Id" -ForegroundColor White
    Write-Host "  FROM AspNetUsers u, AspNetRoles r" -ForegroundColor White
    Write-Host "  WHERE u.UserName = 'username' AND r.Name = 'Analyst'" -ForegroundColor White
    Write-Host "    AND NOT EXISTS (SELECT 1 FROM AspNetUserRoles ur WHERE ur.UserId = u.Id AND ur.RoleId = r.Id)" -ForegroundColor White
    Write-Host ""
    exit 0
}

# Assign role to specified users
$assignedCount = 0
$skippedCount = 0

foreach ($userName in $UserNames) {
    $user = $allUsers | Where-Object { $_.UserName -eq $userName }
    
    if (-not $user) {
        Write-Host "WARNING: User '$userName' not found - skipping" -ForegroundColor Yellow
        $skippedCount++
        continue
    }
    
    # Check if already assigned
    $userId = $user.Id
    $checkAssignmentQuery = "SELECT COUNT(*) AS Count FROM AspNetUserRoles WHERE UserId = '$userId' AND RoleId = '$roleId'"
    $check = Invoke-Sqlcmd -ServerInstance $Server -Database $Database -Query $checkAssignmentQuery
    
    if ($check.Count -gt 0) {
        Write-Host "INFO: User '$userName' already has Analyst role - skipping" -ForegroundColor Gray
        $skippedCount++
        continue
    }
    
    # Assign role
    $userId = $user.Id
    $assignQuery = 'INSERT INTO AspNetUserRoles (UserId, RoleId) VALUES (''' + $userId + ''', ''' + $roleId + ''')'
    
    Invoke-Sqlcmd -ServerInstance $Server -Database $Database -Query $assignQuery
    Write-Host "Assigned Analyst role to '$userName'" -ForegroundColor Green
    $assignedCount++
}

Write-Host ""
Write-Host "Summary:" -ForegroundColor Cyan
Write-Host "  - Assigned: $assignedCount" -ForegroundColor Green
Write-Host "  - Skipped: $skippedCount" -ForegroundColor Gray
Write-Host ""

# Verify final state
$finalAnalysts = Invoke-Sqlcmd -ServerInstance $Server -Database $Database -Query $currentAnalystsQuery

if ($finalAnalysts) {
    Write-Host "Fix 2 Complete: Analyst role assignments" -ForegroundColor Green
    Write-Host ""
    Write-Host "Current Analysts:" -ForegroundColor Cyan
    $finalAnalysts | Format-Table -AutoSize
} else {
    Write-Host "WARNING: No users have Analyst role assigned" -ForegroundColor Yellow
    Write-Host "   Run with -UserNames parameter to assign roles" -ForegroundColor Yellow
}


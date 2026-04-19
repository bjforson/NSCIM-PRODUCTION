# Fix AnalysisSettings - Create or Update Settings for Auto-Assignment
# Step 1 of Image Analyst Assignment Fix

param(
    [string]$Server = "127.0.0.1,1433",
    [string]$Database = "NS_CIS"
)

$ErrorActionPreference = "Continue"

Write-Host "Fix 1: AnalysisSettings" -ForegroundColor Cyan
Write-Host "======================" -ForegroundColor Cyan
Write-Host ""

# Check if settings exist
$checkQuery = "SELECT COUNT(*) AS Count FROM AnalysisSettings"
$check = Invoke-Sqlcmd -ServerInstance $Server -Database $Database -Query $checkQuery

if ($check.Count -eq 0) {
    Write-Host "Creating AnalysisSettings record..." -ForegroundColor Yellow
    
    $createQuery = @"
INSERT INTO AnalysisSettings (Enabled, AssignmentMode, AutoAssignStrategy, AutoAssign, LeaseMinutes, MaxConcurrentPerUser, CreatedAtUtc)
VALUES (1, 'Auto', 'RoundRobin', 1, 15, 5, GETUTCDATE())
"@
    
    Invoke-Sqlcmd -ServerInstance $Server -Database $Database -Query $createQuery
    Write-Host "✅ AnalysisSettings created successfully" -ForegroundColor Green
} else {
    Write-Host "AnalysisSettings exists. Updating to Auto mode..." -ForegroundColor Yellow
    
    $updateQuery = @"
UPDATE AnalysisSettings
SET 
    Enabled = 1,
    AssignmentMode = 'Auto',
    AutoAssignStrategy = 'RoundRobin',
    AutoAssign = 1,
    MaxConcurrentPerUser = 5,
    UpdatedAtUtc = GETUTCDATE()
WHERE Id = (SELECT TOP 1 Id FROM AnalysisSettings)
"@
    
    Invoke-Sqlcmd -ServerInstance $Server -Database $Database -Query $updateQuery
    Write-Host "✅ AnalysisSettings updated to Auto mode" -ForegroundColor Green
}

# Verify settings
Write-Host ""
Write-Host "Verifying settings..." -ForegroundColor Cyan
$verifyQuery = "SELECT Enabled, AssignmentMode, AutoAssignStrategy, MaxConcurrentPerUser, LeaseMinutes FROM AnalysisSettings"
$settings = Invoke-Sqlcmd -ServerInstance $Server -Database $Database -Query $verifyQuery

Write-Host "Current settings:" -ForegroundColor White
$settings | Format-Table -AutoSize

if ($settings.Enabled -eq $true -and $settings.AssignmentMode -eq "Auto") {
    Write-Host ""
    Write-Host "✅ Fix 1 Complete: AnalysisSettings configured for Auto-Assignment" -ForegroundColor Green
} else {
    Write-Host ""
    Write-Host "⚠️  Warning: Settings may not be correct" -ForegroundColor Yellow
}


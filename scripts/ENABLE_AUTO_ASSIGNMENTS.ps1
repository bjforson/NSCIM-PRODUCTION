# Enable Auto Assignments for Image Analysis
# This script enables automatic assignment of Ready groups to Analysts

Write-Host "========================================" -ForegroundColor Cyan
Write-Host "Enabling Auto Assignments" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""

$apiUrl = "http://localhost:5205/api/image-analysis-management/service-state"

# Get current settings
Write-Host "1. Getting current settings..." -ForegroundColor Yellow
try {
    $currentSettings = Invoke-RestMethod -Uri $apiUrl -Method GET -TimeoutSec 10 -ErrorAction Stop
    Write-Host "   Current AssignmentMode: $($currentSettings.assignmentMode)" -ForegroundColor Gray
    Write-Host "   Current AutoAssign: $($currentSettings.autoAssign)" -ForegroundColor Gray
    Write-Host ""
} catch {
    Write-Host "   ⚠️  Could not get current settings: $($_.Exception.Message)" -ForegroundColor Yellow
    Write-Host "   Proceeding with update anyway..." -ForegroundColor Gray
    Write-Host ""
}

# Update settings to enable Auto mode
Write-Host "2. Enabling Auto Assignment Mode..." -ForegroundColor Yellow
$updateBody = @{
    assignmentMode = "Auto"
    autoAssign = $true
    enabled = $true
} | ConvertTo-Json

try {
    $response = Invoke-RestMethod -Uri $apiUrl -Method POST -Body $updateBody -ContentType "application/json" -TimeoutSec 10 -ErrorAction Stop
    Write-Host "   ✅ Auto Assignment Mode Enabled!" -ForegroundColor Green
    Write-Host ""
    Write-Host "   Updated Settings:" -ForegroundColor Cyan
    Write-Host "     • AssignmentMode: Auto" -ForegroundColor White
    Write-Host "     • AutoAssign: True" -ForegroundColor White
    Write-Host "     • Enabled: True" -ForegroundColor White
    Write-Host ""
} catch {
    Write-Host "   ❌ Failed to update settings: $($_.Exception.Message)" -ForegroundColor Red
    if ($_.Exception.Response) {
        $reader = New-Object System.IO.StreamReader($_.Exception.Response.GetResponseStream())
        $responseBody = $reader.ReadToEnd()
        Write-Host "   Response: $responseBody" -ForegroundColor Red
    }
    exit 1
}

# Verify the update
Write-Host "3. Verifying settings..." -ForegroundColor Yellow
Start-Sleep -Seconds 2
try {
    $updatedSettings = Invoke-RestMethod -Uri $apiUrl -Method GET -TimeoutSec 10 -ErrorAction Stop
    if ($updatedSettings.assignmentMode -eq "Auto" -and $updatedSettings.autoAssign -eq $true) {
        Write-Host "   ✅ Settings verified successfully!" -ForegroundColor Green
    } else {
        Write-Host "   ⚠️  Settings may not have updated correctly" -ForegroundColor Yellow
        Write-Host "      AssignmentMode: $($updatedSettings.assignmentMode)" -ForegroundColor Gray
        Write-Host "      AutoAssign: $($updatedSettings.autoAssign)" -ForegroundColor Gray
    }
} catch {
    Write-Host "   ⚠️  Could not verify settings: $($_.Exception.Message)" -ForegroundColor Yellow
}

Write-Host ""
Write-Host "========================================" -ForegroundColor Cyan
Write-Host "Next Steps" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""
Write-Host "The assignment orchestrator will now:" -ForegroundColor Yellow
Write-Host "  • Automatically assign Ready groups to Analysts" -ForegroundColor White
Write-Host "  • Run every 5-30 seconds (adaptive polling)" -ForegroundColor White
Write-Host "  • Create assignments when Ready groups are available" -ForegroundColor White
Write-Host ""
Write-Host "Monitor assignments:" -ForegroundColor Yellow
Write-Host "  • Check /api/image-analysis/my-assignments endpoint" -ForegroundColor White
Write-Host "  • Watch logs for [ASSIGNMENT] messages" -ForegroundColor White
Write-Host ""
Write-Host "To check for Ready groups:" -ForegroundColor Yellow
Write-Host "  GET http://localhost:5205/api/image-analysis/groups/ready" -ForegroundColor White
Write-Host ""


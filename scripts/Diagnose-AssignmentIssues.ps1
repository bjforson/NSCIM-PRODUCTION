# =====================================================
# Assignment Diagnostic Script (PowerShell)
# Diagnoses why assignments are not being made
# =====================================================

param(
    [string]$Server = "localhost",
    [string]$Database = "NickScanCentralImagingPortal",
    [switch]$TrustServerCertificate
)

Write-Host "=========================================" -ForegroundColor Cyan
Write-Host "ASSIGNMENT DIAGNOSTIC SCRIPT" -ForegroundColor Cyan
Write-Host "=========================================" -ForegroundColor Cyan
Write-Host ""

# Get SQL script path
$scriptPath = Join-Path $PSScriptRoot "Diagnose-AssignmentIssues-Complete.sql"

if (-not (Test-Path $scriptPath)) {
    Write-Host "❌ ERROR: SQL script not found: $scriptPath" -ForegroundColor Red
    Write-Host "Please ensure the SQL script exists in the scripts folder" -ForegroundColor Yellow
    exit 1
}

Write-Host "📋 Running diagnostic SQL script..." -ForegroundColor Yellow
Write-Host "Server: $Server" -ForegroundColor Gray
Write-Host "Database: $Database" -ForegroundColor Gray
Write-Host ""

try {
    $params = @{
        ServerInstance = $Server
        Database = $Database
        InputFile = $scriptPath
        ErrorAction = "Stop"
    }
    
    if ($TrustServerCertificate) {
        $params.TrustServerCertificate = $true
    }
    
    $results = Invoke-Sqlcmd @params
    
    # Display results
    $results | Format-Table -AutoSize
    
    Write-Host ""
    Write-Host "✅ Diagnostic script completed successfully" -ForegroundColor Green
    Write-Host ""
    Write-Host "📋 NEXT STEPS:" -ForegroundColor Cyan
    Write-Host "1. Review the Status column for ❌ (red X) indicators" -ForegroundColor White
    Write-Host "2. Most common issue: AssignmentMode != 'Auto'" -ForegroundColor Yellow
    Write-Host "3. Fix: UPDATE AnalysisSettings SET AssignmentMode = 'Auto', UpdatedAtUtc = GETUTCDATE();" -ForegroundColor White
    Write-Host "4. Check service logs for [ASSIGNMENT] messages" -ForegroundColor White
    Write-Host "5. Note: SignalR is PRIMARY source for ready users (database is fallback)" -ForegroundColor White
    
} catch {
    Write-Host ""
    Write-Host "❌ ERROR: Failed to run diagnostic script" -ForegroundColor Red
    Write-Host "Error: $($_.Exception.Message)" -ForegroundColor Red
    Write-Host ""
    Write-Host "Troubleshooting:" -ForegroundColor Yellow
    Write-Host "- Ensure SQL Server is accessible" -ForegroundColor White
    Write-Host "- Check database name is correct" -ForegroundColor White
    Write-Host "- Try adding -TrustServerCertificate parameter" -ForegroundColor White
    Write-Host ""
    exit 1
}


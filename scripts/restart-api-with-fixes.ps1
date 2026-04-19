# Restart API to apply SQL syntax error fixes
# This script stops the API, rebuilds the Services and API projects, and restarts the API

Write-Host "========================================" -ForegroundColor Cyan
Write-Host "Restarting API with SQL Fixes" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""

# 1. Stop existing API process
Write-Host "1. Stopping existing API process..." -ForegroundColor Yellow
$apiProcesses = Get-Process | Where-Object { 
    $_.ProcessName -eq "NickScanCentralImagingPortal.API" -or
    ($_.ProcessName -eq "dotnet" -and $_.Path -like "*NickScanCentralImagingPortal.API*")
}

if ($apiProcesses) {
    foreach ($proc in $apiProcesses) {
        Write-Host "   Found API process (PID: $($proc.Id))" -ForegroundColor White
        Stop-Process -Id $proc.Id -Force -ErrorAction SilentlyContinue
    }
    Start-Sleep -Seconds 3
    Write-Host "   API process(es) stopped" -ForegroundColor Green
} else {
    Write-Host "   No API process found" -ForegroundColor Gray
}
Write-Host ""

# 2. Rebuild Services project (contains the fixes)
Write-Host "2. Rebuilding Services project..." -ForegroundColor Yellow
$servicesBuild = dotnet build "src\NickScanCentralImagingPortal.Services\NickScanCentralImagingPortal.Services.csproj" --no-incremental 2>&1
if ($LASTEXITCODE -eq 0) {
    Write-Host "   ✅ Services build succeeded" -ForegroundColor Green
} else {
    Write-Host "   ❌ Services build failed!" -ForegroundColor Red
    Write-Host $servicesBuild
    exit 1
}
Write-Host ""

# 3. Rebuild API project (to pick up new Services DLL)
Write-Host "3. Rebuilding API project..." -ForegroundColor Yellow
$apiBuild = dotnet build "src\NickScanCentralImagingPortal.API\NickScanCentralImagingPortal.API.csproj" --no-incremental 2>&1
if ($LASTEXITCODE -eq 0) {
    Write-Host "   ✅ API build succeeded" -ForegroundColor Green
} else {
    Write-Host "   ❌ API build failed!" -ForegroundColor Red
    Write-Host $apiBuild
    exit 1
}
Write-Host ""

# 4. Start API
Write-Host "4. Starting API service..." -ForegroundColor Yellow
$apiPath = Join-Path $PWD "src\NickScanCentralImagingPortal.API"
Start-Process powershell -ArgumentList "-NoExit", "-Command", "cd '$apiPath'; Write-Host '🚀 BACKEND API (with SQL fixes)' -ForegroundColor Cyan; dotnet run" -WindowStyle Normal

Write-Host "   API service starting in new window..." -ForegroundColor Green
Write-Host "   Waiting 15 seconds for API to initialize..." -ForegroundColor Yellow
Start-Sleep -Seconds 15

# 5. Verify API is running
Write-Host ""
Write-Host "5. Verifying API is running..." -ForegroundColor Yellow
$newApiProcess = Get-Process | Where-Object { 
    $_.ProcessName -eq "NickScanCentralImagingPortal.API" -or
    ($_.ProcessName -eq "dotnet" -and $_.Path -like "*NickScanCentralImagingPortal.API*")
} | Select-Object -First 1

if ($newApiProcess) {
    Write-Host "   ✅ API is running (PID: $($newApiProcess.Id))" -ForegroundColor Green
    Write-Host "   Started at: $($newApiProcess.StartTime)" -ForegroundColor Gray
} else {
    Write-Host "   ⚠️  API process not found - check the new window for errors" -ForegroundColor Yellow
}

# 6. Test API health endpoint
Write-Host ""
Write-Host "6. Testing API health endpoint..." -ForegroundColor Yellow
Start-Sleep -Seconds 5
try {
    $healthResponse = Invoke-WebRequest -Uri "http://localhost:5205/health" -TimeoutSec 10 -ErrorAction Stop
    if ($healthResponse.StatusCode -eq 200) {
        Write-Host "   ✅ API health check passed" -ForegroundColor Green
    } else {
        Write-Host "   ⚠️  API returned status: $($healthResponse.StatusCode)" -ForegroundColor Yellow
    }
} catch {
    Write-Host "   ⚠️  API health check failed (may still be starting): $($_.Exception.Message)" -ForegroundColor Yellow
}

Write-Host ""
Write-Host "========================================" -ForegroundColor Cyan
Write-Host "✅ Restart Complete!" -ForegroundColor Green
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""
Write-Host "The API has been restarted with the SQL syntax error fixes:" -ForegroundColor White
Write-Host "  • AssignmentWorker.AutoAssignByRoleAsync - Fixed" -ForegroundColor Gray
Write-Host "  • ImageAnalysisController.GetMyAssignments - Fixed" -ForegroundColor Gray
Write-Host ""
Write-Host "The following errors should now be resolved:" -ForegroundColor White
Write-Host "  • 'Incorrect syntax near the keyword WITH'" -ForegroundColor Gray
Write-Host "  • 500 errors on /api/image-analysis/my-assignments?role=Audit" -ForegroundColor Gray
Write-Host ""
Write-Host "API URL: http://localhost:5205" -ForegroundColor Cyan
Write-Host ""


# Restart API with SQL CTE Fix
# This script stops the API, rebuilds the Services project, and restarts the API

Write-Host ""
Write-Host "========================================" -ForegroundColor Cyan
Write-Host "🔄 Restarting API with SQL CTE Fix" -ForegroundColor Yellow
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""

# Step 1: Stop the API
Write-Host "Step 1: Stopping API..." -ForegroundColor White
$apiProcess = Get-Process -Name "NickScanCentralImagingPortal.API" -ErrorAction SilentlyContinue
if ($apiProcess) {
    Write-Host "  Found API process (PID: $($apiProcess.Id))" -ForegroundColor Gray
    Stop-Process -Id $apiProcess.Id -Force -ErrorAction SilentlyContinue
    Start-Sleep -Seconds 2
    Write-Host "  ✅ API stopped" -ForegroundColor Green
} else {
    Write-Host "  ℹ️  API process not found (may already be stopped)" -ForegroundColor Gray
}

# Step 2: Rebuild Services project
Write-Host ""
Write-Host "Step 2: Rebuilding Services project..." -ForegroundColor White
$servicesProject = "src\NickScanCentralImagingPortal.Services\NickScanCentralImagingPortal.Services.csproj"
$buildResult = dotnet build $servicesProject --no-incremental 2>&1

if ($LASTEXITCODE -eq 0) {
    Write-Host "  ✅ Services project rebuilt successfully" -ForegroundColor Green
} else {
    Write-Host "  ❌ Build failed!" -ForegroundColor Red
    Write-Host $buildResult
    exit 1
}

# Step 3: Rebuild API project
Write-Host ""
Write-Host "Step 3: Rebuilding API project..." -ForegroundColor White
$apiProject = "src\NickScanCentralImagingPortal.API\NickScanCentralImagingPortal.API.csproj"
$buildResult = dotnet build $apiProject --no-incremental 2>&1

if ($LASTEXITCODE -eq 0) {
    Write-Host "  ✅ API project rebuilt successfully" -ForegroundColor Green
} else {
    Write-Host "  ❌ Build failed!" -ForegroundColor Red
    Write-Host $buildResult
    exit 1
}

# Step 4: Start the API
Write-Host ""
Write-Host "Step 4: Starting API..." -ForegroundColor White
$apiExe = "src\NickScanCentralImagingPortal.API\bin\Debug\net8.0\NickScanCentralImagingPortal.API.exe"

if (Test-Path $apiExe) {
    Start-Process -FilePath $apiExe -WorkingDirectory (Split-Path $apiExe) -WindowStyle Minimized
    Start-Sleep -Seconds 3
    Write-Host "  ✅ API started" -ForegroundColor Green
} else {
    Write-Host "  ❌ API executable not found at: $apiExe" -ForegroundColor Red
    exit 1
}

Write-Host ""
Write-Host "========================================" -ForegroundColor Cyan
Write-Host "✅ Restart Complete!" -ForegroundColor Green
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""
Write-Host "The API has been restarted with the SQL CTE fixes:" -ForegroundColor White
Write-Host "  • AssignmentWorker.AutoAssignByRoleAsync - Batched Contains calls" -ForegroundColor Gray
Write-Host "  • HousekeepingWorker.SynchronizeStatusWithWorkflowStageAsync - Batched Contains calls - 2 locations" -ForegroundColor Gray
Write-Host ""
Write-Host "The following errors should now be resolved:" -ForegroundColor White
Write-Host "  • 'Incorrect syntax near the keyword WITH'" -ForegroundColor Gray
Write-Host "  • AssignmentWorker crashes every 5 minutes" -ForegroundColor Gray
Write-Host ""
Write-Host "📡 API URL: http://localhost:5205" -ForegroundColor Cyan
Write-Host ""
Write-Host "⏳ Monitor logs for 30 minutes to verify no more CTE errors" -ForegroundColor Yellow
Write-Host ""


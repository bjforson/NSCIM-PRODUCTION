# Quick Verification Commands for Background Services Optimization
# Run these commands to quickly verify orchestrators are working

Write-Host "🔍 Quick Verification Commands" -ForegroundColor Cyan
Write-Host ""

# Check if API is running
$apiProcess = Get-Process -Name "NickScanCentralImagingPortal.API" -ErrorAction SilentlyContinue
if ($apiProcess) {
    Write-Host "✅ API Process Running (PID: $($apiProcess.Id))" -ForegroundColor Green
} else {
    Write-Host "❌ API Process NOT Running" -ForegroundColor Red
    Write-Host "   Please start the API first" -ForegroundColor Yellow
    exit 1
}

Write-Host ""
Write-Host "📋 Quick API Endpoint Tests:" -ForegroundColor Cyan
Write-Host ""

# Test endpoints (adjust base URL if needed)
$baseUrl = "https://localhost:5205"  # Adjust if your API runs on different port

Write-Host "1. Testing /api/image-analysis-management/service-state..." -ForegroundColor Yellow
try {
    $response = Invoke-WebRequest -Uri "$baseUrl/api/image-analysis-management/service-state" -UseBasicParsing -SkipCertificateCheck -ErrorAction Stop
    if ($response.StatusCode -eq 200) {
        Write-Host "   ✅ Returns 200 OK" -ForegroundColor Green
    } else {
        Write-Host "   ⚠️ Returns $($response.StatusCode)" -ForegroundColor Yellow
    }
} catch {
    Write-Host "   ❌ Error: $($_.Exception.Message)" -ForegroundColor Red
}

Write-Host ""
Write-Host "2. Testing /api/image-analysis-management/assignments..." -ForegroundColor Yellow
try {
    $response = Invoke-WebRequest -Uri "$baseUrl/api/image-analysis-management/assignments" -UseBasicParsing -SkipCertificateCheck -ErrorAction Stop
    if ($response.StatusCode -eq 200) {
        Write-Host "   ✅ Returns 200 OK (500 error fixed!)" -ForegroundColor Green
        $data = $response.Content | ConvertFrom-Json
        Write-Host "   📊 Assignments count: $($data.Count)" -ForegroundColor Gray
    } else {
        Write-Host "   ⚠️ Returns $($response.StatusCode)" -ForegroundColor Yellow
    }
} catch {
    Write-Host "   ❌ Error: $($_.Exception.Message)" -ForegroundColor Red
    if ($_.Exception.Response.StatusCode -eq 500) {
        Write-Host "   ⚠️ Still getting 500 error - may need API restart" -ForegroundColor Yellow
    }
}

Write-Host ""
Write-Host "3. Testing /api/image-analysis-management/stats..." -ForegroundColor Yellow
try {
    $response = Invoke-WebRequest -Uri "$baseUrl/api/image-analysis-management/stats" -UseBasicParsing -SkipCertificateCheck -ErrorAction Stop
    if ($response.StatusCode -eq 200) {
        Write-Host "   ✅ Returns 200 OK" -ForegroundColor Green
        $data = $response.Content | ConvertFrom-Json
        Write-Host "   📊 Stats: ImageAnalysis=$($data.imageAnalysis), Audit=$($data.audit), Completed=$($data.completed)" -ForegroundColor Gray
    } else {
        Write-Host "   ⚠️ Returns $($response.StatusCode)" -ForegroundColor Yellow
    }
} catch {
    Write-Host "   ❌ Error: $($_.Exception.Message)" -ForegroundColor Red
}

Write-Host ""
Write-Host "📝 Next Steps:" -ForegroundColor Cyan
Write-Host "1. Check API console/logs for orchestrator startup messages" -ForegroundColor White
Write-Host "2. Look for '[IMAGE-ANALYSIS-ORCHESTRATOR]', '[ICUMS-PIPELINE-ORCHESTRATOR]', '[CONTAINER-COMPLETENESS-ORCHESTRATOR]'" -ForegroundColor White
Write-Host "3. Look for '[ADAPTIVE-POLLING]' messages" -ForegroundColor White
Write-Host "4. Look for '[HEALTH-MONITOR]' messages" -ForegroundColor White
Write-Host ""

Write-Host "✅ Quick verification complete" -ForegroundColor Green


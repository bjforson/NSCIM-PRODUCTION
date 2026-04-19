# Verification Script for Background Services Optimization
# Checks if orchestrators are running and logs are appearing

Write-Host "🔍 Verifying Orchestrator Services Status..." -ForegroundColor Cyan
Write-Host ""

# Check if API process is running
$apiProcess = Get-Process -Name "NickScanCentralImagingPortal.API" -ErrorAction SilentlyContinue
if ($apiProcess) {
    Write-Host "✅ API Process Running (PID: $($apiProcess.Id))" -ForegroundColor Green
} else {
    Write-Host "❌ API Process NOT Running" -ForegroundColor Red
    Write-Host "   Please start the API first" -ForegroundColor Yellow
    exit 1
}

Write-Host ""
Write-Host "📋 Expected Orchestrator Log Messages:" -ForegroundColor Cyan
Write-Host ""

Write-Host "1. ImageAnalysisOrchestratorService:" -ForegroundColor Yellow
Write-Host "   Look for: '[IMAGE-ANALYSIS-ORCHESTRATOR] Starting Image Analysis Orchestrator Service'" -ForegroundColor Gray
Write-Host "   Look for: '[BOOTSTRAPPER] Image Analysis system initialized successfully'" -ForegroundColor Gray
Write-Host "   Look for: '[ADAPTIVE-POLLING] Work count: X, Level: Y, Interval: Zs'" -ForegroundColor Gray
Write-Host ""

Write-Host "2. IcumPipelineOrchestratorService:" -ForegroundColor Yellow
Write-Host "   Look for: '[ICUMS-PIPELINE-ORCHESTRATOR] ICUMS Pipeline Orchestrator Service started'" -ForegroundColor Gray
Write-Host "   Look for: '[FILE-SCANNER]', '[DOWNLOAD-QUEUE]', '[JSON-INGESTION]', '[DATA-TRANSFER]'" -ForegroundColor Gray
Write-Host ""

Write-Host "3. ContainerCompletenessOrchestratorService:" -ForegroundColor Yellow
Write-Host "   Look for: '[CONTAINER-COMPLETENESS-ORCHESTRATOR] Container Completeness Orchestrator Service started'" -ForegroundColor Gray
Write-Host "   Look for: '[COMPLETENESS-CHECK]', '[DATA-MAPPING]', '[BOE-SELECTIVITY]'" -ForegroundColor Gray
Write-Host ""

Write-Host "4. Health Monitor:" -ForegroundColor Yellow
Write-Host "   Look for: '[HEALTH-MONITOR]' messages with execution times" -ForegroundColor Gray
Write-Host ""

Write-Host "📝 Next Steps:" -ForegroundColor Cyan
Write-Host "1. Check API logs for orchestrator startup messages" -ForegroundColor White
Write-Host "2. Verify no errors during startup" -ForegroundColor White
Write-Host "3. Monitor for adaptive polling messages" -ForegroundColor White
Write-Host "4. Check health monitor metrics are being collected" -ForegroundColor White
Write-Host ""

Write-Host "🔗 To check API logs:" -ForegroundColor Cyan
Write-Host "   - If running in console: Check the console output" -ForegroundColor White
Write-Host "   - If running as service: Check Windows Event Logs or log files" -ForegroundColor White
Write-Host ""

Write-Host "✅ Verification script complete" -ForegroundColor Green


# Check Assignment Workflow Logs - Simple Version
# Searches for [ASSIGNMENT] messages in service logs

$LogPath = "src\NickScanCentralImagingPortal.API\logs"

Write-Host "Assignment Workflow Log Checker" -ForegroundColor Cyan
Write-Host ""

if (-not (Test-Path $LogPath)) {
    Write-Host "ERROR: Log directory not found: $LogPath" -ForegroundColor Red
    exit 1
}

# Find most recent log file
$logFile = Get-ChildItem $LogPath -Filter "nickscan-*.txt" | Sort-Object LastWriteTime -Descending | Select-Object -First 1

if (-not $logFile) {
    Write-Host "ERROR: No log files found" -ForegroundColor Red
    exit 1
}

Write-Host "Found log file: $($logFile.Name)" -ForegroundColor Green
Write-Host "Last modified: $($logFile.LastWriteTime)" -ForegroundColor Gray
Write-Host ""

# Check for assignment-related messages
Write-Host "=== ASSIGNMENT WORKFLOW MESSAGES ===" -ForegroundColor Yellow
Write-Host ""

$assignmentMessages = Get-Content $logFile.FullName | Select-String -Pattern "\[ASSIGNMENT" | Select-Object -Last 50

if ($assignmentMessages) {
    Write-Host "Found $($assignmentMessages.Count) [ASSIGNMENT] messages" -ForegroundColor Green
    Write-Host ""
    $assignmentMessages | ForEach-Object { Write-Host $_.Line }
} else {
    Write-Host "NO [ASSIGNMENT] messages found" -ForegroundColor Red
    Write-Host "Assignment workflow is NOT executing" -ForegroundColor Yellow
}

Write-Host ""
Write-Host "=== ASSIGNMENT-POLLING MESSAGES ===" -ForegroundColor Yellow
Write-Host ""

$pollingMessages = Get-Content $logFile.FullName | Select-String -Pattern "\[ASSIGNMENT-POLLING" | Select-Object -Last 20

if ($pollingMessages) {
    Write-Host "Found $($pollingMessages.Count) [ASSIGNMENT-POLLING] messages" -ForegroundColor Green
    Write-Host ""
    $pollingMessages | ForEach-Object { Write-Host $_.Line }
} else {
    Write-Host "NO [ASSIGNMENT-POLLING] messages found" -ForegroundColor Red
}

Write-Host ""
Write-Host "=== ORCHESTRATOR MESSAGES ===" -ForegroundColor Yellow
Write-Host ""

$orchestratorMessages = Get-Content $logFile.FullName | Select-String -Pattern "\[ORCHESTRATOR\]|\[IMAGE-ANALYSIS-ORCHESTRATOR\]" | Select-Object -Last 20

if ($orchestratorMessages) {
    Write-Host "Found orchestrator messages" -ForegroundColor Green
    Write-Host ""
    $orchestratorMessages | ForEach-Object { Write-Host $_.Line }
} else {
    Write-Host "NO orchestrator messages found" -ForegroundColor Red
    Write-Host "Service may not be running" -ForegroundColor Yellow
}

Write-Host ""
Write-Host "=== ERRORS ===" -ForegroundColor Yellow
Write-Host ""

$errors = Get-Content $logFile.FullName | Select-String -Pattern "\[ASSIGNMENT.*ERROR|\[ASSIGNMENT.*Error|\[ASSIGNMENT.*Exception" | Select-Object -Last 10

if ($errors) {
    Write-Host "Found errors in assignment workflow:" -ForegroundColor Red
    Write-Host ""
    $errors | ForEach-Object { Write-Host $_.Line -ForegroundColor Red }
} else {
    Write-Host "No errors found" -ForegroundColor Green
}

Write-Host ""
Write-Host "=== SUMMARY ===" -ForegroundColor Cyan
Write-Host ""

if ($assignmentMessages) {
    Write-Host "Assignment workflow is executing" -ForegroundColor Green
} else {
    Write-Host "Assignment workflow is NOT executing" -ForegroundColor Red
    Write-Host "Check if service is running" -ForegroundColor Yellow
}

if ($pollingMessages) {
    $lastPolling = $pollingMessages[-1].Line
    if ($lastPolling -match "Should execute: True") {
        Write-Host "Workflow should execute (Should execute: True)" -ForegroundColor Green
    } elseif ($lastPolling -match "Should execute: False") {
        Write-Host "Workflow skipped (Should execute: False)" -ForegroundColor Yellow
        Write-Host "This is normal if workflow ran recently" -ForegroundColor Gray
    }
}

Write-Host ""
Write-Host "Log file location: $($logFile.FullName)" -ForegroundColor Gray


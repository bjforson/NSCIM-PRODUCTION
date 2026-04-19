# Check recent assignment logs

$LogPath = "src\NickScanCentralImagingPortal.API\logs"
$logFile = Get-ChildItem $LogPath -Filter "nickscan-20260112*.txt" | Sort-Object LastWriteTime -Descending | Select-Object -First 1

if ($logFile) {
    Write-Host "Checking log file: $($logFile.Name)" -ForegroundColor Cyan
    Write-Host "Last modified: $($logFile.LastWriteTime)" -ForegroundColor Gray
    Write-Host ""
    
    Write-Host "=== RECENT ASSIGNMENT LOGS (last 50 lines) ===" -ForegroundColor Yellow
    Write-Host ""
    
    $assignmentLogs = Get-Content $logFile.FullName | Select-String -Pattern "\[ASSIGNMENT\]|\[ASSIGNMENT-POLLING\]" | Select-Object -Last 50
    
    if ($assignmentLogs) {
        $assignmentLogs | ForEach-Object { Write-Host $_ }
    } else {
        Write-Host "No assignment logs found" -ForegroundColor Red
    }
    
    Write-Host ""
    Write-Host "=== RECENT ERRORS ===" -ForegroundColor Yellow
    Write-Host ""
    
    $errors = Get-Content $logFile.FullName | Select-String -Pattern "ERR.*ASSIGNMENT" | Select-Object -Last 10
    
    if ($errors) {
        $errors | ForEach-Object { Write-Host $_ -ForegroundColor Red }
    } else {
        Write-Host "No assignment errors found" -ForegroundColor Green
    }
} else {
    Write-Host "Log file not found" -ForegroundColor Red
}


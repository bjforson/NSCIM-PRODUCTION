# Check Assignment Errors in Error Log File
# Searches for assignment-related errors with full exception details

$LogPath = "src\NickScanCentralImagingPortal.API\logs"

Write-Host "Assignment Error Checker" -ForegroundColor Cyan
Write-Host ""

if (-not (Test-Path $LogPath)) {
    Write-Host "ERROR: Log directory not found: $LogPath" -ForegroundColor Red
    exit 1
}

# Find most recent error log file
$errorLogFile = Get-ChildItem $LogPath -Filter "nickscan-errors-*.txt" | Sort-Object LastWriteTime -Descending | Select-Object -First 1

if ($errorLogFile) {
    Write-Host "Found error log file: $($errorLogFile.Name)" -ForegroundColor Green
    Write-Host "Last modified: $($errorLogFile.LastWriteTime)" -ForegroundColor Gray
    Write-Host ""
    
    Write-Host "=== ASSIGNMENT ERRORS (with exception details) ===" -ForegroundColor Yellow
    Write-Host ""
    
    $errors = Get-Content $errorLogFile.FullName | Select-String -Pattern "ASSIGNMENT" -Context 5,20 | Select-Object -Last 10
    
    if ($errors) {
        Write-Host "Found assignment errors:" -ForegroundColor Red
        Write-Host ""
        $errors | ForEach-Object { 
            Write-Host "---" -ForegroundColor Gray
            $_.Context.PreContext | ForEach-Object { Write-Host $_ -ForegroundColor Gray }
            Write-Host $_.Line -ForegroundColor Red
            $_.Context.PostContext | ForEach-Object { Write-Host $_ -ForegroundColor Gray }
        }
    } else {
        Write-Host "No assignment errors in error log file" -ForegroundColor Green
    }
} else {
    Write-Host "No error log file found" -ForegroundColor Yellow
    Write-Host "Checking main log file for errors..." -ForegroundColor Yellow
    Write-Host ""
    
    $logFile = Get-ChildItem $LogPath -Filter "nickscan-*.txt" | Sort-Object LastWriteTime -Descending | Select-Object -First 1
    
    if ($logFile) {
        Write-Host "Checking main log file: $($logFile.Name)" -ForegroundColor Cyan
        Write-Host ""
        
        # Look for errors around the time of the assignment error
        $errors = Get-Content $logFile.FullName | Select-String -Pattern "20:45:52.*ASSIGNMENT.*ERR" -Context 0,30
        
        if ($errors) {
            Write-Host "Found errors around 20:45:52:" -ForegroundColor Red
            Write-Host ""
            $errors | ForEach-Object { Write-Host $_.Line -ForegroundColor Red }
            $errors | ForEach-Object { 
                Write-Host ""
                Write-Host "Context after error:" -ForegroundColor Cyan
                $_.Context.PostContext | ForEach-Object { Write-Host $_ }
            }
        }
    }
}

Write-Host ""
Write-Host "Done" -ForegroundColor Green


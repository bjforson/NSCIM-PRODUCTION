# Find the specific assignment error details from main log file

$LogPath = "src\NickScanCentralImagingPortal.API\logs"
$logFile = Get-ChildItem $LogPath -Filter "nickscan-20260112*.txt" | Sort-Object LastWriteTime -Descending | Select-Object -First 1

if ($logFile) {
    Write-Host "Searching for assignment error at 20:45:52..." -ForegroundColor Cyan
    Write-Host "Log file: $($logFile.Name)" -ForegroundColor Gray
    Write-Host ""
    
    # Search for errors around 20:45:52 - look for ERR level messages
    $errors = Get-Content $logFile.FullName | Select-String -Pattern "20:45:5[0-9].*ERR.*ASSIGNMENT" -Context 0,30
    
    if ($errors) {
        Write-Host "Found ERR messages at 20:45:52:" -ForegroundColor Yellow
        Write-Host ""
        foreach ($error in $errors) {
            Write-Host "--- ERROR FOUND ---" -ForegroundColor Red
            Write-Host $error.Line -ForegroundColor Red
            Write-Host ""
            Write-Host "Context after error (next 30 lines):" -ForegroundColor Cyan
            $error.Context.PostContext | ForEach-Object { Write-Host $_ }
            Write-Host ""
        }
    } else {
        Write-Host "No ERR messages found at 20:45:52" -ForegroundColor Yellow
        Write-Host "Checking for any messages at 20:45:52..." -ForegroundColor Gray
        
        # Try searching for just the time with ASSIGNMENT
        $messages = Get-Content $logFile.FullName | Select-String -Pattern "20:45:52.*ASSIGNMENT" -Context 0,20
        
        if ($messages) {
            Write-Host "Found ASSIGNMENT messages at 20:45:52:" -ForegroundColor Yellow
            Write-Host ""
            $messages | ForEach-Object {
                Write-Host "---" -ForegroundColor Gray
                Write-Host $_.Line
                if ($_.Line -match "ERR") {
                    Write-Host "^^^ THIS IS THE ERROR ^^^" -ForegroundColor Red
                    Write-Host ""
                    Write-Host "Context after error:" -ForegroundColor Cyan
                    $_.Context.PostContext | ForEach-Object { Write-Host $_ }
                }
            }
        }
    }
} else {
    Write-Host "Log file not found" -ForegroundColor Red
}


# Find the specific assignment error from 20:45:52

$LogPath = "src\NickScanCentralImagingPortal.API\logs"
$errorLogFile = Get-ChildItem $LogPath -Filter "nickscan-errors-20260112.txt" | Select-Object -First 1

if ($errorLogFile) {
    Write-Host "Searching for assignment error at 20:45:52..." -ForegroundColor Cyan
    Write-Host ""
    
    # Search for errors around 20:45:52
    $errors = Get-Content $errorLogFile.FullName | Select-String -Pattern "20:45:5[0-9].*ASSIGNMENT" -Context 0,50
    
    if ($errors) {
        Write-Host "Found errors around 20:45:52:" -ForegroundColor Yellow
        Write-Host ""
        foreach ($error in $errors) {
            Write-Host "--- MATCH ---" -ForegroundColor Green
            Write-Host $error.Line -ForegroundColor Red
            Write-Host ""
            Write-Host "Context after error:" -ForegroundColor Cyan
            $error.Context.PostContext | ForEach-Object { Write-Host $_ }
            Write-Host ""
        }
    } else {
        Write-Host "No errors found at 20:45:52 with ASSIGNMENT pattern" -ForegroundColor Yellow
        Write-Host "Trying broader search..." -ForegroundColor Gray
        
        # Try searching for just the time
        $errors = Get-Content $errorLogFile.FullName | Select-String -Pattern "20:45:52" -Context 5,50
        
        if ($errors) {
            Write-Host "Found entries at 20:45:52:" -ForegroundColor Yellow
            Write-Host ""
            $errors | ForEach-Object {
                Write-Host "---" -ForegroundColor Gray
                $_.Context.PreContext | ForEach-Object { Write-Host $_ -ForegroundColor Gray }
                Write-Host $_.Line -ForegroundColor Cyan
                $_.Context.PostContext | ForEach-Object { Write-Host $_ }
                Write-Host ""
            }
        }
    }
} else {
    Write-Host "Error log file not found" -ForegroundColor Red
}


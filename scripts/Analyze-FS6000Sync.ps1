# Analyze FS6000 Scanner Folder Sync Status
# This script provides a comprehensive analysis of the FS6000 file sync service

Write-Host "========================================" -ForegroundColor Cyan
Write-Host "FS6000 Scanner Folder Sync Analysis" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""

# 1. Check Database Sync Status
Write-Host "1. Database Sync Status" -ForegroundColor Yellow
Write-Host "   Querying FS6000SyncLogs table..." -ForegroundColor Gray

$statusQuery = @"
SELECT 
    SyncStatus,
    COUNT(*) as Count
FROM FS6000SyncLogs
GROUP BY SyncStatus
ORDER BY Count DESC
"@

try {
    $statusResults = sqlcmd -S localhost -d NS_CIS -Q $statusQuery -W -h -1 2>&1 | Where-Object { $_ -match "^\s*\w+\s+\d+" }
    
    if ($statusResults) {
        foreach ($line in $statusResults) {
            if ($line -match "(\w+)\s+(\d+)") {
                $status = $matches[1]
                $count = $matches[2]
                $color = switch ($status) {
                    "Completed" { "Green" }
                    "Failed" { "Red" }
                    "Processing" { "Yellow" }
                    "Pending" { "Cyan" }
                    default { "White" }
                }
                Write-Host "   $status : $count" -ForegroundColor $color
            }
        }
    } else {
        Write-Host "   No sync logs found" -ForegroundColor Yellow
    }
} catch {
    Write-Host "   Error querying database: $_" -ForegroundColor Red
}

Write-Host ""

# 2. Recent Sync Activity
Write-Host "2. Recent Sync Activity (Last 10)" -ForegroundColor Yellow
$recentQuery = @"
SELECT TOP 10
    DestinationPath,
    SyncStatus,
    CompletedAt,
    CASE WHEN ErrorMessage IS NULL THEN 'None' ELSE LEFT(ErrorMessage, 50) END as Error
FROM FS6000SyncLogs
ORDER BY CompletedAt DESC
"@

try {
    $recentResults = sqlcmd -S localhost -d NS_CIS -Q $recentQuery -W -h -1 2>&1 | Where-Object { $_ -match "^\s*\w:" -or $_ -match "^\s*C:\\" }
    
    if ($recentResults) {
        $count = 0
        foreach ($line in $recentResults) {
            if ($count -lt 10) {
                Write-Host "   $line" -ForegroundColor Gray
                $count++
            }
        }
    }
} catch {
    Write-Host "   Error querying recent activity: $_" -ForegroundColor Red
}

Write-Host ""

# 3. Failed Syncs Analysis
Write-Host "3. Failed Syncs Analysis" -ForegroundColor Yellow
$failedQuery = @"
SELECT TOP 5
    DestinationPath,
    ErrorMessage,
    RetryCount,
    CompletedAt
FROM FS6000SyncLogs
WHERE SyncStatus = 'Failed'
ORDER BY CompletedAt DESC
"@

try {
    $failedResults = sqlcmd -S localhost -d NS_CIS -Q $failedQuery -W -h -1 2>&1 | Where-Object { $_ -match "^\s*\w:" -or $_ -match "^\s*C:\\" -or $_ -match "^\s*\d+" }
    
    if ($failedResults -and $failedResults.Count -gt 0) {
        Write-Host "   Found failed syncs:" -ForegroundColor Red
        foreach ($line in $failedResults) {
            Write-Host "   $line" -ForegroundColor Red
        }
    } else {
        Write-Host "   No failed syncs found" -ForegroundColor Green
    }
} catch {
    Write-Host "   Error querying failed syncs: $_" -ForegroundColor Red
}

Write-Host ""

# 4. Check Configuration
Write-Host "4. Configuration Check" -ForegroundColor Yellow
$configPath = "src\NickScanCentralImagingPortal.API\appsettings.json"
if (Test-Path $configPath) {
    $config = Get-Content $configPath | ConvertFrom-Json
    
    if ($config.FS6000 -and $config.FS6000.FileSync) {
        $fsConfig = $config.FS6000.FileSync
        Write-Host "   Source Path: $($fsConfig.SourcePath)" -ForegroundColor Gray
        Write-Host "   Destination Path: $($fsConfig.DestinationPath)" -ForegroundColor Gray
        Write-Host "   Sync Interval: $($fsConfig.SyncIntervalMinutes) minutes" -ForegroundColor Gray
        Write-Host "   Real-time Sync: $($fsConfig.EnableRealTimeSync)" -ForegroundColor Gray
        
        # Check if paths exist
        if ($fsConfig.SourcePath -and (Test-Path $fsConfig.SourcePath)) {
            Write-Host "   Source directory: EXISTS" -ForegroundColor Green
        } else {
            Write-Host "   Source directory: NOT FOUND" -ForegroundColor Red
        }
        
        if ($fsConfig.DestinationPath -and (Test-Path $fsConfig.DestinationPath)) {
            Write-Host "   Destination directory: EXISTS" -ForegroundColor Green
            $fileCount = (Get-ChildItem $fsConfig.DestinationPath -Recurse -File -ErrorAction SilentlyContinue | Measure-Object).Count
            Write-Host "   Files in destination: $fileCount" -ForegroundColor Gray
        } else {
            Write-Host "   Destination directory: NOT FOUND" -ForegroundColor Red
        }
    } else {
        Write-Host "   FS6000.FileSync configuration not found" -ForegroundColor Yellow
    }
} else {
    Write-Host "   Configuration file not found: $configPath" -ForegroundColor Red
}

Write-Host ""

# 5. Check Service Status
Write-Host "5. Service Status" -ForegroundColor Yellow
$apiProcess = Get-Process | Where-Object { 
    $_.ProcessName -eq "NickScanCentralImagingPortal.API" -or
    ($_.ProcessName -eq "dotnet" -and $_.Path -like "*NickScanCentralImagingPortal.API*")
} | Select-Object -First 1

if ($apiProcess) {
    Write-Host "   API Process: RUNNING (PID: $($apiProcess.Id))" -ForegroundColor Green
    Write-Host "   Started: $($apiProcess.StartTime)" -ForegroundColor Gray
} else {
    Write-Host "   API Process: NOT RUNNING" -ForegroundColor Red
}

Write-Host ""

# 6. Summary
Write-Host "6. Summary" -ForegroundColor Yellow
Write-Host "   Last sync completed: $(sqlcmd -S localhost -d NS_CIS -Q "SELECT TOP 1 FORMAT(CompletedAt, 'yyyy-MM-dd HH:mm:ss') FROM FS6000SyncLogs WHERE SyncStatus = 'Completed' ORDER BY CompletedAt DESC" -W -h -1 2>&1 | Where-Object { $_ -match '\d{4}-\d{2}-\d{2}' } | Select-Object -First 1)" -ForegroundColor Gray
Write-Host "   Total completed syncs: $(sqlcmd -S localhost -d NS_CIS -Q "SELECT COUNT(*) FROM FS6000SyncLogs WHERE SyncStatus = 'Completed'" -W -h -1 2>&1 | Where-Object { $_ -match '^\s*\d+' } | ForEach-Object { ($_ -replace '\s+', '').Trim() })" -ForegroundColor Gray
Write-Host "   Currently processing: $(sqlcmd -S localhost -d NS_CIS -Q "SELECT COUNT(*) FROM FS6000SyncLogs WHERE SyncStatus = 'Processing'" -W -h -1 2>&1 | Where-Object { $_ -match '^\s*\d+' } | ForEach-Object { ($_ -replace '\s+', '').Trim() })" -ForegroundColor Gray
Write-Host "   Failed syncs: $(sqlcmd -S localhost -d NS_CIS -Q "SELECT COUNT(*) FROM FS6000SyncLogs WHERE SyncStatus = 'Failed'" -W -h -1 2>&1 | Where-Object { $_ -match '^\s*\d+' } | ForEach-Object { ($_ -replace '\s+', '').Trim() })" -ForegroundColor Gray

Write-Host ""
Write-Host "========================================" -ForegroundColor Cyan
Write-Host "Analysis Complete" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""


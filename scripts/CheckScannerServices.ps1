# Scanner Services Diagnostic Script
# Checks the status of FS6000 and ASE scanner services

Write-Host "========================================" -ForegroundColor Cyan
Write-Host "Scanner Services Diagnostic" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""

# Check API Health
Write-Host "1. Checking API Health..." -ForegroundColor Yellow
try {
    $healthResponse = Invoke-WebRequest -Uri "http://10.0.1.254:5205/health" -UseBasicParsing -ErrorAction Stop
    $healthData = $healthResponse.Content | ConvertFrom-Json
    Write-Host "   ✅ API is healthy" -ForegroundColor Green
    Write-Host "   Status: $($healthData.status)" -ForegroundColor Gray
} catch {
    Write-Host "   ❌ API health check failed: $($_.Exception.Message)" -ForegroundColor Red
}

Write-Host ""

# Check FS6000 Scanner Infrastructure
Write-Host "2. Checking FS6000 Scanner Infrastructure..." -ForegroundColor Yellow

# Check network share
$zDrive = Test-Path "Z:\"
if ($zDrive) {
    Write-Host "   ✅ Network share Z:\ is accessible" -ForegroundColor Green
    try {
        $zDriveInfo = Get-Item "Z:\" -ErrorAction Stop
        Write-Host "   Last Write: $($zDriveInfo.LastWriteTime)" -ForegroundColor Gray
    } catch {
        Write-Host "   ⚠️  Cannot read Z:\ details: $($_.Exception.Message)" -ForegroundColor Yellow
    }
} else {
    Write-Host "   ❌ Network share Z:\ is NOT accessible" -ForegroundColor Red
}

# Check staging directory
$stagingPath = "C:\NickScan\FS6000\Staging"
$stagingExists = Test-Path $stagingPath
if ($stagingExists) {
    Write-Host "   ✅ Staging directory exists: $stagingPath" -ForegroundColor Green
    try {
        $stagingFiles = Get-ChildItem $stagingPath -ErrorAction Stop | Measure-Object
        Write-Host "   Files in staging: $($stagingFiles.Count)" -ForegroundColor Gray
    } catch {
        Write-Host "   ⚠️  Cannot read staging directory: $($_.Exception.Message)" -ForegroundColor Yellow
    }
} else {
    Write-Host "   ⚠️  Staging directory does not exist: $stagingPath" -ForegroundColor Yellow
}

Write-Host ""

# Check ASE Scanner Infrastructure
Write-Host "3. Checking ASE Scanner Infrastructure..." -ForegroundColor Yellow

# Check database connectivity
$aseServer = "10.0.0.3"
$asePort = 1433
$aseConnection = Test-NetConnection -ComputerName $aseServer -Port $asePort -InformationLevel Quiet -WarningAction SilentlyContinue
if ($aseConnection) {
    Write-Host "   ✅ ASE Database server is reachable: $aseServer`:$asePort" -ForegroundColor Green
} else {
    Write-Host "   ❌ ASE Database server is NOT reachable: $aseServer`:$asePort" -ForegroundColor Red
}

Write-Host ""

# Check Background Services Configuration
Write-Host "4. Checking Background Services Configuration..." -ForegroundColor Yellow

$appSettingsPath = "src\NickScanCentralImagingPortal.API\appsettings.json"
if (Test-Path $appSettingsPath) {
    $appSettings = Get-Content $appSettingsPath | ConvertFrom-Json
    
    # Check FS6000 service
    $fs6000Enabled = $appSettings.BackgroundServices.FS6000BackgroundService.Enabled
    if ($fs6000Enabled) {
        Write-Host "   ✅ FS6000BackgroundService is ENABLED" -ForegroundColor Green
    } else {
        Write-Host "   ❌ FS6000BackgroundService is DISABLED" -ForegroundColor Red
    }
    
    # Check ASE service
    $aseEnabled = $appSettings.BackgroundServices.AseDatabaseSyncService.Enabled
    if ($aseEnabled) {
        Write-Host "   ✅ AseDatabaseSyncService is ENABLED" -ForegroundColor Green
    } else {
        Write-Host "   ❌ AseDatabaseSyncService is DISABLED" -ForegroundColor Red
    }
} else {
    Write-Host "   ⚠️  Cannot find appsettings.json at: $appSettingsPath" -ForegroundColor Yellow
}

Write-Host ""

# Check Recent Logs for Scanner Activity
Write-Host "5. Checking Recent Scanner Activity in Logs..." -ForegroundColor Yellow

$logPath = "src\NickScanCentralImagingPortal.API\logs"
if (Test-Path $logPath) {
    $latestLog = Get-ChildItem $logPath -Filter "*.txt" | Sort-Object LastWriteTime -Descending | Select-Object -First 1
    if ($latestLog) {
        Write-Host "   Latest log: $($latestLog.Name)" -ForegroundColor Gray
        
        # Search for FS6000 activity
        $fs6000Lines = Select-String -Path $latestLog.FullName -Pattern "FS6000|FileSync|Ingestion" -Context 0,0 | Select-Object -Last 5
        if ($fs6000Lines) {
            Write-Host "   Recent FS6000 activity found:" -ForegroundColor Green
            foreach ($line in $fs6000Lines) {
                Write-Host "     - $($line.Line.Trim())" -ForegroundColor Gray
            }
        } else {
            Write-Host "   ⚠️  No recent FS6000 activity in logs" -ForegroundColor Yellow
        }
        
        # Search for ASE activity
        $aseLines = Select-String -Path $latestLog.FullName -Pattern "ASE|AseDatabaseSync" -Context 0,0 | Select-Object -Last 5
        if ($aseLines) {
            Write-Host "   Recent ASE activity found:" -ForegroundColor Green
            foreach ($line in $aseLines) {
                Write-Host "     - $($line.Line.Trim())" -ForegroundColor Gray
            }
        } else {
            Write-Host "   ⚠️  No recent ASE activity in logs" -ForegroundColor Yellow
        }
    } else {
        Write-Host "   ⚠️  No log files found" -ForegroundColor Yellow
    }
} else {
    Write-Host "   ⚠️  Log directory not found: $logPath" -ForegroundColor Yellow
}

Write-Host ""

# Summary
Write-Host "========================================" -ForegroundColor Cyan
Write-Host "Summary" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""
Write-Host "If scanner services are offline, check:" -ForegroundColor Yellow
Write-Host "  1. Ensure background services are enabled in appsettings.json" -ForegroundColor White
Write-Host "  2. Verify scanner infrastructure is accessible:" -ForegroundColor White
Write-Host "     - FS6000: Z:\ network share" -ForegroundColor White
Write-Host "     - ASE: 10.0.0.3:1433 database server" -ForegroundColor White
Write-Host "  3. Check API logs for errors:" -ForegroundColor White
Write-Host "     - src\NickScanCentralImagingPortal.API\logs\" -ForegroundColor White
Write-Host "  4. Restart the API service if needed" -ForegroundColor White
Write-Host ""

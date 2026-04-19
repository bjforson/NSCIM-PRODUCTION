# ASE Sync Operational Status Check
# This script checks if ASE sync is operational

Write-Host "`n=== ASE SYNC OPERATIONAL STATUS CHECK ===" -ForegroundColor Cyan
Write-Host ""

# Check if API is running
$apiProcess = Get-Process -Name "NickScanCentralImagingPortal.API" -ErrorAction SilentlyContinue
if ($apiProcess) {
    Write-Host "✅ API Process Running (PID: $($apiProcess.Id))" -ForegroundColor Green
} else {
    Write-Host "❌ API Process NOT Running" -ForegroundColor Red
    Write-Host "   ASE sync requires the API to be running" -ForegroundColor Yellow
    exit 1
}

# Check environment variable
$asePassword = [Environment]::GetEnvironmentVariable("NICKSCAN_ASE_PASSWORD", "Machine")
if ([string]::IsNullOrEmpty($asePassword)) {
    $asePassword = [Environment]::GetEnvironmentVariable("NICKSCAN_ASE_PASSWORD", "User")
}

if ([string]::IsNullOrEmpty($asePassword)) {
    Write-Host "⚠️  NICKSCAN_ASE_PASSWORD environment variable NOT SET" -ForegroundColor Yellow
    Write-Host "   ASE sync will fail if connection string has password placeholder" -ForegroundColor Yellow
} else {
    Write-Host "✅ NICKSCAN_ASE_PASSWORD environment variable is set (length: $($asePassword.Length))" -ForegroundColor Green
}

# Check configuration
$appsettingsPath = "src\NickScanCentralImagingPortal.API\appsettings.json"
$aseConfig = $null
$connString = $null

if (Test-Path $appsettingsPath) {
    $config = Get-Content $appsettingsPath | ConvertFrom-Json
    $aseConfig = $config.ASE
    
    Write-Host "`n📋 Configuration:" -ForegroundColor Cyan
    Write-Host "   EnableRealTimeSync: $($aseConfig.EnableRealTimeSync)" -ForegroundColor $(if ($aseConfig.EnableRealTimeSync) { "Green" } else { "Yellow" })
    Write-Host "   SyncIntervalMinutes: $($aseConfig.SyncIntervalMinutes)" -ForegroundColor White
    Write-Host "   BatchSize: $($aseConfig.BatchSize)" -ForegroundColor White
    Write-Host "   StartDate: $($aseConfig.StartDate)" -ForegroundColor White
    
    $connString = $aseConfig.ConnectionString
    if ($connString -match "***USE_ENV_VAR") {
        Write-Host "   ConnectionString: Has password placeholder (needs NICKSCAN_ASE_PASSWORD)" -ForegroundColor Yellow
    } else {
        Write-Host "   ConnectionString: Configured" -ForegroundColor Green
        # Extract server info
        if ($connString -match 'Server=([^;]+)') {
            $server = $matches[1]
            Write-Host "   Server: $server" -ForegroundColor White
        }
        if ($connString -match 'Database=([^;]+)') {
            $database = $matches[1]
            Write-Host "   Database: $database" -ForegroundColor White
        }
    }
} else {
    Write-Host "❌ appsettings.json not found at: $appsettingsPath" -ForegroundColor Red
}

# Check recent logs for ASE activity
Write-Host "`n📊 Recent Log Activity:" -ForegroundColor Cyan
$logFiles = Get-ChildItem "src\NickScanCentralImagingPortal.API\logs\*.txt" -ErrorAction SilentlyContinue | Sort-Object LastWriteTime -Descending | Select-Object -First 1

if ($logFiles) {
    $recentLogs = Get-Content $logFiles.FullName -Tail 200 | Select-String -Pattern "ASE|AseBackground|AseDatabaseSync" -Context 0,0
    
    if ($recentLogs) {
        Write-Host "   Found ASE-related log entries:" -ForegroundColor Green
        $recentLogs | Select-Object -Last 5 | ForEach-Object {
            $line = $_.Line
            if ($line -match "starting|Starting|started") {
                Write-Host "   ✅ $line" -ForegroundColor Green
            } elseif ($line -match "disabled|Disabled|error|Error|failed|Failed") {
                Write-Host "   ❌ $line" -ForegroundColor Red
            } else {
                Write-Host "   ℹ️  $line" -ForegroundColor Gray
            }
        }
    } else {
        Write-Host "   ⚠️  No recent ASE-related log entries found" -ForegroundColor Yellow
        Write-Host "   This may indicate the service is not starting or is failing silently" -ForegroundColor Yellow
    }
} else {
    Write-Host "   ⚠️  No log files found" -ForegroundColor Yellow
}

# Check if service is registered
Write-Host "`n🔍 Service Registration Check:" -ForegroundColor Cyan
$serviceConfigPath = "src\NickScanCentralImagingPortal.Services\ServiceConfiguration.cs"
if (Test-Path $serviceConfigPath) {
    $serviceConfig = Get-Content $serviceConfigPath -Raw
    if ($serviceConfig -match "AddHostedService<AseBackgroundService>") {
        Write-Host "   ✅ AseBackgroundService is registered" -ForegroundColor Green
    } else {
        Write-Host "   ❌ AseBackgroundService is NOT registered" -ForegroundColor Red
    }
    
    if ($serviceConfig -match "AddScoped<IAseDatabaseSyncService") {
        Write-Host "   ✅ IAseDatabaseSyncService is registered" -ForegroundColor Green
    } else {
        Write-Host "   ❌ IAseDatabaseSyncService is NOT registered" -ForegroundColor Red
    }
} else {
    Write-Host "   ⚠️  ServiceConfiguration.cs not found" -ForegroundColor Yellow
}

# Summary
Write-Host "`n=== SUMMARY ===" -ForegroundColor Cyan
$issues = @()

if (-not $apiProcess) {
    $issues += "API is not running"
}

if ($connString -and [string]::IsNullOrEmpty($asePassword) -and $connString -match "***USE_ENV_VAR") {
    $issues += "NICKSCAN_ASE_PASSWORD environment variable is not set but connection string requires it"
}

if ($aseConfig -and $aseConfig.EnableRealTimeSync -eq $false) {
    $issues += "EnableRealTimeSync is set to false"
}

if ($issues.Count -eq 0) {
    Write-Host "✅ ASE Sync appears to be configured correctly" -ForegroundColor Green
    Write-Host "   Check API logs for actual sync activity" -ForegroundColor Yellow
    Write-Host "   Use API endpoint: GET /api/AseSync/statistics to check sync status" -ForegroundColor Yellow
} else {
    Write-Host "⚠️  Potential Issues Found:" -ForegroundColor Yellow
    $issues | ForEach-Object {
        Write-Host "   - $_" -ForegroundColor Red
    }
}

Write-Host ""


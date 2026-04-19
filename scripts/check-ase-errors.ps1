# ASE Database Error Checker Script
# Checks API logs and ASE sync status for errors

param(
    [Parameter(Mandatory=$false)]
    [switch]$CheckLogs,
    
    [Parameter(Mandatory=$false)]
    [switch]$CheckStatus,
    
    [Parameter(Mandatory=$false)]
    [switch]$CheckConfig
)

$ErrorActionPreference = "Continue"

Write-Host "========================================" -ForegroundColor Cyan
Write-Host "ASE Database Error Checker" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""

# Check Configuration
if ($CheckConfig -or (-not $CheckLogs -and -not $CheckStatus)) {
    Write-Host "=== Configuration Check ===" -ForegroundColor Yellow
    Write-Host ""
    
    $asePassword = [System.Environment]::GetEnvironmentVariable("NICKSCAN_ASE_PASSWORD", "Machine")
    $hasPassword = -not [string]::IsNullOrEmpty($asePassword)
    
    Write-Host "Environment Variables:" -ForegroundColor Cyan
    if ($hasPassword) {
        Write-Host "  NICKSCAN_ASE_PASSWORD: SET" -ForegroundColor Green
    } else {
        Write-Host "  NICKSCAN_ASE_PASSWORD: NOT SET" -ForegroundColor Red
    }
    if ($hasPassword) {
        Write-Host "    Length: $($asePassword.Length) characters" -ForegroundColor Gray
    } else {
        Write-Host "    ⚠️  Set this variable to enable ASE sync" -ForegroundColor Yellow
    }
    Write-Host ""
    
    # Check appsettings.json
    $appsettingsPath = "src\NickScanCentralImagingPortal.API\appsettings.json"
    if (Test-Path $appsettingsPath) {
        $config = Get-Content $appsettingsPath | ConvertFrom-Json
        $aseConfig = $config.ASE
        
        Write-Host "appsettings.json Configuration:" -ForegroundColor Cyan
        if ($aseConfig.EnableRealTimeSync) {
            Write-Host "  EnableRealTimeSync: $($aseConfig.EnableRealTimeSync)" -ForegroundColor Green
        } else {
            Write-Host "  EnableRealTimeSync: $($aseConfig.EnableRealTimeSync)" -ForegroundColor Yellow
        }
        Write-Host "  SyncIntervalMinutes: $($aseConfig.SyncIntervalMinutes)" -ForegroundColor Gray
        Write-Host "  BatchSize: $($aseConfig.BatchSize)" -ForegroundColor Gray
        Write-Host "  ConnectionString: $($aseConfig.ConnectionString -replace 'Password=[^;]*', 'Password=***')" -ForegroundColor Gray
        
        $hasPlaceholder = $aseConfig.ConnectionString -like "*USE_ENV_VAR*"
        if ($hasPlaceholder -and -not $hasPassword) {
            Write-Host "  ⚠️  Connection string has password placeholder but env var not set!" -ForegroundColor Red
        }
    }
    Write-Host ""
}

# Check API Status
if ($CheckStatus -or (-not $CheckLogs -and -not $CheckConfig)) {
    Write-Host "=== API Status Check ===" -ForegroundColor Yellow
    Write-Host ""
    
    $apiUrl = "http://localhost:5205/api/Ase/sync-status"
    
    try {
        $response = Invoke-RestMethod -Uri $apiUrl -Method Get -TimeoutSec 5 -ErrorAction Stop
        
        Write-Host "✅ API is accessible" -ForegroundColor Green
        Write-Host ""
        Write-Host "Sync Status:" -ForegroundColor Cyan
        Write-Host "  LastSyncTime: $($response.LastSyncTime)" -ForegroundColor Gray
        Write-Host "  LastSyncedInspectionId: $($response.LastSyncedInspectionId)" -ForegroundColor Gray
        Write-Host "  TotalScans: $($response.TotalScans)" -ForegroundColor Gray
        Write-Host "  TodayScans: $($response.TodayScans)" -ForegroundColor Gray
        Write-Host "  SyncStatus: $($response.SyncStatus)" -ForegroundColor Gray
        Write-Host ""
        
        if ($response.Configuration) {
            Write-Host "Configuration Status:" -ForegroundColor Cyan
            if ($response.Configuration.EnableRealTimeSync) {
                Write-Host "  EnableRealTimeSync: $($response.Configuration.EnableRealTimeSync)" -ForegroundColor Green
            } else {
                Write-Host "  EnableRealTimeSync: $($response.Configuration.EnableRealTimeSync)" -ForegroundColor Yellow
            }
            if ($response.Configuration.HasPasswordEnvironmentVariable) {
                Write-Host "  HasPasswordEnvironmentVariable: $($response.Configuration.HasPasswordEnvironmentVariable)" -ForegroundColor Green
            } else {
                Write-Host "  HasPasswordEnvironmentVariable: $($response.Configuration.HasPasswordEnvironmentVariable)" -ForegroundColor Red
            }
            if ($response.Configuration.HasPasswordPlaceholder) {
                Write-Host "  HasPasswordPlaceholder: $($response.Configuration.HasPasswordPlaceholder)" -ForegroundColor Red
            } else {
                Write-Host "  HasPasswordPlaceholder: $($response.Configuration.HasPasswordPlaceholder)" -ForegroundColor Green
            }
            if ($response.Configuration.ConnectionStringConfigured) {
                Write-Host "  ConnectionStringConfigured: $($response.Configuration.ConnectionStringConfigured)" -ForegroundColor Green
            } else {
                Write-Host "  ConnectionStringConfigured: $($response.Configuration.ConnectionStringConfigured)" -ForegroundColor Red
            }
            if ($response.Configuration.CanSync) {
                Write-Host "  CanSync: $($response.Configuration.CanSync)" -ForegroundColor Green
            } else {
                Write-Host "  CanSync: $($response.Configuration.CanSync)" -ForegroundColor Red
            }
            
            if (-not $response.Configuration.CanSync) {
                Write-Host ""
                Write-Host "❌ ASE sync cannot proceed. Issues:" -ForegroundColor Red
                if (-not $response.Configuration.HasPasswordEnvironmentVariable) {
                    Write-Host "  - NICKSCAN_ASE_PASSWORD environment variable is not set" -ForegroundColor Red
                }
                if ($response.Configuration.HasPasswordPlaceholder) {
                    Write-Host "  - Connection string still contains password placeholder" -ForegroundColor Red
                }
                if (-not $response.Configuration.EnableRealTimeSync) {
                    Write-Host "  - EnableRealTimeSync is disabled" -ForegroundColor Yellow
                }
            }
        }
    }
    catch {
        Write-Host "❌ Cannot connect to API: $($_.Exception.Message)" -ForegroundColor Red
        Write-Host "   Make sure the API is running on http://localhost:5205" -ForegroundColor Yellow
    }
    Write-Host ""
}

# Check Logs
if ($CheckLogs -or (-not $CheckStatus -and -not $CheckConfig)) {
    Write-Host "=== Log File Check ===" -ForegroundColor Yellow
    Write-Host ""
    
    $logDirs = @(
        "src\NickScanCentralImagingPortal.API\logs",
        "src\NickScanCentralImagingPortal.API\bin\Debug\net8.0\logs",
        "logs"
    )
    
    $today = Get-Date -Format "yyyy-MM-dd"
    $foundLogs = $false
    
    foreach ($logDir in $logDirs) {
        if (Test-Path $logDir) {
            Write-Host "Checking: $logDir" -ForegroundColor Cyan
            
            $errorLog = Join-Path $logDir "errors-api-$today.log"
            $allLog = Join-Path $logDir "nick-scan-api-$today.log"
            $structuredLog = Join-Path $logDir "structured-api-$today.log"
            
            $logsToCheck = @($errorLog, $allLog, $structuredLog)
            $allLogFiles = Get-ChildItem $logDir -Filter "*.log" -ErrorAction SilentlyContinue | Sort-Object LastWriteTime -Descending | Select-Object -First 5
            
            if ($allLogFiles) {
                $foundLogs = $true
                Write-Host "  Found $($allLogFiles.Count) log file(s)" -ForegroundColor Green
                
                foreach ($logFile in $allLogFiles) {
                    Write-Host ""
                    Write-Host "  Checking: $($logFile.Name)" -ForegroundColor Gray
                    Write-Host "    Last Write: $($logFile.LastWriteTime)" -ForegroundColor Gray
                    
                    $aseErrors = Get-Content $logFile.FullName -ErrorAction SilentlyContinue | 
                        Select-String -Pattern "ASE|AseDatabaseSync|AseBackground|database.*error|connection.*error|SqlException" -CaseSensitive:$false
                    
                    if ($aseErrors) {
                        Write-Host "    ⚠️  Found $($aseErrors.Count) ASE-related error(s)" -ForegroundColor Red
                        $aseErrors | Select-Object -Last 10 | ForEach-Object {
                            Write-Host "      $($_.Line)" -ForegroundColor Yellow
                        }
                    } else {
                        Write-Host "    ✅ No ASE errors found" -ForegroundColor Green
                    }
                }
            } else {
                Write-Host "  No log files found" -ForegroundColor Yellow
            }
        }
    }
    
    if (-not $foundLogs) {
        Write-Host ""
        Write-Host "⚠️  No log files found in expected locations" -ForegroundColor Yellow
        Write-Host "   Logs may not have been generated yet, or API is not running" -ForegroundColor Gray
    }
}

Write-Host ""
Write-Host "========================================" -ForegroundColor Cyan
Write-Host "Check Complete" -ForegroundColor Green
Write-Host "========================================" -ForegroundColor Cyan


# ICUMS Orchestrator Monitoring Script
# Monitors logs for ICUMS pipeline orchestrator activity, especially batch downloads

param(
    [int]$DurationMinutes = 10,
    [int]$RefreshIntervalSeconds = 5
)

$logDir = "C:\Users\Administrator\Documents\GitHub\NICKSCAN-CENTRAL--IMAGE-PORTAL\logs"
$startTime = Get-Date
$endTime = $startTime.AddMinutes($DurationMinutes)
$lastPosition = 0
$activityCount = 0
$apiCallCount = 0
$recordCount = 0
$errorCount = 0

Write-Host "========================================" -ForegroundColor Cyan
Write-Host "ICUMS ORCHESTRATOR MONITOR" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""
Write-Host "Monitoring Duration: $DurationMinutes minutes" -ForegroundColor Yellow
Write-Host "Refresh Interval: $RefreshIntervalSeconds seconds" -ForegroundColor Yellow
Write-Host "Start Time: $($startTime.ToString('yyyy-MM-dd HH:mm:ss'))" -ForegroundColor Yellow
Write-Host ""

# Find latest log file
if (-not (Test-Path $logDir)) {
    Write-Host "❌ Log directory not found: $logDir" -ForegroundColor Red
    exit 1
}

$logFiles = Get-ChildItem -Path $logDir -Filter "nickscan-*.txt" | Sort-Object LastWriteTime -Descending
if (-not $logFiles) {
    Write-Host "❌ No log files found in $logDir" -ForegroundColor Red
    exit 1
}

$latestLog = $logFiles[0].FullName
Write-Host "📄 Monitoring log file: $($logFiles[0].Name)" -ForegroundColor Green
Write-Host "   Last modified: $($logFiles[0].LastWriteTime)" -ForegroundColor Gray
Write-Host ""

# Get initial file size
if (Test-Path $latestLog) {
    $lastPosition = (Get-Item $latestLog).Length
} else {
    Write-Host "❌ Log file not found: $latestLog" -ForegroundColor Red
    exit 1
}

Write-Host "🔍 Starting monitoring... (Press Ctrl+C to stop early)" -ForegroundColor Yellow
Write-Host ""

$iteration = 0
while ((Get-Date) -lt $endTime) {
    $iteration++
    $currentTime = Get-Date
    
    # Check if log file has been updated
    if (Test-Path $latestLog) {
        $currentSize = (Get-Item $latestLog).Length
        
        if ($currentSize -gt $lastPosition) {
            # Read new content
            $stream = [System.IO.File]::OpenRead($latestLog)
            $stream.Position = $lastPosition
            $reader = New-Object System.IO.StreamReader($stream)
            $newContent = $reader.ReadToEnd()
            $reader.Close()
            $stream.Close()
            
            if ($newContent) {
                $lines = $newContent -split "`n"
                
                foreach ($line in $lines) {
                    if ([string]::IsNullOrWhiteSpace($line)) { continue }
                    
                    # Check for orchestrator startup
                    if ($line -match "ICUMS-PIPELINE-ORCHESTRATOR.*started|Pipeline Orchestrator Service started") {
                        Write-Host "[$($currentTime.ToString('HH:mm:ss'))] 🟢 ORCHESTRATOR STARTED" -ForegroundColor Green
                        $activityCount++
                    }
                    # Check for background service activity
                    elseif ($line -match "\[BACKGROUND-SERVICE\]") {
                        if ($line -match "Triggering batch download|starting batch download|Service enabled") {
                            Write-Host "[$($currentTime.ToString('HH:mm:ss'))] ⏰ BATCH DOWNLOAD TRIGGERED" -ForegroundColor Cyan
                            $activityCount++
                        }
                        elseif ($line -match "Skipping batch download") {
                            Write-Host "[$($currentTime.ToString('HH:mm:ss'))] ⏸️  Batch download skipped (waiting for interval)" -ForegroundColor DarkYellow
                        }
                        elseif ($line -match "Service disabled") {
                            Write-Host "[$($currentTime.ToString('HH:mm:ss'))] ⚠️  Batch download service DISABLED" -ForegroundColor Yellow
                            $errorCount++
                        }
                        elseif ($line -match "Fetching ICUMS batch data") {
                            Write-Host "[$($currentTime.ToString('HH:mm:ss'))] 📥 FETCHING BATCH DATA" -ForegroundColor Cyan
                            $activityCount++
                        }
                        elseif ($line -match "Saved and registered batch file|Successfully.*records") {
                            Write-Host "[$($currentTime.ToString('HH:mm:ss'))] ✅ BATCH DATA SAVED" -ForegroundColor Green
                            $activityCount++
                            if ($line -match "(\d+)\s+records") {
                                $recordCount += [int]$matches[1]
                            }
                        }
                        elseif ($line -match "Error|Failed|Exception") {
                            Write-Host "[$($currentTime.ToString('HH:mm:ss'))] ❌ ERROR: $($line.Trim())" -ForegroundColor Red
                            $errorCount++
                        }
                    }
                    # Check for API calls
                    elseif ($line -match "Making ICUMS API call|ICUMS API response") {
                        if ($line -match "Making ICUMS API call") {
                            $apiCallCount++
                            Write-Host "[$($currentTime.ToString('HH:mm:ss'))] 📡 API CALL" -ForegroundColor Magenta
                        }
                        elseif ($line -match "ICUMS API response.*(\d{3})") {
                            $statusCode = $matches[1]
                            if ($statusCode -eq "200") {
                                Write-Host "[$($currentTime.ToString('HH:mm:ss'))] ✅ API Response: 200 OK" -ForegroundColor Green
                            }
                            elseif ($statusCode -eq "401") {
                                Write-Host "[$($currentTime.ToString('HH:mm:ss'))] ❌ API Response: 401 UNAUTHORIZED" -ForegroundColor Red
                                $errorCount++
                            }
                            else {
                                Write-Host "[$($currentTime.ToString('HH:mm:ss'))] ⚠️  API Response: $statusCode" -ForegroundColor Yellow
                            }
                        }
                    }
                    # Check for successful fetches
                    elseif ($line -match "Successfully fetched.*records|Fetched.*records from ICUMS") {
                        if ($line -match "(\d+)\s+records") {
                            $count = [int]$matches[1]
                            $recordCount += $count
                            Write-Host "[$($currentTime.ToString('HH:mm:ss'))] ✅ Fetched $count records from ICUMS" -ForegroundColor Green
                            $activityCount++
                        }
                    }
                    # Check for empty responses (no new data)
                    elseif ($line -match "ICUMS API returned empty response|No new records|0 records") {
                        Write-Host "[$($currentTime.ToString('HH:mm:ss'))] ℹ️  No new data available" -ForegroundColor DarkGray
                    }
                    # Check for download queue activity
                    elseif ($line -match "\[DOWNLOAD-QUEUE\]|\[ICUMS-DOWNLOAD-QUEUE\]") {
                        if ($line -match "Processing|Downloading|Completed") {
                            Write-Host "[$($currentTime.ToString('HH:mm:ss'))] 📥 DOWNLOAD QUEUE: $($line.Trim())" -ForegroundColor Cyan
                            $activityCount++
                        }
                    }
                    # Check for file scanner activity
                    elseif ($line -match "\[FILE-SCANNER\]") {
                        if ($line -match "Found.*files|Registered.*file") {
                            Write-Host "[$($currentTime.ToString('HH:mm:ss'))] 📁 FILE SCANNER: $($line.Trim())" -ForegroundColor Cyan
                            $activityCount++
                        }
                    }
                    # Check for JSON ingestion activity
                    elseif ($line -match "\[JSON-INGESTION\]") {
                        if ($line -match "Processing|Completed|Found.*pending") {
                            Write-Host "[$($currentTime.ToString('HH:mm:ss'))] 📄 JSON INGESTION: $($line.Trim())" -ForegroundColor Cyan
                            $activityCount++
                        }
                    }
                    # Check for data transfer activity
                    elseif ($line -match "\[DATA-TRANSFER\]") {
                        if ($line -match "Transferred|Completed transfer") {
                            Write-Host "[$($currentTime.ToString('HH:mm:ss'))] 🔄 DATA TRANSFER: $($line.Trim())" -ForegroundColor Cyan
                            $activityCount++
                        }
                    }
                }
            }
            
            $lastPosition = $currentSize
        }
        
        # Check if a new log file was created
        $newLogFiles = Get-ChildItem -Path $logDir -Filter "nickscan-*.txt" | Sort-Object LastWriteTime -Descending
        if ($newLogFiles -and $newLogFiles[0].FullName -ne $latestLog) {
            Write-Host "[$($currentTime.ToString('HH:mm:ss'))] 📄 New log file detected: $($newLogFiles[0].Name)" -ForegroundColor Yellow
            $latestLog = $newLogFiles[0].FullName
            $lastPosition = 0
        }
    }
    
    # Show status every 30 seconds
    if ($iteration % 6 -eq 0) {
        $elapsed = (Get-Date) - $startTime
        $remaining = $endTime - (Get-Date)
        Write-Host "[$($currentTime.ToString('HH:mm:ss'))] ⏱️  Elapsed: $([int]$elapsed.TotalMinutes)m $([int]$elapsed.Seconds)s | Remaining: $([int]$remaining.TotalMinutes)m | Activities: $activityCount | API Calls: $apiCallCount | Records: $recordCount | Errors: $errorCount" -ForegroundColor DarkGray
    }
    
    Start-Sleep -Seconds $RefreshIntervalSeconds
}

Write-Host ""
Write-Host "========================================" -ForegroundColor Cyan
Write-Host "MONITORING SUMMARY" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""
Write-Host "Duration: $DurationMinutes minutes" -ForegroundColor Yellow
Write-Host "Total Activities Detected: $activityCount" -ForegroundColor $(if ($activityCount -gt 0) { "Green" } else { "Yellow" })
Write-Host "API Calls: $apiCallCount" -ForegroundColor $(if ($apiCallCount -gt 0) { "Green" } else { "Yellow" })
Write-Host "Records Fetched: $recordCount" -ForegroundColor $(if ($recordCount -gt 0) { "Green" } else { "Yellow" })
Write-Host "Errors: $errorCount" -ForegroundColor $(if ($errorCount -eq 0) { "Green" } else { "Red" })
Write-Host ""

if ($activityCount -eq 0) {
    Write-Host "⚠️  NO ACTIVITY DETECTED" -ForegroundColor Yellow
    Write-Host "   Possible reasons:" -ForegroundColor White
    Write-Host "   • Orchestrator service not running" -ForegroundColor White
    Write-Host "   • Service hasn't reached 30-minute interval yet" -ForegroundColor White
    Write-Host "   • Batch download service is disabled" -ForegroundColor White
    Write-Host "   • No new data available from ICUMS API" -ForegroundColor White
} elseif ($apiCallCount -eq 0) {
    Write-Host "⚠️  NO API CALLS DETECTED" -ForegroundColor Yellow
    Write-Host "   Orchestrator may be waiting for the 30-minute interval" -ForegroundColor White
} elseif ($recordCount -eq 0) {
    Write-Host "ℹ️  API CALLS MADE BUT NO RECORDS RETURNED" -ForegroundColor Yellow
    Write-Host "   This is normal if no new data is available" -ForegroundColor White
} else {
    Write-Host "✅ ACTIVITY DETECTED - System appears to be working" -ForegroundColor Green
}

Write-Host ""


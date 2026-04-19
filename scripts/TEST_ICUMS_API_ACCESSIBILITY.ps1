# ICUMS API Accessibility Test Script
# Tests if the external ICUMS API is accessible and returning data

$apiUrl = "http://localhost:5205"
$testStartDate = (Get-Date).AddHours(-2).ToString("dd-MM-yyyy HH:mm:ss.ff")
$testEndDate = (Get-Date).ToString("dd-MM-yyyy HH:mm:ss.ff")

Write-Host "========================================" -ForegroundColor Cyan
Write-Host "ICUMS API ACCESSIBILITY TEST" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""
Write-Host "Test Date Range:" -ForegroundColor Yellow
Write-Host "   Start: $testStartDate" -ForegroundColor White
Write-Host "   End:   $testEndDate" -ForegroundColor White
Write-Host ""

# Test 1: Check API Status Endpoint
Write-Host "1. Testing ICUMS API Status Endpoint..." -ForegroundColor Yellow
try {
    $statusResponse = Invoke-RestMethod -Uri "$apiUrl/api/icum/status" -Method Get -ErrorAction Stop
    Write-Host "   ✅ API Status Endpoint: ACCESSIBLE" -ForegroundColor Green
    Write-Host "   Response: $($statusResponse | ConvertTo-Json -Depth 2)" -ForegroundColor Gray
} catch {
    Write-Host "   ❌ API Status Endpoint: FAILED" -ForegroundColor Red
    Write-Host "   Error: $($_.Exception.Message)" -ForegroundColor Red
    if ($_.Exception.Response) {
        $statusCode = $_.Exception.Response.StatusCode
        Write-Host "   Status Code: $statusCode" -ForegroundColor Red
    }
}
Write-Host ""

# Test 2: Check if we can call the batch endpoint through our API
Write-Host "2. Testing Batch Data Fetch (via our API)..." -ForegroundColor Yellow
Write-Host "   Note: This requires authentication and may not be directly accessible" -ForegroundColor DarkYellow
Write-Host ""

# Test 3: Check external ICUMS API directly (if credentials are available)
Write-Host "3. Testing External ICUMS API Directly..." -ForegroundColor Yellow
Write-Host "   Base URL: https://esb.unipassghana.com:26004" -ForegroundColor White
Write-Host "   Batch Endpoint: /api/rm/scan/boe?startDate={0}&endDate={1}" -ForegroundColor White
Write-Host ""

# Read configuration to get auth details
$appsettingsPath = "src\NickScanCentralImagingPortal.API\appsettings.json"
if (Test-Path $appsettingsPath) {
    $config = Get-Content $appsettingsPath | ConvertFrom-Json
    $baseUrl = $config.ICUMS.BaseUrl
    $fetchBatchUrl = $config.ICUMS.FetchBatchUrl
    $fetchBatchKey = $config.ICUMS.FetchBatchKey
    $authKey = $config.ICUMS.AuthKey
    
    Write-Host "   Configuration loaded:" -ForegroundColor Cyan
    Write-Host "      Base URL: $baseUrl" -ForegroundColor White
    Write-Host "      Fetch Batch Key: $fetchBatchKey" -ForegroundColor White
    Write-Host "      Auth Key: $($authKey.Substring(0, [Math]::Min(20, $authKey.Length)))..." -ForegroundColor White
    Write-Host ""
    
    # Get auth key from environment variable
    $authKey = [Environment]::GetEnvironmentVariable("NICKSCAN_ICUMS_AUTH_KEY", "Machine")
    if (-not $authKey) {
        $authKey = [Environment]::GetEnvironmentVariable("NICKSCAN_ICUMS_AUTH_KEY", "User")
    }
    
    if ($authKey -and $authKey.Length -gt 10) {
        Write-Host "   Attempting direct API call..." -ForegroundColor Yellow
        try {
            $formattedUrl = $fetchBatchUrl -f $testStartDate, $testEndDate
            Write-Host "   URL: $formattedUrl" -ForegroundColor Gray
            
            $headers = @{
                "ESB_IF_ID" = $fetchBatchKey
                "ESB_AUTH_KEY" = $authKey
                "Accept" = "application/json"
            }
            
            $response = Invoke-WebRequest -Uri $formattedUrl -Method Get -Headers $headers -TimeoutSec 60 -ErrorAction Stop
            Write-Host "   ✅ External API: ACCESSIBLE" -ForegroundColor Green
            Write-Host "   Status Code: $($response.StatusCode)" -ForegroundColor Cyan
            
            if ($response.Content) {
                $contentLength = $response.Content.Length
                Write-Host "   Response Length: $contentLength bytes" -ForegroundColor Cyan
                
                try {
                    $jsonData = $response.Content | ConvertFrom-Json
                    if ($jsonData.BoeScanDocuments) {
                        $recordCount = $jsonData.BoeScanDocuments.Count
                        Write-Host "   ✅ Records Found: $recordCount" -ForegroundColor Green
                        if ($recordCount -gt 0) {
                            Write-Host "   ✅ NEW DATA AVAILABLE" -ForegroundColor Green
                            Write-Host "   Sample Container: $($jsonData.BoeScanDocuments[0].ContainerDetails.ContainerNumber)" -ForegroundColor Gray
                        } else {
                            Write-Host "   ⚠️ No new records in this time range" -ForegroundColor Yellow
                        }
                    } else {
                        Write-Host "   ⚠️ Response structure unexpected" -ForegroundColor Yellow
                        Write-Host "   Response: $($response.Content.Substring(0, [Math]::Min(200, $response.Content.Length)))..." -ForegroundColor Gray
                    }
                } catch {
                    Write-Host "   ⚠️ Could not parse JSON response" -ForegroundColor Yellow
                    Write-Host "   Response: $($response.Content.Substring(0, [Math]::Min(200, $response.Content.Length)))..." -ForegroundColor Gray
                }
            }
        } catch {
            Write-Host "   ❌ External API: FAILED" -ForegroundColor Red
            Write-Host "   Error: $($_.Exception.Message)" -ForegroundColor Red
            if ($_.Exception.Response) {
                $statusCode = $_.Exception.Response.StatusCode
                Write-Host "   Status Code: $statusCode" -ForegroundColor Red
            }
        }
    } else {
        Write-Host "   ⚠️ Auth Key not configured (using environment variable)" -ForegroundColor Yellow
        Write-Host "   Cannot test external API directly" -ForegroundColor Yellow
    }
} else {
    Write-Host "   ❌ Configuration file not found: $appsettingsPath" -ForegroundColor Red
}
Write-Host ""

# Test 4: Check orchestrator logs for recent activity
Write-Host "4. Checking Orchestrator Service Activity..." -ForegroundColor Yellow
$logDir = "C:\Users\Administrator\Documents\GitHub\NICKSCAN-CENTRAL--IMAGE-PORTAL\logs"
if (Test-Path $logDir) {
    $logFiles = Get-ChildItem -Path $logDir -Filter "nickscan-*.txt" | Sort-Object LastWriteTime -Descending | Select-Object -First 1
    if ($logFiles) {
        $latestLog = $logFiles[0].FullName
        $logContent = Get-Content $latestLog -Tail 100
        $orchestratorLogs = $logContent | Select-String -Pattern "\[BACKGROUND-SERVICE\]|Fetching ICUMS batch|ICUMS API|batch download" -Context 0,1
        if ($orchestratorLogs) {
            Write-Host "   ✅ Found orchestrator activity:" -ForegroundColor Green
            $orchestratorLogs | Select-Object -First 5 | ForEach-Object {
                Write-Host "      $($_.Line.Trim())" -ForegroundColor Gray
            }
        } else {
            Write-Host "   ⚠️ No recent orchestrator activity found" -ForegroundColor Yellow
        }
    }
} else {
    Write-Host "   ⚠️ Log directory not found" -ForegroundColor Yellow
}
Write-Host ""

# Summary
Write-Host "========================================" -ForegroundColor Cyan
Write-Host "SUMMARY" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""
Write-Host "If external API test succeeded:" -ForegroundColor Yellow
Write-Host "   • API is accessible" -ForegroundColor White
Write-Host "   • Check record count to see if new data is available" -ForegroundColor White
Write-Host ""
Write-Host "If external API test failed:" -ForegroundColor Yellow
Write-Host "   • Check network connectivity" -ForegroundColor White
Write-Host "   • Verify proxy settings (if enabled)" -ForegroundColor White
Write-Host "   • Check authentication credentials" -ForegroundColor White
Write-Host "   • Verify API endpoint URL is correct" -ForegroundColor White
Write-Host ""


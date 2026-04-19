# =====================================================
# DIAGNOSE ICUMS PROXY CONNECTION
# =====================================================
# Purpose: Test ICUMS API connection through proxy with detailed logging
# Usage: .\DiagnoseICUMSProxyConnection.ps1 [-ContainerNumber "MRKU3468405"]
# =====================================================

param(
    [Parameter(Mandatory=$false)]
    [string]$ContainerNumber = "MRKU3468405",
    
    [Parameter(Mandatory=$false)]
    [string]$ApiBaseUrl = "http://10.0.1.254:5205",
    
    [Parameter(Mandatory=$false)]
    [string]$Username = "admin",
    
    [Parameter(Mandatory=$false)]
    [string]$Password = $env:NICKSCAN_SUPERADMIN_PASSWORD
)

Write-Host "=====================================================" -ForegroundColor Cyan
Write-Host "ICUMS PROXY CONNECTION DIAGNOSTICS" -ForegroundColor Cyan
Write-Host "=====================================================" -ForegroundColor Cyan
Write-Host ""

# Step 1: Test Proxy Server Connectivity
Write-Host "Step 1: Testing Proxy Server Connectivity..." -ForegroundColor Green
Write-Host ""

$proxyHost = "18.135.35.74"
$proxyPort = 3128

Write-Host "  Proxy Server: $proxyHost`:$proxyPort" -ForegroundColor Cyan
$proxyTest = Test-NetConnection -ComputerName $proxyHost -Port $proxyPort -InformationLevel Detailed -WarningAction SilentlyContinue

if ($proxyTest.TcpTestSucceeded) {
    Write-Host "[OK] Proxy server is reachable" -ForegroundColor Green
    Write-Host "     Remote Address: $($proxyTest.RemoteAddress)" -ForegroundColor Gray
    Write-Host "     Source Address: $($proxyTest.SourceAddress)" -ForegroundColor Gray
} else {
    Write-Host "[ERROR] Cannot reach proxy server!" -ForegroundColor Red
    Write-Host "        This will prevent ICUMS API calls from working." -ForegroundColor Yellow
    Write-Host ""
    exit 1
}

Write-Host ""

# Step 2: Load Configuration
Write-Host "Step 2: Loading ICUMS Configuration..." -ForegroundColor Green
Write-Host ""

$appsettingsPath = "src\NickScanCentralImagingPortal.API\appsettings.json"

if (-not (Test-Path $appsettingsPath)) {
    Write-Host "[ERROR] appsettings.json not found at: $appsettingsPath" -ForegroundColor Red
    exit 1
}

$appsettings = Get-Content $appsettingsPath | ConvertFrom-Json

$icumsBaseUrl = $appsettings.ICUMS.BaseUrl
$icumsFetchUrl = $appsettings.ICUMS.FetchUrl
$icumsFetchKey = $appsettings.ICUMS.FetchKey
$icumsAuthKey = $appsettings.ICUMS.AuthKey
$proxyEnabled = $appsettings.ICUMS.Proxy.Enabled
$proxyAddress = $appsettings.ICUMS.Proxy.Address
$proxyBypassLocal = $appsettings.ICUMS.Proxy.BypassOnLocal

Write-Host "[OK] Configuration loaded" -ForegroundColor Green
Write-Host ""
Write-Host "  ICUMS Base URL: $icumsBaseUrl" -ForegroundColor Cyan
Write-Host "  Fetch URL Template: $icumsFetchUrl" -ForegroundColor Cyan
Write-Host "  Fetch Key (ESB_IF_ID): $icumsFetchKey" -ForegroundColor Cyan
Write-Host "  AuthKey Length: $($icumsAuthKey.Length) characters" -ForegroundColor Cyan
Write-Host "  AuthKey (first 20): $($icumsAuthKey.Substring(0, [Math]::Min(20, $icumsAuthKey.Length)))..." -ForegroundColor Gray
Write-Host ""
Write-Host "  Proxy Enabled: $proxyEnabled" -ForegroundColor Cyan
Write-Host "  Proxy Address: $proxyAddress" -ForegroundColor Cyan
Write-Host "  Bypass On Local: $proxyBypassLocal" -ForegroundColor Cyan
Write-Host ""

if (-not $proxyEnabled) {
    Write-Host "[WARNING] Proxy is disabled in configuration!" -ForegroundColor Yellow
    Write-Host "          ICUMS API calls may fail without proxy." -ForegroundColor Yellow
    Write-Host ""
}

# Step 3: Test Direct HTTP Call Through Proxy (PowerShell)
Write-Host "Step 3: Testing Direct HTTP Call Through Proxy..." -ForegroundColor Green
Write-Host ""

$testUrl = $icumsFetchUrl -f $ContainerNumber
Write-Host "  Test URL: $testUrl" -ForegroundColor Cyan
Write-Host "  Container: $ContainerNumber" -ForegroundColor Cyan
Write-Host ""

# Configure proxy for PowerShell web request
$proxyUri = New-Object Uri($proxyAddress)
$webProxy = New-Object System.Net.WebProxy($proxyUri)
$webProxy.BypassProxyOnLocal = $proxyBypassLocal

# Create web request with proxy
$headers = @{
    "ESB_IF_ID" = $icumsFetchKey
    "ESB_AUTH_KEY" = $icumsAuthKey
    "Accept" = "application/json"
    "User-Agent" = "NickScan-Diagnostics/1.0"
}

Write-Host "  Request Headers:" -ForegroundColor Yellow
Write-Host "    ESB_IF_ID: $icumsFetchKey" -ForegroundColor Gray
Write-Host "    ESB_AUTH_KEY: $($icumsAuthKey.Substring(0, [Math]::Min(20, $icumsAuthKey.Length)))..." -ForegroundColor Gray
Write-Host "    Accept: application/json" -ForegroundColor Gray
Write-Host ""

try {
    Write-Host "  Making request through proxy..." -ForegroundColor Yellow
    
    # Create web request with proxy
    $request = [System.Net.WebRequest]::Create($testUrl)
    $request.Proxy = $webProxy
    $request.Method = "GET"
    $request.Timeout = 30000  # 30 seconds
    
    # Add headers
    foreach ($key in $headers.Keys) {
        if ($key -eq "ESB_IF_ID" -or $key -eq "ESB_AUTH_KEY") {
            $request.Headers.Add($key, $headers[$key])
        } elseif ($key -eq "Accept") {
            $request.Accept = $headers[$key]
        } elseif ($key -eq "User-Agent") {
            $request.UserAgent = $headers[$key]
        }
    }
    
    Write-Host "  Proxy configured: $($request.Proxy.GetProxy($testUrl))" -ForegroundColor Gray
    
    $response = $request.GetResponse()
    $statusCode = [int]$response.StatusCode
    $statusDescription = $response.StatusDescription
    
    Write-Host ""
    Write-Host "[OK] Request successful!" -ForegroundColor Green
    Write-Host "     Status Code: $statusCode" -ForegroundColor Green
    Write-Host "     Status: $statusDescription" -ForegroundColor Green
    
    # Read response
    $stream = $response.GetResponseStream()
    $reader = New-Object System.IO.StreamReader($stream)
    $responseBody = $reader.ReadToEnd()
    $reader.Close()
    $stream.Close()
    $response.Close()
    
    Write-Host ""
    Write-Host "  Response Length: $($responseBody.Length) bytes" -ForegroundColor Gray
    
    if ($responseBody.Length -gt 0) {
        Write-Host "  Response Preview (first 500 chars):" -ForegroundColor Yellow
        $preview = if ($responseBody.Length -gt 500) { $responseBody.Substring(0, 500) + "..." } else { $responseBody }
        Write-Host "    $preview" -ForegroundColor Gray
    }
    
} catch {
    $statusCode = $null
    $errorMessage = $_.Exception.Message
    
    if ($_.Exception.Response) {
        $statusCode = [int]$_.Exception.Response.StatusCode
        $statusDescription = $_.Exception.Response.StatusDescription
        
        Write-Host ""
        Write-Host "[ERROR] Request failed!" -ForegroundColor Red
        Write-Host "     Status Code: $statusCode" -ForegroundColor Red
        Write-Host "     Status: $statusDescription" -ForegroundColor Red
        
        # Try to read error response
        try {
            $errorStream = $_.Exception.Response.GetResponseStream()
            $errorReader = New-Object System.IO.StreamReader($errorStream)
            $errorBody = $errorReader.ReadToEnd()
            $errorReader.Close()
            $errorStream.Close()
            
            Write-Host ""
            Write-Host "  Error Response:" -ForegroundColor Yellow
            Write-Host "    $errorBody" -ForegroundColor Gray
        } catch {
            # Ignore if we can't read error response
        }
    } else {
        Write-Host ""
        Write-Host "[ERROR] Request failed!" -ForegroundColor Red
        Write-Host "     Error: $errorMessage" -ForegroundColor Red
    }
    
    Write-Host ""
    Write-Host "  Diagnostic Information:" -ForegroundColor Yellow
    
    if ($statusCode -eq 401) {
        Write-Host "    [ISSUE] Unauthorized - Possible causes:" -ForegroundColor Yellow
        Write-Host "      1. AuthKey is incorrect or expired" -ForegroundColor Yellow
        Write-Host "      2. Interface Key (ESB_IF_ID) is incorrect" -ForegroundColor Yellow
        Write-Host "      3. Headers not being sent correctly through proxy" -ForegroundColor Yellow
    } elseif ($statusCode -eq 400) {
        Write-Host "    [ISSUE] Bad Request - Possible causes:" -ForegroundColor Yellow
        Write-Host "      1. Request headers are malformed" -ForegroundColor Yellow
        Write-Host "      2. Interface Key format is incorrect" -ForegroundColor Yellow
        Write-Host "      3. URL format is incorrect" -ForegroundColor Yellow
    } elseif ($statusCode -eq 407) {
        Write-Host "    [ISSUE] Proxy Authentication Required" -ForegroundColor Yellow
        Write-Host "      The proxy may require authentication" -ForegroundColor Yellow
    } elseif ($null -eq $statusCode) {
        Write-Host "    [ISSUE] Connection Error - Possible causes:" -ForegroundColor Yellow
        Write-Host "      1. Proxy server is unreachable" -ForegroundColor Yellow
        Write-Host "      2. Network connectivity issues" -ForegroundColor Yellow
        Write-Host "      3. Firewall blocking connection" -ForegroundColor Yellow
    }
}

Write-Host ""

# Step 4: Test Through API Endpoint
Write-Host "Step 4: Testing Through API Endpoint..." -ForegroundColor Green
Write-Host ""

Write-Host "  This will test if the API is correctly configured to use the proxy..." -ForegroundColor Gray
Write-Host ""

try {
    # Authenticate
    $loginUrl = "$ApiBaseUrl/api/Authentication/login"
    $loginBody = @{
        username = $Username
        password = $Password
    } | ConvertTo-Json

    $loginResponse = Invoke-RestMethod -Uri $loginUrl -Method Post -Body $loginBody -ContentType "application/json" -ErrorAction Stop
    $token = $loginResponse.token
    
    Write-Host "[OK] Authenticated with API" -ForegroundColor Green
    Write-Host ""
    
    # Test download endpoint
    $downloadUrl = "$ApiBaseUrl/api/ICUMSManual/direct-download/$ContainerNumber"
    $headers = @{
        "Authorization" = "Bearer $token"
        "Content-Type" = "application/json"
    }
    
    Write-Host "  Calling API endpoint: $downloadUrl" -ForegroundColor Cyan
    Write-Host ""
    
    $apiResponse = Invoke-RestMethod -Uri $downloadUrl -Method Post -Headers $headers -ErrorAction Stop
    
    Write-Host "[OK] API endpoint responded" -ForegroundColor Green
    Write-Host ""
    Write-Host "  Response:" -ForegroundColor Yellow
    $apiResponse | ConvertTo-Json -Depth 5 | Write-Host
    
    if ($apiResponse.Success) {
        Write-Host ""
        Write-Host "[OK] ICUMS download successful!" -ForegroundColor Green
    } else {
        Write-Host ""
        Write-Host "[WARNING] ICUMS download failed: $($apiResponse.Message)" -ForegroundColor Yellow
        if ($apiResponse.ErrorMessage) {
            Write-Host "  Error: $($apiResponse.ErrorMessage)" -ForegroundColor Red
        }
    }
    
} catch {
    Write-Host "[ERROR] API test failed: $($_.Exception.Message)" -ForegroundColor Red
}

Write-Host ""
Write-Host "=====================================================" -ForegroundColor Cyan
Write-Host "DIAGNOSTICS COMPLETE" -ForegroundColor Cyan
Write-Host "=====================================================" -ForegroundColor Cyan
Write-Host ""

# Summary
Write-Host "SUMMARY:" -ForegroundColor Cyan
Write-Host ""

$allGood = $true

if ($proxyTest.TcpTestSucceeded) {
    Write-Host "[OK] Proxy server is reachable" -ForegroundColor Green
} else {
    Write-Host "[X] Proxy server is NOT reachable" -ForegroundColor Red
    $allGood = $false
}

if ($proxyEnabled) {
    Write-Host "[OK] Proxy is enabled in configuration" -ForegroundColor Green
} else {
    Write-Host "[X] Proxy is disabled in configuration" -ForegroundColor Red
    $allGood = $false
}

if ($icumsAuthKey -and $icumsAuthKey.Length -gt 0 -and -not $icumsAuthKey.Contains("USE_ENV_VAR")) {
    Write-Host "[OK] AuthKey is configured" -ForegroundColor Green
} else {
    Write-Host "[X] AuthKey is missing or not configured" -ForegroundColor Red
    $allGood = $false
}

if ($allGood) {
    Write-Host ""
    Write-Host "[OK] Basic configuration looks good!" -ForegroundColor Green
    Write-Host "     Check the test results above for API connectivity issues." -ForegroundColor Yellow
} else {
    Write-Host ""
    Write-Host "[ACTION REQUIRED] Fix configuration issues above" -ForegroundColor Red
}


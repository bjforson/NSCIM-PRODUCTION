# =====================================================
# CHECK ICUMS CONFIGURATION
# =====================================================
# Purpose: Verify ICUMS API configuration is correct
# Usage: .\CheckICUMSConfiguration.ps1
# =====================================================

Write-Host "=====================================================" -ForegroundColor Cyan
Write-Host "ICUMS CONFIGURATION CHECK" -ForegroundColor Cyan
Write-Host "=====================================================" -ForegroundColor Cyan
Write-Host ""

# Step 1: Check Environment Variables
Write-Host "Step 1: Checking Environment Variables..." -ForegroundColor Green
Write-Host ""

$authKey = $env:NICKSCAN_ICUMS_AUTH_KEY
$docsAuthKey = $env:NICKSCAN_ICUMS_DOCS_AUTH_KEY
$jsonAuthKey = $env:NICKSCAN_ICUMS_JSON_AUTH_KEY

if ($authKey) {
    Write-Host "[OK] NICKSCAN_ICUMS_AUTH_KEY is set" -ForegroundColor Green
    Write-Host "     Length: $($authKey.Length) characters" -ForegroundColor Gray
    Write-Host "     First 10 chars: $($authKey.Substring(0, [Math]::Min(10, $authKey.Length)))..." -ForegroundColor Gray
} else {
    Write-Host "[ERROR] NICKSCAN_ICUMS_AUTH_KEY is NOT set!" -ForegroundColor Red
    Write-Host "        This is required for ICUMS API calls" -ForegroundColor Yellow
}

Write-Host ""

if ($docsAuthKey) {
    Write-Host "[OK] NICKSCAN_ICUMS_DOCS_AUTH_KEY is set" -ForegroundColor Green
} else {
    Write-Host "[WARNING] NICKSCAN_ICUMS_DOCS_AUTH_KEY is not set (optional)" -ForegroundColor Yellow
}

Write-Host ""

if ($jsonAuthKey) {
    Write-Host "[OK] NICKSCAN_ICUMS_JSON_AUTH_KEY is set" -ForegroundColor Green
} else {
    Write-Host "[WARNING] NICKSCAN_ICUMS_JSON_AUTH_KEY is not set (optional)" -ForegroundColor Yellow
}

Write-Host ""

# Step 2: Check appsettings.json Configuration
Write-Host "Step 2: Checking appsettings.json Configuration..." -ForegroundColor Green
Write-Host ""

$appsettingsPath = "src\NickScanCentralImagingPortal.API\appsettings.json"

if (Test-Path $appsettingsPath) {
    $appsettings = Get-Content $appsettingsPath | ConvertFrom-Json
    
    if ($appsettings.ICUMS) {
        Write-Host "[OK] ICUMS section found in appsettings.json" -ForegroundColor Green
        Write-Host ""
        
        Write-Host "  BaseUrl: $($appsettings.ICUMS.BaseUrl)" -ForegroundColor Cyan
        Write-Host "  FetchUrl: $($appsettings.ICUMS.FetchUrl)" -ForegroundColor Cyan
        Write-Host "  FetchKey: $($appsettings.ICUMS.FetchKey)" -ForegroundColor Cyan
        Write-Host "  TimeoutSeconds: $($appsettings.ICUMS.TimeoutSeconds)" -ForegroundColor Cyan
        Write-Host ""
        
        if ($appsettings.ICUMS.AuthKey -like "*USE_ENV_VAR*") {
            Write-Host "[OK] AuthKey configured to use environment variable" -ForegroundColor Green
        } else {
            Write-Host "[WARNING] AuthKey in appsettings.json (should use env var)" -ForegroundColor Yellow
        }
        
        Write-Host ""
        
        # Check Proxy Configuration
        if ($appsettings.ICUMS.Proxy) {
            Write-Host "  Proxy Configuration:" -ForegroundColor Cyan
            Write-Host "    Enabled: $($appsettings.ICUMS.Proxy.Enabled)" -ForegroundColor Gray
            if ($appsettings.ICUMS.Proxy.Enabled) {
                Write-Host "    Address: $($appsettings.ICUMS.Proxy.Address)" -ForegroundColor Gray
                Write-Host "    BypassOnLocal: $($appsettings.ICUMS.Proxy.BypassOnLocal)" -ForegroundColor Gray
            }
        }
    } else {
        Write-Host "[ERROR] ICUMS section not found in appsettings.json!" -ForegroundColor Red
    }
} else {
    Write-Host "[ERROR] appsettings.json not found at: $appsettingsPath" -ForegroundColor Red
}

Write-Host ""

# Step 3: Test ICUMS API Connectivity
Write-Host "Step 3: Testing ICUMS API Connectivity..." -ForegroundColor Green
Write-Host ""

if ($authKey) {
    $baseUrl = $appsettings.ICUMS.BaseUrl
    $fetchKey = $appsettings.ICUMS.FetchKey
    
    Write-Host "  Testing connection to: $baseUrl" -ForegroundColor Gray
    Write-Host "  Using Interface Key: $fetchKey" -ForegroundColor Gray
    Write-Host ""
    
    try {
        # Test with a simple container number
        $testContainer = "TEST1234567"
        $testUrl = $appsettings.ICUMS.FetchUrl -f $testContainer
        
        Write-Host "  Test URL: $testUrl" -ForegroundColor Gray
        Write-Host ""
        
        $headers = @{
            "ESB_IF_ID" = $fetchKey
            "ESB_AUTH_KEY" = $authKey
            "Accept" = "application/json"
        }
        
        Write-Host "  Making test API call..." -ForegroundColor Yellow
        
        $response = Invoke-WebRequest -Uri $testUrl -Method Get -Headers $headers -TimeoutSec 30 -ErrorAction Stop
        
        Write-Host "[OK] API connection successful!" -ForegroundColor Green
        Write-Host "     Status Code: $($response.StatusCode)" -ForegroundColor Gray
        
        if ($response.StatusCode -eq 200) {
            Write-Host "     Response received (may be empty for test container)" -ForegroundColor Gray
        }
        
    } catch {
        $statusCode = $_.Exception.Response.StatusCode.value__
        $statusDescription = $_.Exception.Response.StatusDescription
        
        Write-Host "[ERROR] API connection failed!" -ForegroundColor Red
        Write-Host "     Status Code: $statusCode" -ForegroundColor Red
        Write-Host "     Status: $statusDescription" -ForegroundColor Red
        
        if ($statusCode -eq 401) {
            Write-Host ""
            Write-Host "  [ISSUE] Unauthorized - Possible causes:" -ForegroundColor Yellow
            Write-Host "    1. AuthKey is incorrect or expired" -ForegroundColor Yellow
            Write-Host "    2. Interface Key (ESB_IF_ID) is incorrect" -ForegroundColor Yellow
            Write-Host "    3. AuthKey format is wrong" -ForegroundColor Yellow
        } elseif ($statusCode -eq 400) {
            Write-Host ""
            Write-Host "  [ISSUE] Bad Request - Possible causes:" -ForegroundColor Yellow
            Write-Host "    1. Request headers are malformed" -ForegroundColor Yellow
            Write-Host "    2. Interface Key format is incorrect" -ForegroundColor Yellow
        }
        
        # Try to get response body
        try {
            $reader = New-Object System.IO.StreamReader($_.Exception.Response.GetResponseStream())
            $responseBody = $reader.ReadToEnd()
            Write-Host ""
            Write-Host "  Response Body: $responseBody" -ForegroundColor Gray
        } catch {
            # Ignore if we can't read the response
        }
    }
} else {
    Write-Host "[SKIP] Cannot test API - AuthKey not configured" -ForegroundColor Yellow
}

Write-Host ""
Write-Host "=====================================================" -ForegroundColor Cyan
Write-Host "CONFIGURATION CHECK COMPLETE" -ForegroundColor Cyan
Write-Host "=====================================================" -ForegroundColor Cyan
Write-Host ""

# Summary
Write-Host "SUMMARY:" -ForegroundColor Cyan
Write-Host ""

$allGood = $true

if (-not $authKey) {
    Write-Host "[X] AuthKey is missing - ICUMS API calls will fail" -ForegroundColor Red
    $allGood = $false
} else {
    Write-Host "[OK] AuthKey is configured" -ForegroundColor Green
}

if ($allGood) {
    Write-Host ""
    Write-Host "[OK] Basic configuration looks good!" -ForegroundColor Green
    Write-Host "     If API calls are still failing, check:" -ForegroundColor Yellow
    Write-Host "     1. AuthKey is valid and not expired" -ForegroundColor Yellow
    Write-Host "     2. Interface Key matches ICUMS requirements" -ForegroundColor Yellow
    Write-Host "     3. Proxy settings if behind a firewall" -ForegroundColor Yellow
    Write-Host "     4. Network connectivity to ICUMS servers" -ForegroundColor Yellow
} else {
    Write-Host ""
    Write-Host "[ACTION REQUIRED] Fix configuration issues above" -ForegroundColor Red
}


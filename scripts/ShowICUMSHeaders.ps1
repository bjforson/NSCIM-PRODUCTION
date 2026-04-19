# =====================================================
# SHOW ICUMS API HEADERS
# =====================================================
# Purpose: Display the exact headers being sent to ICUMS API
# Usage: .\ShowICUMSHeaders.ps1
# =====================================================

Write-Host "=====================================================" -ForegroundColor Cyan
Write-Host "ICUMS API HEADERS CONFIGURATION" -ForegroundColor Cyan
Write-Host "=====================================================" -ForegroundColor Cyan
Write-Host ""

# Load configuration
$appsettingsPath = "src\NickScanCentralImagingPortal.API\appsettings.json"

if (-not (Test-Path $appsettingsPath)) {
    Write-Host "[ERROR] appsettings.json not found!" -ForegroundColor Red
    exit 1
}

$appsettings = Get-Content $appsettingsPath | ConvertFrom-Json

# Get ICUMS settings
$icumsBaseUrl = $appsettings.ICUMS.BaseUrl
$icumsFetchUrl = $appsettings.ICUMS.FetchUrl
$icumsFetchKey = $appsettings.ICUMS.FetchKey
$icumsAuthKey = $appsettings.ICUMS.AuthKey

# Check environment variable as fallback
if ($icumsAuthKey -like "*USE_ENV_VAR*" -or [string]::IsNullOrEmpty($icumsAuthKey)) {
    $icumsAuthKey = $env:NICKSCAN_ICUMS_AUTH_KEY
    Write-Host "[INFO] Using AuthKey from environment variable" -ForegroundColor Yellow
}

if ([string]::IsNullOrEmpty($icumsAuthKey)) {
    Write-Host "[ERROR] AuthKey is not configured!" -ForegroundColor Red
    exit 1
}

# Display headers
Write-Host "EXACT HEADERS BEING SENT TO ICUMS API:" -ForegroundColor Green
Write-Host ""
Write-Host "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━" -ForegroundColor Cyan
Write-Host ""

Write-Host "Header Name: ESB_IF_ID" -ForegroundColor Yellow
Write-Host "  Value: $icumsFetchKey" -ForegroundColor White
Write-Host "  Source: appsettings.json -> ICUMS:FetchKey" -ForegroundColor Gray
Write-Host ""

Write-Host "Header Name: ESB_AUTH_KEY" -ForegroundColor Yellow
Write-Host "  Value: $icumsAuthKey" -ForegroundColor White
Write-Host "  Length: $($icumsAuthKey.Length) characters" -ForegroundColor Gray
Write-Host "  First 20 chars: $($icumsAuthKey.Substring(0, [Math]::Min(20, $icumsAuthKey.Length)))..." -ForegroundColor Gray
Write-Host "  Last 20 chars: ...$($icumsAuthKey.Substring([Math]::Max(0, $icumsAuthKey.Length - 20)))" -ForegroundColor Gray
Write-Host "  Source: appsettings.json -> ICUMS:AuthKey (or NICKSCAN_ICUMS_AUTH_KEY env var)" -ForegroundColor Gray
Write-Host ""

Write-Host "Header Name: Accept" -ForegroundColor Yellow
Write-Host "  Value: application/json" -ForegroundColor White
Write-Host "  Source: Hardcoded in IcumApiService" -ForegroundColor Gray
Write-Host ""

Write-Host "Header Name: User-Agent" -ForegroundColor Yellow
Write-Host "  Value: NickScan-CIM/1.0" -ForegroundColor White
Write-Host "  Source: Hardcoded in HttpClient configuration" -ForegroundColor Gray
Write-Host ""

Write-Host "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━" -ForegroundColor Cyan
Write-Host ""

# Show example request
Write-Host "EXAMPLE REQUEST:" -ForegroundColor Green
Write-Host ""
$exampleUrl = $icumsFetchUrl -f "CONTAINER123"
Write-Host "  URL: $exampleUrl" -ForegroundColor Cyan
Write-Host ""
Write-Host "  Headers:" -ForegroundColor Yellow
Write-Host "    ESB_IF_ID: $icumsFetchKey" -ForegroundColor White
Write-Host "    ESB_AUTH_KEY: $icumsAuthKey" -ForegroundColor White
Write-Host "    Accept: application/json" -ForegroundColor White
Write-Host "    User-Agent: NickScan-CIM/1.0" -ForegroundColor White
Write-Host ""

# Check for common issues
Write-Host "VALIDATION:" -ForegroundColor Green
Write-Host ""

$issues = @()

if ($icumsFetchKey -ne "IF_P01_NSCUNI_05") {
    $issues += "FetchKey is '$icumsFetchKey' but expected 'IF_P01_NSCUNI_05'"
}

if ($icumsAuthKey.Length -ne 64) {
    $issues += "AuthKey length is $($icumsAuthKey.Length) but expected 64 characters"
}

if ($icumsAuthKey -match "[^0-9a-f]") {
    $issues += "AuthKey contains non-hexadecimal characters"
}

if ($issues.Count -eq 0) {
    Write-Host "[OK] Header configuration looks correct" -ForegroundColor Green
} else {
    Write-Host "[WARNING] Potential issues found:" -ForegroundColor Yellow
    foreach ($issue in $issues) {
        Write-Host "  - $issue" -ForegroundColor Yellow
    }
}

Write-Host ""
Write-Host "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━" -ForegroundColor Cyan
Write-Host ""

# Show what the code does
Write-Host "CODE IMPLEMENTATION (IcumApiService.cs):" -ForegroundColor Green
Write-Host ""
Write-Host "  _httpClient.DefaultRequestHeaders.Clear();" -ForegroundColor Gray
Write-Host "  _httpClient.DefaultRequestHeaders.Add(\"ESB_IF_ID\", interfaceKey);" -ForegroundColor Gray
Write-Host "  _httpClient.DefaultRequestHeaders.Add(\"ESB_AUTH_KEY\", authKey);" -ForegroundColor Gray
Write-Host "  _httpClient.DefaultRequestHeaders.Add(\"Accept\", \"application/json\");" -ForegroundColor Gray
Write-Host ""

Write-Host "NOTE: After the code update, detailed header logging will appear in API logs" -ForegroundColor Cyan
Write-Host "      Look for: [ICUMS-API-HEADERS] in the application logs" -ForegroundColor Cyan
Write-Host ""


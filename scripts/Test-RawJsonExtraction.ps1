# Test the RawJsonData extraction fallback by calling the API endpoints
# Usage: .\Test-RawJsonExtraction.ps1 -ContainerNumber "GCNU4908257" -ApiBaseUrl "https://localhost:5001"

param(
    [Parameter(Mandatory=$true)]
    [string]$ContainerNumber,
    
    [Parameter(Mandatory=$false)]
    [string]$ApiBaseUrl = "https://localhost:5001"
)

$ErrorActionPreference = "Stop"

# Bypass SSL certificate validation for localhost testing
if ($PSVersionTable.PSVersion.Major -ge 6) {
    # PowerShell Core
    $null = [System.Net.ServicePointManager]::SecurityProtocol = [System.Net.SecurityProtocolType]::Tls12
} else {
    # Windows PowerShell - use callback
    add-type @"
        using System.Net;
        using System.Security.Cryptography.X509Certificates;
        public class TrustAllCertsPolicy : ICertificatePolicy {
            public bool CheckValidationResult(
                ServicePoint srvPoint, X509Certificate certificate,
                WebRequest request, int certificateProblem) {
                return true;
            }
        }
"@
    [System.Net.ServicePointManager]::CertificatePolicy = New-Object TrustAllCertsPolicy
}

Write-Host "Testing RawJsonData extraction fallback for container: $ContainerNumber" -ForegroundColor Cyan
Write-Host "API Base URL: $ApiBaseUrl" -ForegroundColor Gray
Write-Host ""

try {
    # Test 1: Get group identifier by container
    Write-Host "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━" -ForegroundColor DarkGray
    Write-Host "TEST 1: Get Group Identifier by Container" -ForegroundColor Yellow
    Write-Host ""
    
    $groupIdentifierUrl = "$ApiBaseUrl/api/CargoGroup/by-container/$ContainerNumber"
    Write-Host "Calling: GET $groupIdentifierUrl" -ForegroundColor Gray
    
    try {
        $groupIdentifierResponse = Invoke-RestMethod -Uri $groupIdentifierUrl -Method Get -ErrorAction Stop
        
        Write-Host "✅ SUCCESS: Group identifier found" -ForegroundColor Green
        Write-Host "   GroupIdentifier: $($groupIdentifierResponse.groupIdentifier)" -ForegroundColor White
        Write-Host "   Type: $($groupIdentifierResponse.type)" -ForegroundColor White
        Write-Host ""
    }
    catch {
        if ($_.Exception.Response.StatusCode -eq 404) {
            Write-Host "❌ NOT FOUND: No group identifier found for container" -ForegroundColor Red
            Write-Host "   This means the fallback extraction also failed" -ForegroundColor Yellow
        }
        else {
            Write-Host "❌ ERROR: $($_.Exception.Message)" -ForegroundColor Red
        }
        Write-Host ""
    }
    
    # Test 2: Get full BOE data (FullBOEDataRecordDto)
    Write-Host "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━" -ForegroundColor DarkGray
    Write-Host "TEST 2: Get Full BOE Data (with fallback extraction)" -ForegroundColor Yellow
    Write-Host ""
    
    $icumsDataUrl = "$ApiBaseUrl/api/containerdetails/icums/$ContainerNumber?full=true"
    Write-Host "Calling: GET $icumsDataUrl" -ForegroundColor Gray
    
    try {
        $icumsDataResponse = Invoke-RestMethod -Uri $icumsDataUrl -Method Get -ErrorAction Stop
        
        Write-Host "✅ SUCCESS: Full BOE data retrieved" -ForegroundColor Green
        Write-Host "   ContainerNumber: $($icumsDataResponse.containerNumber)" -ForegroundColor White
        $blNumber = if ($icumsDataResponse.blNumber) { $icumsDataResponse.blNumber } else { "NULL" }
        $declNumber = if ($icumsDataResponse.declarationNumber) { $icumsDataResponse.declarationNumber } else { "NULL" }
        $clearanceType = if ($icumsDataResponse.clearanceType) { $icumsDataResponse.clearanceType } else { "NULL" }
        Write-Host "   BlNumber: $blNumber" -ForegroundColor $(if ($icumsDataResponse.blNumber) { "Green" } else { "Red" })
        Write-Host "   DeclarationNumber: $declNumber" -ForegroundColor $(if ($icumsDataResponse.declarationNumber) { "Green" } else { "Red" })
        Write-Host "   ClearanceType: $clearanceType" -ForegroundColor White
        Write-Host "   AvailableFields: $($icumsDataResponse.availableFields.Count) fields" -ForegroundColor White
        Write-Host ""
        
        if ($icumsDataResponse.blNumber -or $icumsDataResponse.declarationNumber) {
            Write-Host "✅ Grouping fields extracted successfully!" -ForegroundColor Green
        }
        else {
            Write-Host "⚠️  Grouping fields still missing (fallback may not have worked)" -ForegroundColor Yellow
        }
    }
    catch {
        if ($_.Exception.Response.StatusCode -eq 404) {
            Write-Host "❌ NOT FOUND: No BOE data found for container" -ForegroundColor Red
        }
        else {
            Write-Host "❌ ERROR: $($_.Exception.Message)" -ForegroundColor Red
        }
        Write-Host ""
    }
    
    # Test 3: Get cargo group (if we have a group identifier)
    if ($groupIdentifierResponse -and $groupIdentifierResponse.groupIdentifier) {
        Write-Host "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━" -ForegroundColor DarkGray
        Write-Host "TEST 3: Get Cargo Group" -ForegroundColor Yellow
        Write-Host ""
        
        $cargoGroupUrl = "$ApiBaseUrl/api/CargoGroup/$($groupIdentifierResponse.groupIdentifier)?type=$($groupIdentifierResponse.type)"
        Write-Host "Calling: GET $cargoGroupUrl" -ForegroundColor Gray
        
        try {
            $cargoGroupResponse = Invoke-RestMethod -Uri $cargoGroupUrl -Method Get -ErrorAction Stop
            
            Write-Host "✅ SUCCESS: Cargo group retrieved" -ForegroundColor Green
            Write-Host "   Type: $($cargoGroupResponse.type)" -ForegroundColor White
            Write-Host "   TotalContainers: $($cargoGroupResponse.totalContainers)" -ForegroundColor White
            Write-Host "   TotalHouseBLs: $($cargoGroupResponse.totalHouseBLs)" -ForegroundColor White
            Write-Host ""
        }
        catch {
            Write-Host "❌ ERROR: $($_.Exception.Message)" -ForegroundColor Red
            Write-Host ""
        }
    }
    
    Write-Host "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━" -ForegroundColor DarkGray
    Write-Host "Test complete!" -ForegroundColor Green
    
} catch {
    Write-Host "❌ Fatal Error: $($_.Exception.Message)" -ForegroundColor Red
    Write-Host $_.ScriptStackTrace -ForegroundColor Red
    exit 1
}


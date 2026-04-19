# Test ICUMS API Directly (bypasses our implementation)
param(
    [Parameter(Mandatory=$false)]
    [string]$ContainerNumber = "MRSU7761986"
)

# ICUMS API Configuration
$icumsBaseUrl = "https://esb.unipassghana.com:26004"
$authKey = "e80b69d843b14ddca6e8a398eb6c3bb2f587d21126e643b9a31b65b6f7740675"
$interfaceKey = "IF_P01_NSCUNI_05"
$containerEndpoint = "$icumsBaseUrl/api/rm/scan/boe/container/$ContainerNumber"

Write-Host "=====================================================" -ForegroundColor Cyan
Write-Host "TESTING ICUMS API DIRECTLY" -ForegroundColor Cyan
Write-Host "=====================================================" -ForegroundColor Cyan
Write-Host ""
Write-Host "Container Number: $ContainerNumber" -ForegroundColor Yellow
Write-Host "Endpoint: $containerEndpoint" -ForegroundColor Cyan
Write-Host ""

# Setup headers
$headers = @{
    "ESB_IF_ID" = $interfaceKey
    "ESB_AUTH_KEY" = $authKey
    "Accept" = "application/json"
}

Write-Host "Request Headers:" -ForegroundColor Yellow
Write-Host "  ESB_IF_ID: $interfaceKey" -ForegroundColor Gray
Write-Host "  ESB_AUTH_KEY: $($authKey.Substring(0, 20))... (length: $($authKey.Length))" -ForegroundColor Gray
Write-Host "  Accept: application/json" -ForegroundColor Gray
Write-Host ""

# Configure proxy if needed (optional - uncomment if needed)
# $proxyAddress = "http://18.135.35.74:3128"
# $proxy = New-Object System.Net.WebProxy($proxyAddress)
# $proxy.UseDefaultCredentials = $true
# [System.Net.WebRequest]::DefaultWebProxy = $proxy

# Make the API call
Write-Host "Making API call..." -ForegroundColor Yellow
try {
    $response = Invoke-WebRequest -Uri $containerEndpoint -Method Get -Headers $headers -UseBasicParsing -TimeoutSec 600
    
    Write-Host ""
    Write-Host "Response Status:" -ForegroundColor Green
    Write-Host "  Status Code: $($response.StatusCode)" -ForegroundColor Cyan
    Write-Host "  Status Description: $($response.StatusDescription)" -ForegroundColor Cyan
    Write-Host ""
    
    # Get response content
    $responseContent = $response.Content
    $responseLength = $responseContent.Length
    
    Write-Host "Response Content:" -ForegroundColor Yellow
    Write-Host "  Length: $responseLength bytes" -ForegroundColor Cyan
    
    # Try to parse as JSON
    try {
        $jsonResponse = $responseContent | ConvertFrom-Json
        
        Write-Host ""
        Write-Host "Parsed JSON Response:" -ForegroundColor Green
        $jsonResponse | ConvertTo-Json -Depth 10 | Write-Host
        
        # Check for empty response
        if ($jsonResponse.PSObject.Properties.Name -contains "BOEScanDocument") {
            $boeDocuments = $jsonResponse.BOEScanDocument
            if ($null -eq $boeDocuments -or $boeDocuments.Count -eq 0) {
                Write-Host ""
                Write-Host "⚠️  WARNING: BOEScanDocument is empty or null!" -ForegroundColor Red
                Write-Host "This means the container was not found in ICUMS." -ForegroundColor Yellow
            } else {
                Write-Host ""
                Write-Host "✅ BOEScanDocument contains $($boeDocuments.Count) document(s)" -ForegroundColor Green
            }
        }
        
        # Check Status field
        if ($jsonResponse.PSObject.Properties.Name -contains "Status") {
            Write-Host ""
            Write-Host "Response Status: $($jsonResponse.Status)" -ForegroundColor $(if ($jsonResponse.Status -eq "SUCC") { "Green" } else { "Red" })
        }
        
    } catch {
        Write-Host ""
        Write-Host "❌ Error parsing JSON: $($_.Exception.Message)" -ForegroundColor Red
        Write-Host ""
        Write-Host "Raw Response Content:" -ForegroundColor Yellow
        Write-Host $responseContent -ForegroundColor Gray
    }
    
} catch {
    Write-Host ""
    Write-Host "❌ Error calling ICUMS API:" -ForegroundColor Red
    Write-Host "  Exception Type: $($_.Exception.GetType().Name)" -ForegroundColor Red
    Write-Host "  Message: $($_.Exception.Message)" -ForegroundColor Red
    
    if ($_.Exception.Response) {
        Write-Host ""
        Write-Host "HTTP Response Details:" -ForegroundColor Yellow
        Write-Host "  Status Code: $($_.Exception.Response.StatusCode.value__)" -ForegroundColor Cyan
        Write-Host "  Status Description: $($_.Exception.Response.StatusDescription)" -ForegroundColor Cyan
        
        try {
            $errorStream = $_.Exception.Response.GetResponseStream()
            $reader = New-Object System.IO.StreamReader($errorStream)
            $errorContent = $reader.ReadToEnd()
            Write-Host ""
            Write-Host "Error Response Body:" -ForegroundColor Yellow
            Write-Host $errorContent -ForegroundColor Gray
        } catch {
            Write-Host "  Could not read error response body" -ForegroundColor Gray
        }
    }
}

Write-Host ""
Write-Host "=====================================================" -ForegroundColor Cyan


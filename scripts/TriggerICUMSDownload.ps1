# =====================================================
# TRIGGER ICUMS CONTAINER DOWNLOAD
# =====================================================
# Purpose: Trigger ICUMS download for a specific container
# Usage: .\TriggerICUMSDownload.ps1 -ContainerNumber "TEMU9786811" [-Username "admin"] [-Password from $env:NICKSCAN_SUPERADMIN_PASSWORD]
# =====================================================

param(
    [Parameter(Mandatory=$true)]
    [string]$ContainerNumber,
    
    [Parameter(Mandatory=$false)]
    [string]$Username = "admin",
    
    [Parameter(Mandatory=$false)]
    [string]$Password = $env:NICKSCAN_SUPERADMIN_PASSWORD,
    
    [Parameter(Mandatory=$false)]
    [string]$ApiBaseUrl = "http://10.0.1.254:5205"
)

Write-Host "=====================================================" -ForegroundColor Cyan
Write-Host "ICUMS CONTAINER DOWNLOAD TRIGGER" -ForegroundColor Cyan
Write-Host "=====================================================" -ForegroundColor Cyan
Write-Host ""
Write-Host "Container Number: $ContainerNumber" -ForegroundColor Yellow
Write-Host "API Base URL: $ApiBaseUrl" -ForegroundColor Yellow
Write-Host ""

# Step 1: Login to get JWT token
Write-Host "Step 1: Authenticating..." -ForegroundColor Green
try {
    $loginUrl = "$ApiBaseUrl/api/Authentication/login"
    $loginBody = @{
        username = $Username
        password = $Password
    } | ConvertTo-Json

    $loginResponse = Invoke-RestMethod -Uri $loginUrl -Method Post -Body $loginBody -ContentType "application/json" -ErrorAction Stop
    
    if ($loginResponse.token) {
        $token = $loginResponse.token
        Write-Host "✓ Authentication successful!" -ForegroundColor Green
        Write-Host "  User: $($loginResponse.user.username) ($($loginResponse.user.roleName))" -ForegroundColor Gray
        Write-Host ""
    } else {
        Write-Host "✗ Authentication failed: No token received" -ForegroundColor Red
        exit 1
    }
} catch {
    Write-Host "✗ Authentication failed: $($_.Exception.Message)" -ForegroundColor Red
    if ($_.Exception.Response) {
        $reader = New-Object System.IO.StreamReader($_.Exception.Response.GetResponseStream())
        $responseBody = $reader.ReadToEnd()
        Write-Host "  Response: $responseBody" -ForegroundColor Red
    }
    exit 1
}

# Step 2: Trigger ICUMS download
Write-Host "Step 2: Triggering ICUMS download for container $ContainerNumber..." -ForegroundColor Green
try {
    $downloadUrl = "$ApiBaseUrl/api/ICUMSManual/trigger-download/$ContainerNumber"
    $headers = @{
        "Authorization" = "Bearer $token"
        "Content-Type" = "application/json"
    }

    # Optional: Add requestedBy parameter
    $downloadUrlWithParams = "$downloadUrl" + "?requestedBy=PowerShell+Script"

    $downloadResponse = Invoke-RestMethod -Uri $downloadUrlWithParams -Method Post -Headers $headers -ErrorAction Stop
    
    Write-Host ""
    Write-Host "=====================================================" -ForegroundColor Cyan
    Write-Host "DOWNLOAD REQUEST RESULT" -ForegroundColor Cyan
    Write-Host "=====================================================" -ForegroundColor Cyan
    Write-Host ""
    
    if ($downloadResponse.success) {
        Write-Host "✓ SUCCESS: $($downloadResponse.message)" -ForegroundColor Green
        Write-Host "  Container: $($downloadResponse.containerNumber)" -ForegroundColor Gray
        if ($downloadResponse.queuedAt) {
            Write-Host "  Queued At: $($downloadResponse.queuedAt)" -ForegroundColor Gray
        }
        Write-Host ""
        Write-Host "The container has been added to the download queue with high priority." -ForegroundColor Yellow
        Write-Host "The ICUMSDownloadBackgroundService will process it within the next 2 minutes." -ForegroundColor Yellow
    } else {
        Write-Host "⚠ INFO: $($downloadResponse.message)" -ForegroundColor Yellow
        Write-Host "  Container: $($downloadResponse.containerNumber)" -ForegroundColor Gray
        Write-Host ""
        Write-Host "This usually means the container is already in the queue or was recently downloaded." -ForegroundColor Yellow
    }
    
    Write-Host ""
    
    # Display full response for debugging
    Write-Host "Full Response:" -ForegroundColor Cyan
    $downloadResponse | ConvertTo-Json -Depth 5 | Write-Host
    
} catch {
    Write-Host ""
    Write-Host "✗ Download request failed: $($_.Exception.Message)" -ForegroundColor Red
    
    if ($_.Exception.Response) {
        $statusCode = [int]$_.Exception.Response.StatusCode
        Write-Host "  Status Code: $statusCode" -ForegroundColor Red
        
        $reader = New-Object System.IO.StreamReader($_.Exception.Response.GetResponseStream())
        $responseBody = $reader.ReadToEnd()
        
        if ($responseBody) {
            Write-Host "  Response Body:" -ForegroundColor Red
            try {
                $errorJson = $responseBody | ConvertFrom-Json
                $errorJson | ConvertTo-Json -Depth 5 | Write-Host
            } catch {
                Write-Host "  $responseBody" -ForegroundColor Red
            }
        }
    }
    
    exit 1
}

Write-Host ""
Write-Host "=====================================================" -ForegroundColor Cyan
Write-Host "SCRIPT COMPLETED" -ForegroundColor Cyan
Write-Host "=====================================================" -ForegroundColor Cyan


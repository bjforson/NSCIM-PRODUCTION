# =====================================================
# TRIGGER ICUMS BATCH DOWNLOAD
# =====================================================
# Purpose: Manually trigger ICUMS batch download
# Usage: .\TriggerBatchDownload.ps1
# =====================================================

param(
    [Parameter(Mandatory=$false)]
    [string]$ApiBaseUrl = "http://10.0.1.254:5205",
    
    [Parameter(Mandatory=$false)]
    [string]$Username = "admin",
    
    [Parameter(Mandatory=$false)]
    [string]$Password = $env:NICKSCAN_SUPERADMIN_PASSWORD
)

Write-Host "=====================================================" -ForegroundColor Cyan
Write-Host "TRIGGER ICUMS BATCH DOWNLOAD" -ForegroundColor Cyan
Write-Host "=====================================================" -ForegroundColor Cyan
Write-Host ""

# Step 1: Authenticate
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
        Write-Host "[OK] Authentication successful!" -ForegroundColor Green
        Write-Host "  User: $($loginResponse.user.username) ($($loginResponse.user.roleName))" -ForegroundColor Gray
        Write-Host ""
    } else {
        Write-Host "[ERROR] Authentication failed: No token received" -ForegroundColor Red
        exit 1
    }
} catch {
    Write-Host "[ERROR] Authentication failed: $($_.Exception.Message)" -ForegroundColor Red
    exit 1
}

$headers = @{
    "Authorization" = "Bearer $token"
    "Content-Type" = "application/json"
}

# Step 2: Trigger batch download
Write-Host "Step 2: Triggering batch download..." -ForegroundColor Green
Write-Host ""

try {
    $triggerUrl = "$ApiBaseUrl/api/icums/batch/trigger"
    Write-Host "  Endpoint: $triggerUrl" -ForegroundColor Cyan
    Write-Host ""
    
    $response = Invoke-RestMethod -Uri $triggerUrl -Method Post -Headers $headers -ErrorAction Stop
    
    Write-Host "[OK] Batch download triggered!" -ForegroundColor Green
    Write-Host ""
    Write-Host "Response:" -ForegroundColor Yellow
    $response | ConvertTo-Json -Depth 5 | Write-Host
    Write-Host ""
    
    Write-Host "NOTE: The batch download service will process this request." -ForegroundColor Cyan
    Write-Host "      Check the application logs for batch download activity." -ForegroundColor Cyan
    Write-Host ""
    
} catch {
    Write-Host "[ERROR] Failed to trigger batch download: $($_.Exception.Message)" -ForegroundColor Red
    
    if ($_.Exception.Response) {
        $statusCode = [int]$_.Exception.Response.StatusCode.value__
        $reader = New-Object System.IO.StreamReader($_.Exception.Response.GetResponseStream())
        $responseBody = $reader.ReadToEnd()
        $reader.Close()
        
        Write-Host ""
        Write-Host "  Status Code: $statusCode" -ForegroundColor Red
        Write-Host "  Response: $responseBody" -ForegroundColor Red
    }
    
    Write-Host ""
    Write-Host "NOTE: The trigger endpoint may not be fully implemented yet." -ForegroundColor Yellow
    Write-Host "      Check IcumBatchController.cs for implementation status." -ForegroundColor Yellow
    exit 1
}

Write-Host "=====================================================" -ForegroundColor Cyan
Write-Host "COMPLETE" -ForegroundColor Cyan
Write-Host "=====================================================" -ForegroundColor Cyan


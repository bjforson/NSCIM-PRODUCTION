# Simple ICUMS Download Trigger Script
param(
    [Parameter(Mandatory=$true)]
    [string]$ContainerNumber
)

$apiBaseUrl = "http://10.0.1.254:5205"
$username = "admin"
$password = $env:NICKSCAN_SUPERADMIN_PASSWORD

Write-Host "Triggering ICUMS download for: $ContainerNumber" -ForegroundColor Cyan

# Login
$loginUrl = "$apiBaseUrl/api/Authentication/login"
$loginBody = @{ username = $username; password = $password } | ConvertTo-Json
$loginResponse = Invoke-RestMethod -Uri $loginUrl -Method Post -Body $loginBody -ContentType "application/json"
$token = $loginResponse.token

Write-Host "Authenticated successfully" -ForegroundColor Green

# Trigger download
$downloadUrl = "$apiBaseUrl/api/ICUMSManual/trigger-download/$ContainerNumber" + "?requestedBy=PowerShell+Script"
$headers = @{ Authorization = "Bearer $token" }
$result = Invoke-RestMethod -Uri $downloadUrl -Method Post -Headers $headers

Write-Host ""
Write-Host "Result:" -ForegroundColor Yellow
$result | ConvertTo-Json | Write-Host


# Direct ICUMS Download (bypasses queue)
param(
    [Parameter(Mandatory=$true)]
    [string]$ContainerNumber
)

$apiBaseUrl = "http://10.0.1.254:5205"
$username = "admin"
$password = $env:NICKSCAN_SUPERADMIN_PASSWORD

Write-Host "Direct ICUMS download for: $ContainerNumber" -ForegroundColor Cyan

# Login
$loginUrl = "$apiBaseUrl/api/Authentication/login"
$loginBody = @{ username = $username; password = $password } | ConvertTo-Json
$loginResponse = Invoke-RestMethod -Uri $loginUrl -Method Post -Body $loginBody -ContentType "application/json"
$token = $loginResponse.token
$headers = @{ Authorization = "Bearer $token" }

Write-Host "Authenticated successfully" -ForegroundColor Green

# Direct download
$downloadUrl = "$apiBaseUrl/api/ICUMSManual/direct-download/$ContainerNumber"
$result = Invoke-RestMethod -Uri $downloadUrl -Method Post -Headers $headers

Write-Host ""
Write-Host "Result:" -ForegroundColor Yellow
$result | ConvertTo-Json -Depth 5 | Write-Host

if ($result.success) {
    Write-Host ""
    Write-Host "SUCCESS! Container downloaded and processed." -ForegroundColor Green
    if ($result.alreadyExists) {
        Write-Host "Container already had ICUMS data." -ForegroundColor Yellow
    }
} else {
    Write-Host ""
    Write-Host "FAILED: $($result.message)" -ForegroundColor Red
    if ($result.errorMessage) {
        Write-Host "Error: $($result.errorMessage)" -ForegroundColor Red
    }
}


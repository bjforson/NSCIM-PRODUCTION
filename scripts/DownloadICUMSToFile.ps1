# Download ICUMS container data and save to a file location
# Usage: .\DownloadICUMSToFile.ps1 -ContainerNumber FCIU5884650 [-OutputPath "C:\path\to\save.json"]
param(
    [Parameter(Mandatory=$true)]
    [string]$ContainerNumber,

    [Parameter(Mandatory=$false)]
    [string]$OutputPath = ""

)

$apiBaseUrl = "http://10.0.1.254:5205"
$username = "admin"
$password = $env:NICKSCAN_SUPERADMIN_PASSWORD
$defaultBackupDir = "C:\ICUMS Downloads\ContainerData"

# Default output: project folder / ICUMS_Exports / {ContainerNumber}_{timestamp}.json
if ([string]::IsNullOrEmpty($OutputPath)) {
    $scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
    $projectRoot = Split-Path -Parent $scriptDir
    $exportsDir = Join-Path $projectRoot "ICUMS_Exports"
    if (!(Test-Path $exportsDir)) { New-Item -ItemType Directory -Path $exportsDir -Force | Out-Null }
    $timestamp = Get-Date -Format "yyyyMMdd_HHmmss"
    $OutputPath = Join-Path $exportsDir "${ContainerNumber}_${timestamp}.json"
}

Write-Host "ICUMS Download: $ContainerNumber" -ForegroundColor Cyan
Write-Host "Output path: $OutputPath" -ForegroundColor Gray

# Login
$loginUrl = "$apiBaseUrl/api/Authentication/login"
$loginBody = @{ username = $username; password = $password } | ConvertTo-Json
try {
    $loginResponse = Invoke-RestMethod -Uri $loginUrl -Method Post -Body $loginBody -ContentType "application/json"
} catch {
    Write-Host "FAILED: Could not reach API at $apiBaseUrl" -ForegroundColor Red
    Write-Host $_.Exception.Message -ForegroundColor Red
    exit 1
}

$token = $loginResponse.token
$headers = @{ Authorization = "Bearer $token" }

Write-Host "Authenticated successfully" -ForegroundColor Green

# Direct download (fetches from ICUMS API and saves to DB + backup folder)
$downloadUrl = "$apiBaseUrl/api/ICUMSManual/direct-download/$ContainerNumber"
try {
    $result = Invoke-RestMethod -Uri $downloadUrl -Method Post -Headers $headers
} catch {
    Write-Host "FAILED: API request failed" -ForegroundColor Red
    Write-Host $_.Exception.Message -ForegroundColor Red
    exit 1
}

Write-Host ""
if (-not $result.success) {
    Write-Host "FAILED: $($result.message)" -ForegroundColor Red
    if ($result.errorMessage) { Write-Host "Error: $($result.errorMessage)" -ForegroundColor Red }
    exit 1
}

Write-Host "ICUMS data fetched successfully" -ForegroundColor Green
if ($result.alreadyExists) {
    Write-Host "(Container already had ICUMS data - re-fetched)" -ForegroundColor Yellow
}

# Find the backup file (ContainerData_{container}_{timestamp}.json)
$pattern = "ContainerData_${ContainerNumber}_*.json"
$backupFiles = Get-ChildItem -Path $defaultBackupDir -Filter $pattern -ErrorAction SilentlyContinue | Sort-Object LastWriteTime -Descending

if ($backupFiles -and $backupFiles.Count -gt 0) {
    $sourceFile = $backupFiles[0].FullName
    Copy-Item -Path $sourceFile -Destination $OutputPath -Force
    Write-Host ""
    Write-Host "Saved to: $OutputPath" -ForegroundColor Green
} else {
    # Fallback: get stored ICUMS data from containerdetails endpoint
    Write-Host "Backup file not found in $defaultBackupDir" -ForegroundColor Yellow
    Write-Host "Fetching stored ICUMS data from API..." -ForegroundColor Gray
    $containerUrl = "$apiBaseUrl/api/containerdetails/icums/$ContainerNumber" + "?full=true"
    try {
        $containerData = Invoke-RestMethod -Uri $containerUrl -Method Get -Headers $headers
        $containerData | ConvertTo-Json -Depth 15 | Set-Content -Path $OutputPath -Encoding UTF8
        Write-Host ""
        Write-Host "Saved ICUMS data to: $OutputPath" -ForegroundColor Green
    } catch {
        Write-Host "Could not retrieve data: $($_.Exception.Message)" -ForegroundColor Red
        Write-Host "Check $defaultBackupDir for backup files, or ensure container has ICUMS data." -ForegroundColor Yellow
        exit 1
    }
}

# Check if image exists via image processing pipeline
# Usage: .\Check-ImageViaPipeline.ps1 -ContainerNumber "MRSU5131340"

param(
    [Parameter(Mandatory=$true)]
    [string]$ContainerNumber
)

Write-Host "============================================================" -ForegroundColor Cyan
Write-Host "IMAGE PIPELINE CHECK" -ForegroundColor Cyan
Write-Host "============================================================" -ForegroundColor Cyan
Write-Host ""
Write-Host "Container: $ContainerNumber" -ForegroundColor Green
Write-Host ""

try {
    # Load API base URL from configuration or use default
    $appsettingsPath = "src\NickScanCentralImagingPortal.API\appsettings.json"
    $scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
    $projectRoot = Split-Path -Parent $scriptDir
    $appsettingsPath = Join-Path $projectRoot $appsettingsPath
    
    if (Test-Path $appsettingsPath) {
        $appsettings = Get-Content $appsettingsPath -Raw | ConvertFrom-Json
        $apiBaseUrl = $appsettings.ApiSettings.PublicBaseUrl
        if ([string]::IsNullOrWhiteSpace($apiBaseUrl)) {
            $apiBaseUrl = "http://10.0.1.254:5205"
        }
    } else {
        $apiBaseUrl = "http://10.0.1.254:5205"
    }
    
    Write-Host "API Base URL: $apiBaseUrl" -ForegroundColor Gray
    Write-Host ""
    
    # Test 1: Check complete container data endpoint
    Write-Host "Test 1: Checking complete container data..." -ForegroundColor Cyan
    $completeUrl = "$apiBaseUrl/api/ImageProcessing/container/$([Uri]::EscapeDataString($ContainerNumber))/complete"
    Write-Host "  URL: $completeUrl" -ForegroundColor Gray
    
    try {
        $response = Invoke-RestMethod -Uri $completeUrl -Method Get -TimeoutSec 30 -ErrorAction Stop
        Write-Host "  [OK] Complete data found!" -ForegroundColor Green
        Write-Host "    Scanner: $($response.detectedScanner)" -ForegroundColor White
        Write-Host "    Image Size: $([Math]::Round($response.imageSizeBytes / 1024.0, 2)) KB" -ForegroundColor White
        Write-Host "    MIME Type: $($response.mimeType)" -ForegroundColor White
        Write-Host "    From Cache: $($response.fromCache)" -ForegroundColor White
        if ($response.fs6000Data) {
            Write-Host "    FS6000 Image Count: $($response.fs6000Data.imageCount)" -ForegroundColor White
        }
        Write-Host ""
    } catch {
        if ($_.Exception.Response.StatusCode -eq 404) {
            Write-Host "  [NOT FOUND] Complete data not found for container" -ForegroundColor Yellow
            Write-Host ""
        } else {
            Write-Host "  [ERROR] $($_.Exception.Message)" -ForegroundColor Red
            Write-Host ""
        }
    }
    
    # Test 2: Check image endpoint directly
    Write-Host "Test 2: Checking image endpoint (Main type)..." -ForegroundColor Cyan
    $imageUrl = "$apiBaseUrl/api/ImageProcessing/container/$([Uri]::EscapeDataString($ContainerNumber))/complete/image?imageType=Main&size=full"
    Write-Host "  URL: $imageUrl" -ForegroundColor Gray
    
    try {
        $imageResponse = Invoke-WebRequest -Uri $imageUrl -Method Get -TimeoutSec 30 -ErrorAction Stop
        Write-Host "  [OK] Image found! Status: $($imageResponse.StatusCode)" -ForegroundColor Green
        Write-Host "    Content-Type: $($imageResponse.Headers.'Content-Type')" -ForegroundColor White
        Write-Host "    Content-Length: $([Math]::Round($imageResponse.RawContentLength / 1024.0, 2)) KB" -ForegroundColor White
        Write-Host ""
    } catch {
        if ($_.Exception.Response.StatusCode -eq 404) {
            Write-Host "  [NOT FOUND] Image not found for container" -ForegroundColor Yellow
            Write-Host ""
        } else {
            Write-Host "  [ERROR] $($_.Exception.Message)" -ForegroundColor Red
            Write-Host ""
        }
    }
    
    # Test 3: Check scanner type detection
    Write-Host "Test 3: Checking scanner type detection..." -ForegroundColor Cyan
    
    # Load connection string from appsettings
    if (Test-Path $appsettingsPath) {
        $appsettings = Get-Content $appsettingsPath -Raw | ConvertFrom-Json
        $connectionString = $appsettings.ConnectionStrings.NS_CIS_Connection
        
        if ($connectionString) {
            $connection = New-Object System.Data.SqlClient.SqlConnection($connectionString)
            $connection.Open()
            
            # Check FS6000
            $fs6000Query = "SELECT COUNT(*) FROM FS6000Scans WHERE ContainerNumber = @ContainerNumber"
            $fs6000Cmd = New-Object System.Data.SqlClient.SqlCommand($fs6000Query, $connection)
            $fs6000Cmd.Parameters.AddWithValue("@ContainerNumber", $ContainerNumber) | Out-Null
            $fs6000Count = $fs6000Cmd.ExecuteScalar()
            
            # Check ASE
            $aseQuery = "SELECT COUNT(*) FROM AseScans WHERE ContainerNumber = @ContainerNumber"
            $aseCmd = New-Object System.Data.SqlClient.SqlCommand($aseQuery, $connection)
            $aseCmd.Parameters.AddWithValue("@ContainerNumber", $ContainerNumber) | Out-Null
            $aseCount = $aseCmd.ExecuteScalar()
            
            # Check FS6000 Images
            $fs6000ImageQuery = @"
                SELECT COUNT(*) 
                FROM FS6000Images i
                INNER JOIN FS6000Scans s ON i.ScanId = s.Id
                WHERE s.ContainerNumber = @ContainerNumber
"@
            $fs6000ImageCmd = New-Object System.Data.SqlClient.SqlCommand($fs6000ImageQuery, $connection)
            $fs6000ImageCmd.Parameters.AddWithValue("@ContainerNumber", $ContainerNumber) | Out-Null
            $fs6000ImageCount = $fs6000ImageCmd.ExecuteScalar()
            
            $connection.Close()
            
            Write-Host "  FS6000 Scans: $fs6000Count" -ForegroundColor $(if ($fs6000Count -gt 0) { 'Green' } else { 'Yellow' })
            Write-Host "  FS6000 Images: $fs6000ImageCount" -ForegroundColor $(if ($fs6000ImageCount -gt 0) { 'Green' } else { 'Yellow' })
            Write-Host "  ASE Scans: $aseCount" -ForegroundColor $(if ($aseCount -gt 0) { 'Green' } else { 'Yellow' })
            Write-Host ""
            
            # Determine detected scanner type
            if ($fs6000Count -gt 0) {
                Write-Host "  Detected Scanner: FS6000" -ForegroundColor Green
            } elseif ($aseCount -gt 0) {
                Write-Host "  Detected Scanner: ASE" -ForegroundColor Green
            } else {
                Write-Host "  Detected Scanner: Unknown (no records found)" -ForegroundColor Red
            }
            
            Write-Host ""
            
            if ($fs6000Count -gt 0 -and $fs6000ImageCount -eq 0) {
                Write-Host "  [WARNING] FS6000 scan exists but has NO images in database!" -ForegroundColor Yellow
                Write-Host "    This means images may exist in archive files but haven't been ingested." -ForegroundColor Yellow
                Write-Host ""
            }
        }
    }
    
    Write-Host "============================================================" -ForegroundColor Cyan
    Write-Host "CHECK COMPLETE" -ForegroundColor Cyan
    Write-Host "============================================================" -ForegroundColor Cyan
    
} catch {
    Write-Host "ERROR: $($_.Exception.Message)" -ForegroundColor Red
    Write-Host $_.ScriptStackTrace -ForegroundColor Red
    exit 1
}


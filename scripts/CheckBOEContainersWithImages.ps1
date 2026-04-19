# Script to check BOE 40825476816 for all attached containers and find which have images
# Usage: .\CheckBOEContainersWithImages.ps1

param(
    [Parameter(Mandatory=$false)]
    [string]$BOENumber = "40825476816",
    
    [Parameter(Mandatory=$false)]
    [string]$ApiBaseUrl = "http://10.0.1.254:5205"
)

Write-Host "========================================" -ForegroundColor Cyan
Write-Host "BOE Container Image Checker" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host "BOE/Declaration Number: $BOENumber" -ForegroundColor Yellow
Write-Host "API Base URL: $ApiBaseUrl" -ForegroundColor Yellow
Write-Host ""

try {
    # Step 1: Get all containers for this BOE/Declaration
    Write-Host "Step 1: Fetching containers for BOE $BOENumber..." -ForegroundColor Green
    $containersUrl = "$ApiBaseUrl/api/consolidatedcargo/declaration/$BOENumber/containers"
    
    $containersResponse = Invoke-RestMethod -Uri $containersUrl -Method Get -ErrorAction Stop
    $containers = $containersResponse
    
    if ($null -eq $containers -or $containers.Count -eq 0) {
        Write-Host "❌ No containers found for BOE $BOENumber" -ForegroundColor Red
        exit 1
    }
    
    Write-Host "✅ Found $($containers.Count) container(s) for BOE $BOENumber" -ForegroundColor Green
    Write-Host ""
    
    # Step 2: Check each container for images
    Write-Host "Step 2: Checking images for each container..." -ForegroundColor Green
    Write-Host ""
    
    $results = @()
    $containersWithImages = @()
    $containersWithoutImages = @()
    
    foreach ($container in $containers) {
        Write-Host "  Checking container: $container" -ForegroundColor Cyan -NoNewline
        
        try {
            # Get basic info for container (includes ImageCount)
            $basicInfoUrl = "$ApiBaseUrl/api/containerdetails/$container/basic"
            $basicInfo = Invoke-RestMethod -Uri $basicInfoUrl -Method Get -ErrorAction Stop
            
            $hasImages = $basicInfo.ImageCount -gt 0
            $imageCount = $basicInfo.ImageCount
            
            $result = [PSCustomObject]@{
                ContainerNumber = $container
                HasImages = $hasImages
                ImageCount = $imageCount
                ScannerType = $basicInfo.ScannerType
                HasScannerData = $basicInfo.ScannerRecordCount -gt 0
                HasICUMSData = $basicInfo.ICUMSRecordCount -gt 0
                ValidationStatus = $basicInfo.ValidationStatus
                CompletenessScore = $basicInfo.DataCompletenessScore
            }
            
            $results += $result
            
            if ($hasImages) {
                Write-Host " ✅ - $imageCount image(s)" -ForegroundColor Green
                $containersWithImages += $container
            } else {
                Write-Host " ❌ - No images" -ForegroundColor Red
                $containersWithoutImages += $container
            }
        }
        catch {
            Write-Host " ⚠️  - Error: $($_.Exception.Message)" -ForegroundColor Yellow
            $result = [PSCustomObject]@{
                ContainerNumber = $container
                HasImages = $false
                ImageCount = 0
                ScannerType = "Error"
                HasScannerData = $false
                HasICUMSData = $false
                ValidationStatus = "Error"
                CompletenessScore = 0
                Error = $_.Exception.Message
            }
            $results += $result
            $containersWithoutImages += $container
        }
    }
    
    Write-Host ""
    Write-Host "========================================" -ForegroundColor Cyan
    Write-Host "SUMMARY" -ForegroundColor Cyan
    Write-Host "========================================" -ForegroundColor Cyan
    Write-Host ""
    
    Write-Host "Total Containers: $($containers.Count)" -ForegroundColor White
    Write-Host "Containers WITH Images: $($containersWithImages.Count)" -ForegroundColor Green
    Write-Host "Containers WITHOUT Images: $($containersWithoutImages.Count)" -ForegroundColor Red
    Write-Host ""
    
    # Display detailed results table
    Write-Host "DETAILED RESULTS:" -ForegroundColor Cyan
    Write-Host ""
    $results | Format-Table -AutoSize
    
    Write-Host ""
    Write-Host "Containers WITH Images:" -ForegroundColor Green
    if ($containersWithImages.Count -gt 0) {
        foreach ($container in $containersWithImages) {
            $result = $results | Where-Object { $_.ContainerNumber -eq $container }
            Write-Host "  ✅ $container - $($result.ImageCount) image(s) [$($result.ScannerType)]" -ForegroundColor Green
        }
    } else {
        Write-Host "  None" -ForegroundColor Gray
    }
    
    Write-Host ""
    Write-Host "Containers WITHOUT Images:" -ForegroundColor Red
    if ($containersWithoutImages.Count -gt 0) {
        foreach ($container in $containersWithoutImages) {
            Write-Host "  ❌ $container" -ForegroundColor Red
        }
    } else {
        Write-Host "  None" -ForegroundColor Gray
    }
    
    Write-Host ""
    Write-Host "========================================" -ForegroundColor Cyan
    
    # Export to CSV if requested
    $exportPath = "BOE_${BOENumber}_ContainerImageCheck_$(Get-Date -Format 'yyyyMMdd_HHmmss').csv"
    $results | Export-Csv -Path $exportPath -NoTypeInformation
    Write-Host "Results exported to: $exportPath" -ForegroundColor Yellow
    
}
catch {
    Write-Host ""
    Write-Host "❌ ERROR: $($_.Exception.Message)" -ForegroundColor Red
    Write-Host "Stack Trace: $($_.ScriptStackTrace)" -ForegroundColor Red
    exit 1
}

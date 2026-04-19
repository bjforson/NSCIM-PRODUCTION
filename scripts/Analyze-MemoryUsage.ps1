# Memory Analysis Script for NickScan API
# This script helps identify memory usage patterns and potential leaks

param(
    [string]$ProcessName = "NickScanCentralImagingPortal.API",
    [int]$IntervalSeconds = 30,
    [int]$DurationMinutes = 10
)

Write-Host "=== Memory Analysis for $ProcessName ===" -ForegroundColor Cyan
Write-Host "Monitoring for $DurationMinutes minutes at $IntervalSeconds second intervals" -ForegroundColor Yellow
Write-Host ""

$process = Get-Process -Name $ProcessName -ErrorAction SilentlyContinue
if (-not $process) {
    Write-Host "ERROR: Process '$ProcessName' not found!" -ForegroundColor Red
    Write-Host "Available processes:" -ForegroundColor Yellow
    Get-Process | Where-Object { $_.ProcessName -like "*NickScan*" } | Select-Object ProcessName, Id, @{Name="Memory(MB)";Expression={[math]::Round($_.WorkingSet64/1MB,2)}}
    exit 1
}

$endTime = (Get-Date).AddMinutes($DurationMinutes)
$results = @()

Write-Host "Time                 | Memory (MB) | Memory (GB) | Change (MB) | Gen0 GC | Gen1 GC | Gen2 GC | Handles" -ForegroundColor Green
Write-Host "---------------------|-------------|-------------|-------------|---------|---------|---------|--------" -ForegroundColor Green

$previousMemory = 0
$previousGen0 = 0
$previousGen1 = 0
$previousGen2 = 0

while ((Get-Date) -lt $endTime) {
    $process.Refresh()
    $currentMemory = [math]::Round($process.WorkingSet64 / 1MB, 2)
    $currentMemoryGB = [math]::Round($process.WorkingSet64 / 1GB, 2)
    $change = $currentMemory - $previousMemory
    
    # Get GC stats (requires .NET runtime)
    $gcGen0 = [System.GC]::CollectionCount(0)
    $gcGen1 = [System.GC]::CollectionCount(1)
    $gcGen2 = [System.GC]::CollectionCount(2)
    
    $gen0Delta = $gcGen0 - $previousGen0
    $gen1Delta = $gcGen1 - $previousGen1
    $gen2Delta = $gcGen2 - $previousGen2
    
    $timestamp = Get-Date -Format "yyyy-MM-dd HH:mm:ss"
    
    $color = "White"
    if ($change -gt 100) { $color = "Red" }
    elseif ($change -gt 50) { $color = "Yellow" }
    elseif ($change -lt -50) { $color = "Green" }
    
    Write-Host ("{0} | {1,11:N2} | {2,11:N2} | {3,11:N2} | {4,7} | {5,7} | {6,7} | {7,7}" -f `
        $timestamp, $currentMemory, $currentMemoryGB, $change, $gen0Delta, $gen1Delta, $gen2Delta, $process.HandleCount) `
        -ForegroundColor $color
    
    $results += [PSCustomObject]@{
        Time = $timestamp
        MemoryMB = $currentMemory
        MemoryGB = $currentMemoryGB
        ChangeMB = $change
        Gen0GC = $gen0Delta
        Gen1GC = $gen1Delta
        Gen2GC = $gen2Delta
        Handles = $process.HandleCount
    }
    
    $previousMemory = $currentMemory
    $previousGen0 = $gcGen0
    $previousGen1 = $gcGen1
    $previousGen2 = $gcGen2
    
    Start-Sleep -Seconds $IntervalSeconds
}

Write-Host ""
Write-Host "=== Summary ===" -ForegroundColor Cyan
$totalChange = $results[-1].MemoryMB - $results[0].MemoryMB
$avgMemory = ($results | Measure-Object -Property MemoryMB -Average).Average
$maxMemory = ($results | Measure-Object -Property MemoryMB -Maximum).Maximum
$minMemory = ($results | Measure-Object -Property MemoryMB -Minimum).Minimum
$totalGen2GC = ($results | Measure-Object -Property Gen2GC -Sum).Sum

Write-Host "Total Memory Change: $([math]::Round($totalChange, 2)) MB" -ForegroundColor $(if ($totalChange -gt 0) { "Red" } else { "Green" })
Write-Host "Average Memory: $([math]::Round($avgMemory, 2)) MB ($([math]::Round($avgMemory/1024, 2)) GB)" -ForegroundColor Yellow
Write-Host "Peak Memory: $([math]::Round($maxMemory, 2)) MB ($([math]::Round($maxMemory/1024, 2)) GB)" -ForegroundColor Yellow
Write-Host "Min Memory: $([math]::Round($minMemory, 2)) MB ($([math]::Round($minMemory/1024, 2)) GB)" -ForegroundColor Yellow
Write-Host "Total Gen2 GC Collections: $totalGen2GC" -ForegroundColor $(if ($totalGen2GC -gt 0) { "Green" } else { "Yellow" })

if ($totalChange -gt 500) {
    Write-Host ""
    Write-Host "WARNING: Memory increased by more than 500 MB during monitoring!" -ForegroundColor Red
    Write-Host "This indicates a potential memory leak." -ForegroundColor Red
}

# Export to CSV
$csvPath = "memory-analysis-$(Get-Date -Format 'yyyyMMdd-HHmmss').csv"
$results | Export-Csv -Path $csvPath -NoTypeInformation
Write-Host ""
Write-Host "Results exported to: $csvPath" -ForegroundColor Green


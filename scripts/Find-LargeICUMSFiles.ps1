# Find Large ICUMS Files for Testing
# Helps identify files suitable for bulk operations and streaming parser testing

param(
    [string]$DownloadsPath = "C:\ICUMS Downloads",
    [int]$MinSizeMB = 10,
    [switch]$ShowDetails
)

Write-Host "🔍 Finding Large ICUMS Files for Testing" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""

if (-not (Test-Path $DownloadsPath)) {
    Write-Host "❌ Downloads path not found: $DownloadsPath" -ForegroundColor Red
    exit 1
}

Write-Host "Scanning: $DownloadsPath" -ForegroundColor Gray
Write-Host "Minimum size: $MinSizeMB MB" -ForegroundColor Gray
Write-Host ""

$files = Get-ChildItem -Path $DownloadsPath -Recurse -Filter "*.json" | 
    Where-Object { $_.Length -ge ($MinSizeMB * 1MB) } |
    Sort-Object Length -Descending |
    Select-Object -First 20

if ($files) {
    Write-Host "✅ Found $($files.Count) large files:" -ForegroundColor Green
    Write-Host ""
    
    $fileInfo = $files | ForEach-Object {
        [PSCustomObject]@{
            FileName = $_.Name
            Path = $_.FullName
            SizeMB = [math]::Round($_.Length / 1MB, 2)
            LastModified = $_.LastWriteTime
            Age = (New-TimeSpan -Start $_.LastWriteTime -End (Get-Date)).Days
        }
    }
    
    $fileInfo | Format-Table -AutoSize
    
    Write-Host ""
    Write-Host "📊 Summary:" -ForegroundColor Cyan
    $totalSize = ($fileInfo | Measure-Object -Property SizeMB -Sum).Sum
    $avgSize = ($fileInfo | Measure-Object -Property SizeMB -Average).Average
    $maxSize = ($fileInfo | Measure-Object -Property SizeMB -Maximum).Maximum
    
    Write-Host "  - Total size: $([math]::Round($totalSize, 2)) MB" -ForegroundColor White
    Write-Host "  - Average size: $([math]::Round($avgSize, 2)) MB" -ForegroundColor White
    Write-Host "  - Largest file: $([math]::Round($maxSize, 2)) MB" -ForegroundColor White
    Write-Host ""
    
    if ($ShowDetails) {
        Write-Host "📝 File Details:" -ForegroundColor Cyan
        foreach ($file in $files) {
            Write-Host ""
            Write-Host "  File: $($file.Name)" -ForegroundColor Yellow
            Write-Host "    Path: $($file.FullName)" -ForegroundColor Gray
            Write-Host "    Size: $([math]::Round($file.Length / 1MB, 2)) MB" -ForegroundColor Gray
            Write-Host "    Modified: $($file.LastWriteTime)" -ForegroundColor Gray
            
            # Try to count manifest items (quick check)
            try {
                $content = Get-Content $file.FullName -Raw -ErrorAction SilentlyContinue
                if ($content) {
                    $manifestMatches = ([regex]::Matches($content, '"HsCode"')).Count
                    Write-Host "    Estimated manifest items: ~$manifestMatches" -ForegroundColor $(if ($manifestMatches -ge 1000) { "Green" } else { "Yellow" })
                }
            } catch {
                Write-Host "    (Could not analyze content)" -ForegroundColor Gray
            }
        }
    }
    
    Write-Host ""
    Write-Host "💡 Testing Recommendations:" -ForegroundColor Cyan
    Write-Host "  - Use files with 1000+ manifest items for bulk operations test" -ForegroundColor White
    Write-Host "  - Use files > 50 MB for streaming parser memory test" -ForegroundColor White
    Write-Host "  - Check database for actual manifest item counts" -ForegroundColor White
    
} else {
    Write-Host "⚠️ No large files found (>= $MinSizeMB MB)" -ForegroundColor Yellow
    Write-Host "  Try lowering MinSizeMB parameter or check downloads path" -ForegroundColor Gray
}


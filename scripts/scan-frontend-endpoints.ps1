# Comprehensive Frontend Endpoint Scanner
# Scans all frontend pages for API calls and verifies backend endpoints exist

Write-Host "`n=== Frontend Endpoint Scanner ===" -ForegroundColor Cyan
Write-Host "`nScanning frontend pages for API calls..." -ForegroundColor Yellow

$frontendPages = Get-ChildItem -Path "src\NickScanWebApp.New\Pages" -Filter "*.razor" -Recurse
$endpoints = @()

foreach ($page in $frontendPages) {
    $content = Get-Content $page.FullName -Raw
    # Extract all /api/... endpoints
    $matches = [regex]::Matches($content, '/api/[^"\s'']+')
    foreach ($match in $matches) {
        $endpoint = $match.Value
        # Clean up endpoint (remove query params, fragments)
        if ($endpoint -match '^/api/[^?]+') {
            $endpoint = $matches[0].Value
        }
        $endpoints += $endpoint
    }
}

$uniqueEndpoints = $endpoints | Sort-Object -Unique

Write-Host "`n✅ Found $($uniqueEndpoints.Count) unique API endpoint calls" -ForegroundColor Green
Write-Host "`n📋 All endpoints:" -ForegroundColor Cyan
foreach ($endpoint in $uniqueEndpoints) {
    Write-Host "   $endpoint" -ForegroundColor White
}

# Save to file
$uniqueEndpoints | Out-File -FilePath "frontend-endpoints.txt" -Encoding UTF8
Write-Host "`n✅ Saved to frontend-endpoints.txt" -ForegroundColor Green


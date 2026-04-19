# Quick script to find which port the API is running on

$ports = @(5000, 5001, 5205, 54423, 7161)
$found = $false

Write-Host "Testing API ports..." -ForegroundColor Cyan
Write-Host ""

foreach ($port in $ports) {
    try {
        $response = Invoke-RestMethod -Uri "http://localhost:$port/api/health" -Method Get -TimeoutSec 2 -ErrorAction Stop
        Write-Host "✅ API found on port $port" -ForegroundColor Green
        Write-Host "   Status: $($response.status)" -ForegroundColor Cyan
        $found = $true
        break
    } catch {
        # Port not active, continue
    }
}

if (-not $found) {
    Write-Host "⚠️ API not responding on common ports" -ForegroundColor Yellow
    Write-Host ""
    Write-Host "Check if application is running:" -ForegroundColor Cyan
    Write-Host "  Get-Process | Where-Object { `$_.ProcessName -like '*dotnet*' }" -ForegroundColor Gray
    Write-Host ""
    Write-Host "Or check all listening ports:" -ForegroundColor Cyan
    Write-Host "  netstat -ano | findstr LISTENING" -ForegroundColor Gray
}

Write-Host ""


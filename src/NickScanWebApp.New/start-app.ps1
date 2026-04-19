Write-Host "Starting NickScan MudBlazor WebApp..." -ForegroundColor Cyan
Write-Host ""

Write-Host "Building..." -ForegroundColor Yellow
dotnet build --verbosity minimal

if ($LASTEXITCODE -ne 0) {
    Write-Host "Build failed!" -ForegroundColor Red
    pause
    exit 1
}

Write-Host "Build successful!" -ForegroundColor Green
Write-Host ""
Write-Host "Starting application..." -ForegroundColor Yellow
Write-Host "URLs:" -ForegroundColor Cyan
Write-Host "  • HTTP:  http://localhost:5299" -ForegroundColor White
Write-Host "  • HTTPS: https://localhost:7142" -ForegroundColor White
Write-Host ""
Write-Host "Press Ctrl+C to stop" -ForegroundColor Gray
Write-Host ""

dotnet run --no-build


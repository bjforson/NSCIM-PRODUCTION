# Step 1: Apply EF Core Migrations to create schema on MSSQLSERVER

$ErrorActionPreference = "Continue"

Write-Host "Applying EF Core Migrations to MSSQLSERVER..." -ForegroundColor Cyan
Write-Host ""

# Update connection strings temporarily
$appsettingsPath = "src\NickScanCentralImagingPortal.API\appsettings.json"
if (Test-Path $appsettingsPath) {
    $content = Get-Content $appsettingsPath -Raw
    $content = $content -replace '127\.0\.0\.1,1433', '127.0.0.1,1433'
    $content = $content -replace 'localhost\\NS_CIS', '(local)'
    $content = $content -replace 'localhost\\.*?;', '(local);'
    Set-Content $appsettingsPath -Value $content -Encoding UTF8
    Write-Host "Updated connection strings temporarily" -ForegroundColor Yellow
}

Write-Host "Applying ApplicationDbContext migrations (NS_CIS)..." -ForegroundColor Cyan
dotnet ef database update --project src\NickScanCentralImagingPortal.Infrastructure --startup-project src\NickScanCentralImagingPortal.API --context ApplicationDbContext

Write-Host ""
Write-Host "Applying IcumDbContext migrations (ICUMS)..." -ForegroundColor Cyan
dotnet ef database update --project src\NickScanCentralImagingPortal.Infrastructure --startup-project src\NickScanCentralImagingPortal.API --context IcumDbContext

Write-Host ""
Write-Host "Applying IcumDownloadsDbContext migrations (ICUMS_Downloads)..." -ForegroundColor Cyan
dotnet ef database update --project src\NickScanCentralImagingPortal.Infrastructure --startup-project src\NickScanCentralImagingPortal.API --context IcumDownloadsDbContext

Write-Host ""
Write-Host "Migrations complete! Restore appsettings.json from backup if needed." -ForegroundColor Green


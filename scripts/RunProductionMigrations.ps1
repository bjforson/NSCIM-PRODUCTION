# Run Entity Framework Migrations on Production Databases
# Uses production appsettings.json from \\10.0.0.79\Shared\NSCIM_PRODUCTION

param(
    [string]$ProductionPath = "\\10.0.0.79\Shared\NSCIM_PRODUCTION"
)

$ErrorActionPreference = "Stop"

Write-Host "==================================================" -ForegroundColor Cyan
Write-Host "Production Database Migrations" -ForegroundColor Cyan
Write-Host "==================================================" -ForegroundColor Cyan
Write-Host ""
Write-Host "Production Path: $ProductionPath" -ForegroundColor White
Write-Host ""

# Check if production path exists
if (-not (Test-Path $ProductionPath)) {
    Write-Host "❌ Production path not found: $ProductionPath" -ForegroundColor Red
    exit 1
}

# Get project paths
$infrastructureProject = Join-Path $ProductionPath "src\NickScanCentralImagingPortal.Infrastructure\NickScanCentralImagingPortal.Infrastructure.csproj"
$apiProject = Join-Path $ProductionPath "src\NickScanCentralImagingPortal.API\NickScanCentralImagingPortal.API.csproj"

if (-not (Test-Path $infrastructureProject)) {
    Write-Host "❌ Infrastructure project not found: $infrastructureProject" -ForegroundColor Red
    exit 1
}

if (-not (Test-Path $apiProject)) {
    Write-Host "❌ API project not found: $apiProject" -ForegroundColor Red
    exit 1
}

Write-Host "✅ Project files found" -ForegroundColor Green
Write-Host ""

# Change to production directory
$originalLocation = Get-Location
Set-Location $ProductionPath

try {
    Write-Host "Step 1: Restoring NuGet packages..." -ForegroundColor Yellow
    dotnet restore $apiProject --verbosity quiet
    if ($LASTEXITCODE -ne 0) {
        Write-Host "❌ Package restore failed" -ForegroundColor Red
        exit 1
    }
    Write-Host "✅ Packages restored" -ForegroundColor Green
    Write-Host ""
    
    # Step 2: Run migrations for ApplicationDbContext (NS_CIS)
    Write-Host "Step 2: Running migrations for ApplicationDbContext (NS_CIS)..." -ForegroundColor Yellow
    Write-Host "   This will create all tables in the NS_CIS database" -ForegroundColor Gray
    Write-Host ""
    
    dotnet ef database update `
        --project $infrastructureProject `
        --startup-project $apiProject `
        --context ApplicationDbContext
    
    if ($LASTEXITCODE -ne 0) {
        Write-Host "❌ ApplicationDbContext migration failed" -ForegroundColor Red
        exit 1
    }
    
    Write-Host ""
    Write-Host "✅ ApplicationDbContext migrations applied (NS_CIS)" -ForegroundColor Green
    Write-Host ""
    
    # Step 3: Run migrations for IcumDbContext (ICUMS)
    Write-Host "Step 3: Running migrations for IcumDbContext (ICUMS)..." -ForegroundColor Yellow
    Write-Host "   This will create all tables in the ICUMS database" -ForegroundColor Gray
    Write-Host ""
    
    dotnet ef database update `
        --project $infrastructureProject `
        --startup-project $apiProject `
        --context IcumDbContext
    
    if ($LASTEXITCODE -ne 0) {
        Write-Host "❌ IcumDbContext migration failed" -ForegroundColor Red
        exit 1
    }
    
    Write-Host ""
    Write-Host "✅ IcumDbContext migrations applied (ICUMS)" -ForegroundColor Green
    Write-Host ""
    
    # Step 4: Run migrations for IcumDownloadsDbContext (ICUMS_Downloads)
    Write-Host "Step 4: Running migrations for IcumDownloadsDbContext (ICUMS_Downloads)..." -ForegroundColor Yellow
    Write-Host "   This will create all tables in the ICUMS_Downloads database" -ForegroundColor Gray
    Write-Host ""
    
    dotnet ef database update `
        --project $infrastructureProject `
        --startup-project $apiProject `
        --context IcumDownloadsDbContext
    
    if ($LASTEXITCODE -ne 0) {
        Write-Host "❌ IcumDownloadsDbContext migration failed" -ForegroundColor Red
        exit 1
    }
    
    Write-Host ""
    Write-Host "✅ IcumDownloadsDbContext migrations applied (ICUMS_Downloads)" -ForegroundColor Green
    Write-Host ""
    
    Write-Host "==================================================" -ForegroundColor Cyan
    Write-Host "Migration Summary" -ForegroundColor Cyan
    Write-Host "==================================================" -ForegroundColor Cyan
    Write-Host ""
    Write-Host "✅ All migrations completed successfully!" -ForegroundColor Green
    Write-Host ""
    Write-Host "Databases ready:" -ForegroundColor Yellow
    Write-Host "  ✅ NS_CIS - ApplicationDbContext" -ForegroundColor Green
    Write-Host "  ✅ ICUMS - IcumDbContext" -ForegroundColor Green
    Write-Host "  ✅ ICUMS_Downloads - IcumDownloadsDbContext" -ForegroundColor Green
    Write-Host ""
    Write-Host "Next Steps:" -ForegroundColor Yellow
    Write-Host "1. Verify database tables were created" -ForegroundColor White
    Write-Host "2. Test application connectivity to production databases" -ForegroundColor White
    Write-Host "3. Start production application" -ForegroundColor White
    Write-Host ""
    
} catch {
    Write-Host "❌ Error: $($_.Exception.Message)" -ForegroundColor Red
    Write-Host $_.Exception.StackTrace -ForegroundColor Red
    exit 1
} finally {
    Set-Location $originalLocation
}


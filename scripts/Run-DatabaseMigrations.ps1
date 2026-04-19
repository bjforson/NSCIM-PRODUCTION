# ================================================
# Run EF Core Database Migrations
# NickScan Central Imaging Portal - SQL Server 2014
# ================================================

param(
    [string]$ServerName = "127.0.0.1,1433",
    [switch]$SkipVerification
)

$ErrorActionPreference = "Stop"

Write-Host "========================================" -ForegroundColor Cyan
Write-Host "EF Core Database Migrations" -ForegroundColor Cyan
Write-Host "SQL Server 2014 Setup" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""

# Get the project root directory
$projectRoot = Split-Path -Parent $PSScriptRoot
$projectRoot = Split-Path -Parent $projectRoot

$infrastructureProject = Join-Path $projectRoot "src\NickScanCentralImagingPortal.Infrastructure\NickScanCentralImagingPortal.Infrastructure.csproj"
$apiProject = Join-Path $projectRoot "src\NickScanCentralImagingPortal.API\NickScanCentralImagingPortal.API.csproj"

# Verify projects exist
if (-not (Test-Path $infrastructureProject)) {
    Write-Host "❌ ERROR: Infrastructure project not found at: $infrastructureProject" -ForegroundColor Red
    exit 1
}

if (-not (Test-Path $apiProject)) {
    Write-Host "❌ ERROR: API project not found at: $apiProject" -ForegroundColor Red
    exit 1
}

Write-Host "✓ Projects found" -ForegroundColor Green
Write-Host ""

# Change to project root
Push-Location $projectRoot

try {
    # Step 1: Verify .NET EF Tools
    Write-Host "Step 1: Checking EF Core tools..." -ForegroundColor Yellow
    $efTools = dotnet ef --version 2>&1
    if ($LASTEXITCODE -ne 0) {
        Write-Host "⚠️  EF Core tools not found. Installing..." -ForegroundColor Yellow
        dotnet tool install --global dotnet-ef
        if ($LASTEXITCODE -ne 0) {
            Write-Host "❌ ERROR: Failed to install EF Core tools" -ForegroundColor Red
            exit 1
        }
    }
    Write-Host "✓ EF Core tools available" -ForegroundColor Green
    Write-Host ""

    # Step 2: Verify connection strings
    if (-not $SkipVerification) {
        Write-Host "Step 2: Verifying connection strings..." -ForegroundColor Yellow
        $appsettingsPath = Join-Path $projectRoot "src\NickScanCentralImagingPortal.API\appsettings.json"
        
        if (Test-Path $appsettingsPath) {
            $appsettings = Get-Content $appsettingsPath | ConvertFrom-Json
            $connections = @(
                $appsettings.ConnectionStrings.NS_CIS_Connection,
                $appsettings.ConnectionStrings.ICUMS_Connection,
                $appsettings.ConnectionStrings.ICUMS_Downloads_Connection
            )
            
            foreach ($conn in $connections) {
                if ($conn -notmatch $ServerName) {
                    Write-Host "⚠️  WARNING: Connection string may not match server: $ServerName" -ForegroundColor Yellow
                    Write-Host "   Connection: $($conn.Substring(0, [Math]::Min(80, $conn.Length)))..." -ForegroundColor Gray
                }
            }
            Write-Host "✓ Connection strings found" -ForegroundColor Green
        } else {
            Write-Host "⚠️  WARNING: appsettings.json not found" -ForegroundColor Yellow
        }
        Write-Host ""
    }

    # Step 3: Run migrations for ApplicationDbContext
    Write-Host "Step 3: Running migrations for ApplicationDbContext (NS_CIS)..." -ForegroundColor Yellow
    Write-Host ""
    
    dotnet ef database update `
        --project $infrastructureProject `
        --startup-project $apiProject `
        --context ApplicationDbContext `
        --verbose
    
    if ($LASTEXITCODE -ne 0) {
        Write-Host "❌ ERROR: Failed to update ApplicationDbContext" -ForegroundColor Red
        exit 1
    }
    
    Write-Host ""
    Write-Host "✓ ApplicationDbContext migrations applied" -ForegroundColor Green
    Write-Host ""

    # Step 4: Run migrations for IcumDbContext
    Write-Host "Step 4: Running migrations for IcumDbContext (ICUMS)..." -ForegroundColor Yellow
    Write-Host ""
    
    dotnet ef database update `
        --project $infrastructureProject `
        --startup-project $apiProject `
        --context IcumDbContext `
        --verbose
    
    if ($LASTEXITCODE -ne 0) {
        Write-Host "❌ ERROR: Failed to update IcumDbContext" -ForegroundColor Red
        exit 1
    }
    
    Write-Host ""
    Write-Host "✓ IcumDbContext migrations applied" -ForegroundColor Green
    Write-Host ""

    # Step 5: Run migrations for IcumDownloadsDbContext
    Write-Host "Step 5: Running migrations for IcumDownloadsDbContext (ICUMS_Downloads)..." -ForegroundColor Yellow
    Write-Host ""
    
    dotnet ef database update `
        --project $infrastructureProject `
        --startup-project $apiProject `
        --context IcumDownloadsDbContext `
        --verbose
    
    if ($LASTEXITCODE -ne 0) {
        Write-Host "❌ ERROR: Failed to update IcumDownloadsDbContext" -ForegroundColor Red
        exit 1
    }
    
    Write-Host ""
    Write-Host "✓ IcumDownloadsDbContext migrations applied" -ForegroundColor Green
    Write-Host ""

    # Success
    Write-Host "========================================" -ForegroundColor Green
    Write-Host "✅ All Migrations Applied Successfully!" -ForegroundColor Green
    Write-Host "========================================" -ForegroundColor Green
    Write-Host ""
    Write-Host "Next Steps:" -ForegroundColor Cyan
    Write-Host "1. Verify databases using: .\scripts\Verify-DatabaseSetup.ps1" -ForegroundColor White
    Write-Host "2. Start your application" -ForegroundColor White
    Write-Host ""

} catch {
    Write-Host ""
    Write-Host "❌ ERROR: $($_.Exception.Message)" -ForegroundColor Red
    Write-Host $_.ScriptStackTrace -ForegroundColor Gray
    exit 1
} finally {
    Pop-Location
}


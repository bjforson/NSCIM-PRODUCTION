# Apply FixAnalysisGroupsUniqueConstraint Migration
# This script applies the database constraint fix using either EF Core migration or manual SQL

param(
    [Parameter(Mandatory=$false)]
    [ValidateSet("EFCore", "Manual")]
    [string]$Method = "Manual"
)

# Load connection string from appsettings.json
$appsettingsPath = "src\NickScanCentralImagingPortal.API\appsettings.json"
if (-not (Test-Path $appsettingsPath)) {
    Write-Host "Error: appsettings.json not found" -ForegroundColor Red
    exit 1
}

$appsettings = Get-Content $appsettingsPath | ConvertFrom-Json
$connString = $appsettings.ConnectionStrings.NS_CIS_Connection

# Parse connection string
$builder = New-Object System.Data.SqlClient.SqlConnectionStringBuilder($connString)
$server = $builder.DataSource
$database = $builder.InitialCatalog
$serverParts = $server -split ","
$serverName = $serverParts[0]
$port = if ($serverParts.Length -gt 1) { $serverParts[1] } else { "1433" }

$sqlcmdPath = "C:\Program Files\Microsoft SQL Server\Client SDK\ODBC\170\Tools\Binn\SQLCMD.EXE"
if (-not (Test-Path $sqlcmdPath)) {
    Write-Host "Error: sqlcmd.exe not found" -ForegroundColor Red
    exit 1
}

Write-Host "`n=== APPLYING FIX: AnalysisGroups Unique Constraint ===" -ForegroundColor Cyan
Write-Host "Method: $Method" -ForegroundColor Yellow
Write-Host ""

if ($Method -eq "EFCore") {
    Write-Host "Applying via EF Core migration..." -ForegroundColor Yellow
    Write-Host "Note: API must be stopped before running this" -ForegroundColor Yellow
    Write-Host ""
    
    dotnet ef database update --context ApplicationDbContext --project "src\NickScanCentralImagingPortal.Infrastructure\NickScanCentralImagingPortal.Infrastructure.csproj" --startup-project "src\NickScanCentralImagingPortal.API\NickScanCentralImagingPortal.API.csproj"
    
    if ($LASTEXITCODE -eq 0) {
        Write-Host "`n✅ Migration applied successfully!" -ForegroundColor Green
    } else {
        Write-Host "`n❌ Migration failed. Check errors above." -ForegroundColor Red
        Write-Host "You can try the Manual method instead." -ForegroundColor Yellow
    }
} else {
    Write-Host "Applying via manual SQL script..." -ForegroundColor Yellow
    Write-Host ""
    
    $sqlScript = "database_migrations\FixAnalysisGroupsUniqueConstraint.sql"
    if (-not (Test-Path $sqlScript)) {
        Write-Host "Error: SQL script not found at $sqlScript" -ForegroundColor Red
        exit 1
    }
    
    Write-Host "Executing SQL script: $sqlScript" -ForegroundColor Cyan
    Write-Host "Server: $serverName,$port" -ForegroundColor DarkCyan
    Write-Host "Database: $database" -ForegroundColor DarkCyan
    Write-Host ""
    
    # Read and execute SQL script
    $sqlContent = Get-Content $sqlScript -Raw
    
    Write-Host "Running migration script..." -ForegroundColor Yellow
    & $sqlcmdPath -S "$serverName,$port" -d $database -E -Q $sqlContent -W
    
    if ($LASTEXITCODE -eq 0) {
        Write-Host "`n✅ Migration applied successfully!" -ForegroundColor Green
    } else {
        Write-Host "`n❌ Migration failed. Check errors above." -ForegroundColor Red
    }
}

Write-Host "`nDone!" -ForegroundColor Green


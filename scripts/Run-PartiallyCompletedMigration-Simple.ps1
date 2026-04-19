# Simple script to run AddPartiallyCompletedSupport migration using sqlcmd
# Reads connection string from appsettings.json

$appsettingsPath = "src\NickScanCentralImagingPortal.API\appsettings.json"
$sqlScriptPath = "database_migrations\AddPartiallyCompletedSupport.sql"

Write-Host "========================================" -ForegroundColor Cyan
Write-Host "PartiallyCompleted Migration Script" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""

# Read connection string
if (-not (Test-Path $appsettingsPath)) {
    Write-Host "Error: appsettings.json not found" -ForegroundColor Red
    exit 1
}

$appsettings = Get-Content $appsettingsPath | ConvertFrom-Json
$connString = $appsettings.ConnectionStrings.NS_CIS_Connection

if ([string]::IsNullOrEmpty($connString)) {
    Write-Host "Error: NS_CIS_Connection not found" -ForegroundColor Red
    exit 1
}

# Parse connection string
$builder = New-Object System.Data.SqlClient.SqlConnectionStringBuilder($connString)
$server = $builder.DataSource
$database = $builder.InitialCatalog

Write-Host "Server: $server" -ForegroundColor Gray
Write-Host "Database: $database" -ForegroundColor Gray
Write-Host "SQL Script: $sqlScriptPath" -ForegroundColor Gray
Write-Host ""

# Check if sqlcmd is available
$sqlcmdPath = (Get-Command sqlcmd -ErrorAction SilentlyContinue).Source
if (-not $sqlcmdPath) {
    Write-Host "Error: sqlcmd not found. Please install SQL Server Command Line Utilities" -ForegroundColor Red
    Write-Host "Or use SQL Server Management Studio to run: $sqlScriptPath" -ForegroundColor Yellow
    exit 1
}

# Confirm
$confirm = Read-Host "Proceed with migration? (Y/N)"
if ($confirm -ne "Y" -and $confirm -ne "y") {
    Write-Host "Cancelled" -ForegroundColor Yellow
    exit 0
}

Write-Host ""
Write-Host "Executing migration..." -ForegroundColor Green
Write-Host ""

# Build sqlcmd command
# Extract server and instance from DataSource
$serverParts = $server -split ","
$serverName = $serverParts[0]
$port = if ($serverParts.Length -gt 1) { $serverParts[1] } else { "1433" }

# Use Trusted Connection (Windows Authentication)
$sqlcmdArgs = @(
    "-S", "$serverName,$port",
    "-d", $database,
    "-i", $sqlScriptPath,
    "-E"  # Trusted connection
)

try {
    & sqlcmd $sqlcmdArgs
    
    if ($LASTEXITCODE -eq 0) {
        Write-Host ""
        Write-Host "========================================" -ForegroundColor Cyan
        Write-Host "Migration completed successfully!" -ForegroundColor Green
        Write-Host "========================================" -ForegroundColor Cyan
    } else {
        Write-Host ""
        Write-Host "Migration completed with exit code: $LASTEXITCODE" -ForegroundColor Yellow
        Write-Host "Please check the output above for any errors" -ForegroundColor Yellow
    }
} catch {
    Write-Host ""
    Write-Host "Error executing migration:" -ForegroundColor Red
    Write-Host $_.Exception.Message -ForegroundColor Red
    exit 1
}


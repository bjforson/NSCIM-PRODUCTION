# Script to run AddPartiallyCompletedSupport migration
# Reads connection string from appsettings.json and executes the SQL script

param(
    [string]$ConnectionString = "",
    [string]$SqlScriptPath = "database_migrations\AddPartiallyCompletedSupport.sql"
)

Write-Host "========================================" -ForegroundColor Cyan
Write-Host "PartiallyCompleted Migration Script" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""

# Check if SQL Server module is available
if (-not (Get-Module -ListAvailable -Name SqlServer)) {
    Write-Host "SqlServer PowerShell module not found. Installing..." -ForegroundColor Yellow
    Install-Module -Name SqlServer -Scope CurrentUser -Force -AllowClobber
    Import-Module SqlServer
}

# Get connection string from appsettings.json if not provided
if ([string]::IsNullOrEmpty($ConnectionString)) {
    Write-Host "Reading connection string from appsettings.json..." -ForegroundColor Yellow
    
    $appsettingsPath = "src\NickScanCentralImagingPortal.API\appsettings.json"
    if (-not (Test-Path $appsettingsPath)) {
        Write-Host "Error: appsettings.json not found at $appsettingsPath" -ForegroundColor Red
        exit 1
    }
    
    $appsettings = Get-Content $appsettingsPath | ConvertFrom-Json
    $ConnectionString = $appsettings.ConnectionStrings.NS_CIS_Connection
    
    if ([string]::IsNullOrEmpty($ConnectionString)) {
        Write-Host "Error: NS_CIS_Connection not found in appsettings.json" -ForegroundColor Red
        exit 1
    }
    
    Write-Host "Connection string found" -ForegroundColor Green
}

# Check if SQL script exists
if (-not (Test-Path $SqlScriptPath)) {
    Write-Host "Error: SQL script not found at $SqlScriptPath" -ForegroundColor Red
    exit 1
}

Write-Host ""
Write-Host "SQL Script: $SqlScriptPath" -ForegroundColor Cyan
Write-Host "Database: NS_CIS" -ForegroundColor Cyan
Write-Host ""

# Parse connection string to get server and database
$builder = New-Object System.Data.SqlClient.SqlConnectionStringBuilder($ConnectionString)
$server = $builder.DataSource
$database = $builder.InitialCatalog

Write-Host "Server: $server" -ForegroundColor Gray
Write-Host "Database: $database" -ForegroundColor Gray
Write-Host ""

# Confirm execution
$confirm = Read-Host "Do you want to proceed with the migration? (Y/N)"
if ($confirm -ne "Y" -and $confirm -ne "y") {
    Write-Host "Migration cancelled by user" -ForegroundColor Yellow
    exit 0
}

Write-Host ""
Write-Host "Executing migration..." -ForegroundColor Green
Write-Host ""

try {
    # Execute SQL script
    $result = Invoke-Sqlcmd -ConnectionString $ConnectionString -InputFile $SqlScriptPath -Verbose
    
    Write-Host ""
    Write-Host "Migration completed successfully!" -ForegroundColor Green
    Write-Host ""
    
    # Display any output messages
    if ($result) {
        Write-Host "Migration output:" -ForegroundColor Cyan
        $result | ForEach-Object {
            Write-Host "  $_" -ForegroundColor Gray
        }
    }
    
    Write-Host ""
    Write-Host "========================================" -ForegroundColor Cyan
    Write-Host "All columns have been added successfully" -ForegroundColor Green
    Write-Host "========================================" -ForegroundColor Cyan
    
} catch {
    Write-Host ""
    Write-Host "Error executing migration:" -ForegroundColor Red
    Write-Host $_.Exception.Message -ForegroundColor Red
    Write-Host ""
    Write-Host "Stack trace:" -ForegroundColor Yellow
    Write-Host $_.ScriptStackTrace -ForegroundColor Yellow
    exit 1
}


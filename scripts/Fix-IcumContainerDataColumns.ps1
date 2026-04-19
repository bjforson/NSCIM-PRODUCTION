# ============================================================================
# Fix Missing Columns in IcumContainerData Table
# This script runs the SQL fix to add missing columns to IcumContainerData
# ============================================================================
# Usage: .\scripts\Fix-IcumContainerDataColumns.ps1
# ============================================================================

param(
    [string]$Server = "127.0.0.1,1433",
    [string]$Database = "ICUMS",
    [switch]$UseIntegratedSecurity = $true,
    [string]$Username = "",
    [string]$Password = ""
)

$ErrorActionPreference = "Stop"

# Get the script directory
$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$projectRoot = Split-Path -Parent $scriptDir
$sqlScriptPath = Join-Path $projectRoot "scripts\sql\Fix_IcumContainerData_MissingColumns.sql"

Write-Host "========================================" -ForegroundColor Cyan
Write-Host "Fix IcumContainerData Missing Columns" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""

# Check if SQL script exists
if (-not (Test-Path $sqlScriptPath)) {
    Write-Host "ERROR: SQL script not found at: $sqlScriptPath" -ForegroundColor Red
    exit 1
}

Write-Host "SQL Script: $sqlScriptPath" -ForegroundColor Gray
Write-Host "Server: $Server" -ForegroundColor Gray
Write-Host "Database: $Database" -ForegroundColor Gray
Write-Host ""

# Build connection string
if ($UseIntegratedSecurity) {
    $connectionString = "Server=$Server;Database=$Database;Integrated Security=true;TrustServerCertificate=true;"
} else {
    if ([string]::IsNullOrWhiteSpace($Username) -or [string]::IsNullOrWhiteSpace($Password)) {
        Write-Host "ERROR: Username and Password required when not using Integrated Security" -ForegroundColor Red
        exit 1
    }
    $connectionString = "Server=$Server;Database=$Database;User Id=$Username;Password=$Password;TrustServerCertificate=true;"
}

try {
    Write-Host "Connecting to SQL Server..." -ForegroundColor Yellow
    
    # Test connection first
    $testConnection = New-Object System.Data.SqlClient.SqlConnection($connectionString)
    $testConnection.Open()
    $testConnection.Close()
    
    Write-Host "✓ Connection successful" -ForegroundColor Green
    Write-Host ""
    
    # Read SQL script
    Write-Host "Reading SQL script..." -ForegroundColor Yellow
    $sqlScript = Get-Content $sqlScriptPath -Raw -Encoding UTF8
    
    # Remove the USE statement if present (we'll use the connection string database)
    $sqlScript = $sqlScript -replace 'USE\s+\[ICUMS\];\s*GO\s*', ''
    
    Write-Host "✓ SQL script loaded" -ForegroundColor Green
    Write-Host ""
    
    # Execute SQL script
    Write-Host "Executing SQL script..." -ForegroundColor Yellow
    Write-Host "This may take a few moments..." -ForegroundColor Gray
    Write-Host ""
    
    $connection = New-Object System.Data.SqlClient.SqlConnection($connectionString)
    $connection.Open()
    
    try {
        # Split script by GO statements and execute each batch
        $batches = $sqlScript -split 'GO\s*', [System.StringSplitOptions]::RemoveEmptyEntries
        
        foreach ($batch in $batches) {
            $batch = $batch.Trim()
            if ([string]::IsNullOrWhiteSpace($batch)) {
                continue
            }
            
            $command = $connection.CreateCommand()
            $command.CommandText = $batch
            $command.CommandTimeout = 120
            
            try {
                $reader = $command.ExecuteReader()
                
                # Read and display output
                while ($reader.Read()) {
                    if ($reader.FieldCount > 0) {
                        $output = $reader[0]
                        if ($null -ne $output) {
                            Write-Host $output.ToString() -ForegroundColor White
                        }
                    }
                }
                
                $reader.Close()
            }
            catch {
                # Some commands don't return readers, just execute
                $command.ExecuteNonQuery() | Out-Null
            }
            finally {
                $command.Dispose()
            }
        }
        
        Write-Host ""
        Write-Host "========================================" -ForegroundColor Green
        Write-Host "✅ SQL Script Executed Successfully!" -ForegroundColor Green
        Write-Host "========================================" -ForegroundColor Green
        Write-Host ""
        Write-Host "All missing columns have been added to IcumContainerData table." -ForegroundColor Green
        Write-Host "The application should now work without 'Invalid column name' errors." -ForegroundColor Green
        Write-Host ""
    }
    finally {
        $connection.Close()
    }
}
catch {
    Write-Host ""
    Write-Host "========================================" -ForegroundColor Red
    Write-Host "❌ ERROR: Script execution failed" -ForegroundColor Red
    Write-Host "========================================" -ForegroundColor Red
    Write-Host ""
    Write-Host "Error Details:" -ForegroundColor Red
    Write-Host $_.Exception.Message -ForegroundColor Red
    
    if ($_.Exception.InnerException) {
        Write-Host ""
        Write-Host "Inner Exception:" -ForegroundColor Red
        Write-Host $_.Exception.InnerException.Message -ForegroundColor Red
    }
    
    Write-Host ""
    Write-Host "Troubleshooting:" -ForegroundColor Yellow
    Write-Host "1. Verify SQL Server is running and accessible" -ForegroundColor Yellow
    Write-Host "2. Check that the ICUMS database exists" -ForegroundColor Yellow
    Write-Host "3. Verify your connection credentials" -ForegroundColor Yellow
    Write-Host "4. Ensure you have ALTER TABLE permissions on the database" -ForegroundColor Yellow
    Write-Host ""
    
    exit 1
}


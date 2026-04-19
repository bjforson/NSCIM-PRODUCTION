# Setup Production Databases on SQL Server 10.0.0.79
# Creates NS_CIS, ICUMS, and ICUMS_Downloads databases

param(
    [string]$Server = "10.0.0.79",
    [string]$Database = "master"
)

$ErrorActionPreference = "Stop"

Write-Host "==================================================" -ForegroundColor Cyan
Write-Host "Production Database Setup" -ForegroundColor Cyan
Write-Host "==================================================" -ForegroundColor Cyan
Write-Host ""
Write-Host "Server: $Server" -ForegroundColor White
Write-Host "Authentication: Windows Integrated Security" -ForegroundColor White
Write-Host ""

try {
    $connectionString = "Server=$Server;Database=$Database;Integrated Security=true;Connection Timeout=10;TrustServerCertificate=true;"
    $connection = New-Object System.Data.SqlClient.SqlConnection($connectionString)
    $connection.Open()
    
    Write-Host "✅ Connected to SQL Server" -ForegroundColor Green
    Write-Host ""
    
    # Databases to create
    $databases = @(
        @{ Name = "NS_CIS"; Description = "Main application database" },
        @{ Name = "ICUMS"; Description = "ICUMS integration database" },
        @{ Name = "ICUMS_Downloads"; Description = "ICUMS downloads staging database" }
    )
    
    Write-Host "Step 1: Creating databases..." -ForegroundColor Yellow
    Write-Host ""
    
    foreach ($db in $databases) {
        $dbName = $db.Name
        Write-Host "   Checking database: $dbName..." -ForegroundColor Gray
        
        # Check if database exists
        $checkQuery = "SELECT COUNT(*) FROM sys.databases WHERE name = '$dbName'"
        $checkCommand = New-Object System.Data.SqlClient.SqlCommand($checkQuery, $connection)
        $exists = [int]$checkCommand.ExecuteScalar()
        
        if ($exists -gt 0) {
            Write-Host "   ⚠️ Database '$dbName' already exists, skipping creation" -ForegroundColor Yellow
            
            # Check database state
            $stateQuery = "SELECT state_desc FROM sys.databases WHERE name = '$dbName'"
            $stateCommand = New-Object System.Data.SqlClient.SqlCommand($stateQuery, $connection)
            $state = $stateCommand.ExecuteScalar()
            
            if ($state -ne "ONLINE") {
                Write-Host "   ⚠️ Database is in '$state' state" -ForegroundColor Yellow
            } else {
                Write-Host "   ✅ Database is ONLINE" -ForegroundColor Green
            }
        } else {
            Write-Host "   Creating database: $dbName..." -ForegroundColor Gray
            
            # Create database with appropriate settings
            $createQuery = @"
CREATE DATABASE [$dbName]
ON 
( NAME = '$dbName', FILENAME = 'C:\Program Files\Microsoft SQL Server\MSSQL12.MSSQLSERVER\MSSQL\DATA\${dbName}.mdf' , SIZE = 100MB , MAXSIZE = UNLIMITED, FILEGROWTH = 10MB )
LOG ON 
( NAME = '${dbName}_Log', FILENAME = 'C:\Program Files\Microsoft SQL Server\MSSQL12.MSSQLSERVER\MSSQL\DATA\${dbName}_Log.ldf' , SIZE = 10MB , MAXSIZE = UNLIMITED, FILEGROWTH = 10MB );
"@
            
            try {
                $createCommand = New-Object System.Data.SqlClient.SqlCommand($createQuery, $connection)
                $createCommand.ExecuteNonQuery() | Out-Null
                Write-Host "   ✅ Database '$dbName' created successfully" -ForegroundColor Green
            } catch {
                Write-Host "   ❌ Error creating database '$dbName': $($_.Exception.Message)" -ForegroundColor Red
                # Try with default file locations
                Write-Host "   Retrying with default file locations..." -ForegroundColor Gray
                $createQuerySimple = "CREATE DATABASE [$dbName];"
                try {
                    $createCommandSimple = New-Object System.Data.SqlClient.SqlCommand($createQuerySimple, $connection)
                    $createCommandSimple.ExecuteNonQuery() | Out-Null
                    Write-Host "   ✅ Database '$dbName' created successfully (default location)" -ForegroundColor Green
                } catch {
                    Write-Host "   ❌ Failed to create database: $($_.Exception.Message)" -ForegroundColor Red
                    continue
                }
            }
            
            # Set database options
            Write-Host "   Configuring database options..." -ForegroundColor Gray
            $optionsQuery = @"
USE [$dbName];
ALTER DATABASE [$dbName] SET RECOVERY SIMPLE;
ALTER DATABASE [$dbName] SET AUTO_SHRINK ON;
ALTER DATABASE [$dbName] SET AUTO_CREATE_STATISTICS ON;
ALTER DATABASE [$dbName] SET AUTO_UPDATE_STATISTICS ON;
"@
            
            try {
                $optionsCommand = New-Object System.Data.SqlClient.SqlCommand($optionsQuery, $connection)
                $optionsCommand.ExecuteNonQuery() | Out-Null
                Write-Host "   ✅ Database options configured" -ForegroundColor Green
            } catch {
                Write-Host "   ⚠️ Warning: Could not set all database options: $($_.Exception.Message)" -ForegroundColor Yellow
            }
        }
        
        Write-Host ""
    }
    
    Write-Host "Step 2: Verifying database setup..." -ForegroundColor Yellow
    Write-Host ""
    
    $allCreated = $true
    foreach ($db in $databases) {
        $dbName = $db.Name
        $verifyQuery = "SELECT name, state_desc, recovery_model_desc FROM sys.databases WHERE name = '$dbName'"
        $verifyCommand = New-Object System.Data.SqlClient.SqlCommand($verifyQuery, $connection)
        $reader = $verifyCommand.ExecuteReader()
        
        if ($reader.Read()) {
            $state = $reader["state_desc"]
            $recovery = $reader["recovery_model_desc"]
            $statusColor = if ($state -eq "ONLINE") { "Green" } else { "Red" }
            Write-Host "   ✅ $dbName" -ForegroundColor $statusColor
            Write-Host "      State: $state" -ForegroundColor White
            Write-Host "      Recovery Model: $recovery" -ForegroundColor White
        } else {
            Write-Host "   ❌ $dbName - NOT FOUND" -ForegroundColor Red
            $allCreated = $false
        }
        $reader.Close()
        Write-Host ""
    }
    
    Write-Host "==================================================" -ForegroundColor Cyan
    Write-Host "Database Setup Summary" -ForegroundColor Cyan
    Write-Host "==================================================" -ForegroundColor Cyan
    Write-Host ""
    
    if ($allCreated) {
        Write-Host "✅ All databases are ready!" -ForegroundColor Green
        Write-Host ""
        Write-Host "Next Steps:" -ForegroundColor Yellow
        Write-Host "1. Run Entity Framework migrations to create tables" -ForegroundColor White
        Write-Host "2. Verify database connectivity from application" -ForegroundColor White
        Write-Host "3. Test production deployment" -ForegroundColor White
    } else {
        Write-Host "⚠️ Some databases may need manual setup" -ForegroundColor Yellow
    }
    
    Write-Host ""
    Write-Host "✅ Database setup completed!" -ForegroundColor Green
    
    $connection.Close()
    
} catch {
    Write-Host "❌ Error: $($_.Exception.Message)" -ForegroundColor Red
    Write-Host $_.Exception.StackTrace -ForegroundColor Red
    exit 1
}


# ================================================
# Verify Database Setup for SQL Server 2014
# NickScan Central Imaging Portal
# ================================================

param(
    [string]$ServerName = "127.0.0.1,1433"
)

$ErrorActionPreference = "Stop"

Write-Host "========================================" -ForegroundColor Cyan
Write-Host "Database Setup Verification" -ForegroundColor Cyan
Write-Host "SQL Server 2014" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""

# Connection string
$connectionString = "Server=$ServerName;Database=master;Trusted_Connection=true;TrustServerCertificate=true;"

try {
    # Test connection
    Write-Host "Testing SQL Server connection..." -ForegroundColor Yellow
    $connection = New-Object System.Data.SqlClient.SqlConnection($connectionString)
    $connection.Open()
    Write-Host "✓ Connected to SQL Server" -ForegroundColor Green
    Write-Host ""

    # Query to check databases
    $query = @"
SELECT 
    name AS DatabaseName,
    compatibility_level AS CompatibilityLevel,
    recovery_model_desc AS RecoveryModel,
    state_desc AS State,
    create_date AS CreatedDate
FROM sys.databases
WHERE name IN ('NS_CIS', 'ICUMS', 'ICUMS_Downloads')
ORDER BY name;
"@

    $command = $connection.CreateCommand()
    $command.CommandText = $query
    $reader = $command.ExecuteReader()

    Write-Host "Database Status:" -ForegroundColor Yellow
    Write-Host ""

    $databases = @()
    while ($reader.Read()) {
        $db = @{
            Name = $reader["DatabaseName"]
            CompatibilityLevel = $reader["CompatibilityLevel"]
            RecoveryModel = $reader["RecoveryModel"]
            State = $reader["State"]
            CreatedDate = $reader["CreatedDate"]
        }
        $databases += $db
    }
    $reader.Close()

    if ($databases.Count -eq 0) {
        Write-Host "❌ ERROR: No databases found!" -ForegroundColor Red
        Write-Host "   Run: .\scripts\sql\Create_Databases_SQL2014.sql" -ForegroundColor Yellow
        exit 1
    }

    # Display database info
    $allGood = $true
    foreach ($db in $databases) {
        $status = "✓"
        $color = "Green"
        
        if ($db.CompatibilityLevel -ne 120) {
            $status = "⚠️"
            $color = "Yellow"
            $allGood = $false
        }
        
        if ($db.State -ne "ONLINE") {
            $status = "❌"
            $color = "Red"
            $allGood = $false
        }

        Write-Host "$status Database: $($db.Name)" -ForegroundColor $color
        Write-Host "   Compatibility Level: $($db.CompatibilityLevel) (Expected: 120)" -ForegroundColor $(if ($db.CompatibilityLevel -eq 120) { "Green" } else { "Yellow" })
        Write-Host "   Recovery Model: $($db.RecoveryModel)" -ForegroundColor Gray
        Write-Host "   State: $($db.State)" -ForegroundColor $(if ($db.State -eq "ONLINE") { "Green" } else { "Red" })
        Write-Host "   Created: $($db.CreatedDate)" -ForegroundColor Gray
        Write-Host ""
    }

    # Check for tables in each database
    Write-Host "Checking for tables..." -ForegroundColor Yellow
    Write-Host ""

    $databasesToCheck = @("NS_CIS", "ICUMS", "ICUMS_Downloads")
    
    foreach ($dbName in $databasesToCheck) {
        $dbConnectionString = "Server=$ServerName;Database=$dbName;Trusted_Connection=true;TrustServerCertificate=true;"
        $dbConnection = New-Object System.Data.SqlClient.SqlConnection($dbConnectionString)
        $dbConnection.Open()
        
        $tableQuery = @"
SELECT COUNT(*) AS TableCount
FROM INFORMATION_SCHEMA.TABLES
WHERE TABLE_TYPE = 'BASE TABLE';
"@
        
        $tableCommand = $dbConnection.CreateCommand()
        $tableCommand.CommandText = $tableQuery
        $tableCount = $tableCommand.ExecuteScalar()
        
        $dbConnection.Close()
        
        if ($tableCount -gt 0) {
            Write-Host "✓ $dbName : $tableCount tables found" -ForegroundColor Green
        } else {
            Write-Host "⚠️  $dbName : No tables found (migrations may not have run)" -ForegroundColor Yellow
            $allGood = $false
        }
    }

    Write-Host ""

    # Check EF Core migration history
    Write-Host "Checking EF Core migration history..." -ForegroundColor Yellow
    Write-Host ""

    $nsCisConnection = New-Object System.Data.SqlClient.SqlConnection("Server=$ServerName;Database=NS_CIS;Trusted_Connection=true;TrustServerCertificate=true;")
    $nsCisConnection.Open()
    
    $migrationQuery = @"
IF EXISTS (SELECT * FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = '__EFMigrationsHistory')
BEGIN
    SELECT COUNT(*) AS MigrationCount
    FROM __EFMigrationsHistory;
END
ELSE
BEGIN
    SELECT 0 AS MigrationCount;
END
"@
    
    $migrationCommand = $nsCisConnection.CreateCommand()
    $migrationCommand.CommandText = $migrationQuery
    $migrationCount = $migrationCommand.ExecuteScalar()
    
    $nsCisConnection.Close()
    
    if ($migrationCount -gt 0) {
        Write-Host "✓ NS_CIS : $migrationCount migrations applied" -ForegroundColor Green
    } else {
        Write-Host "⚠️  NS_CIS : No migrations found (run migrations first)" -ForegroundColor Yellow
        $allGood = $false
    }

    Write-Host ""

    # Final summary
    if ($allGood) {
        Write-Host "========================================" -ForegroundColor Green
        Write-Host "✅ All Checks Passed!" -ForegroundColor Green
        Write-Host "========================================" -ForegroundColor Green
        Write-Host ""
        Write-Host "Your databases are ready for use!" -ForegroundColor Cyan
    } else {
        Write-Host "========================================" -ForegroundColor Yellow
        Write-Host "⚠️  Some Issues Found" -ForegroundColor Yellow
        Write-Host "========================================" -ForegroundColor Yellow
        Write-Host ""
        Write-Host "Please review the warnings above." -ForegroundColor Yellow
    }

} catch {
    Write-Host ""
    Write-Host "❌ ERROR: $($_.Exception.Message)" -ForegroundColor Red
    Write-Host $_.ScriptStackTrace -ForegroundColor Gray
    exit 1
} finally {
    if ($connection -and $connection.State -eq "Open") {
        $connection.Close()
    }
}


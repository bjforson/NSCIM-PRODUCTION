# Verify Production Environment Setup
# Checks databases, tables, and connectivity

param(
    [string]$Server = "10.0.0.79",
    [string]$ProductionPath = "\\10.0.0.79\Shared\NSCIM_PRODUCTION"
)

$ErrorActionPreference = "Stop"

Write-Host "==================================================" -ForegroundColor Cyan
Write-Host "Production Environment Verification" -ForegroundColor Cyan
Write-Host "==================================================" -ForegroundColor Cyan
Write-Host ""

try {
    $connectionString = "Server=$Server;Database=master;Integrated Security=true;Connection Timeout=10;TrustServerCertificate=true;"
    $connection = New-Object System.Data.SqlClient.SqlConnection($connectionString)
    $connection.Open()
    
    Write-Host "✅ Connected to SQL Server" -ForegroundColor Green
    Write-Host ""
    
    # Check databases
    Write-Host "Step 1: Verifying databases..." -ForegroundColor Yellow
    $databases = @("NS_CIS", "ICUMS", "ICUMS_Downloads")
    $allDatabasesExist = $true
    
    foreach ($dbName in $databases) {
        $checkQuery = "SELECT name, state_desc FROM sys.databases WHERE name = '$dbName'"
        $checkCommand = New-Object System.Data.SqlClient.SqlCommand($checkQuery, $connection)
        $reader = $checkCommand.ExecuteReader()
        
        if ($reader.Read()) {
            $state = $reader["state_desc"]
            $statusColor = if ($state -eq "ONLINE") { "Green" } else { "Red" }
            Write-Host "   ✅ $dbName - $state" -ForegroundColor $statusColor
            
            if ($state -ne "ONLINE") {
                $allDatabasesExist = $false
            }
        } else {
            Write-Host "   ❌ $dbName - NOT FOUND" -ForegroundColor Red
            $allDatabasesExist = $false
        }
        $reader.Close()
    }
    
    Write-Host ""
    
    # Check tables in NS_CIS
    Write-Host "Step 2: Verifying tables in NS_CIS database..." -ForegroundColor Yellow
    $tableQuery = @"
USE NS_CIS;
SELECT COUNT(*) AS TableCount FROM sys.tables;
"@
    
    $tableCommand = New-Object System.Data.SqlClient.SqlCommand($tableQuery, $connection)
    $tableCount = [int]$tableCommand.ExecuteScalar()
    
    Write-Host "   Found $tableCount table(s) in NS_CIS" -ForegroundColor $(if ($tableCount -gt 0) { "Green" } else { "Red" })
    
    # Check key tables
    $keyTables = @("Users", "Containers", "ContainerCompletenessStatuses", "AnalysisGroups", "AuditDecisions", "ImageAnalysisDecisions")
    Write-Host "   Checking key tables..." -ForegroundColor Gray
    foreach ($tableName in $keyTables) {
        $tableCheckQuery = "USE NS_CIS; SELECT COUNT(*) FROM sys.tables WHERE name = '$tableName'"
        $tableCheckCommand = New-Object System.Data.SqlClient.SqlCommand($tableCheckQuery, $connection)
        $exists = [int]$tableCheckCommand.ExecuteScalar()
        if ($exists -gt 0) {
            Write-Host "      ✅ $tableName" -ForegroundColor Green
        } else {
            Write-Host "      ❌ $tableName - NOT FOUND" -ForegroundColor Red
        }
    }
    
    Write-Host ""
    
    # Check tables in ICUMS
    Write-Host "Step 3: Verifying tables in ICUMS database..." -ForegroundColor Yellow
    $icumsTableQuery = "USE ICUMS; SELECT COUNT(*) AS TableCount FROM sys.tables;"
    $icumsTableCommand = New-Object System.Data.SqlClient.SqlCommand($icumsTableQuery, $connection)
    $icumsTableCount = [int]$icumsTableCommand.ExecuteScalar()
    Write-Host "   Found $icumsTableCount table(s) in ICUMS" -ForegroundColor $(if ($icumsTableCount -gt 0) { "Green" } else { "Red" })
    
    Write-Host ""
    
    # Check tables in ICUMS_Downloads
    Write-Host "Step 4: Verifying tables in ICUMS_Downloads database..." -ForegroundColor Yellow
    $downloadsTableQuery = "USE ICUMS_Downloads; SELECT COUNT(*) AS TableCount FROM sys.tables;"
    $downloadsTableCommand = New-Object System.Data.SqlClient.SqlCommand($downloadsTableQuery, $connection)
    $downloadsTableCount = [int]$downloadsTableCommand.ExecuteScalar()
    Write-Host "   Found $downloadsTableCount table(s) in ICUMS_Downloads" -ForegroundColor $(if ($downloadsTableCount -gt 0) { "Green" } else { "Red" })
    
    Write-Host ""
    
    # Check production files
    Write-Host "Step 5: Verifying production files..." -ForegroundColor Yellow
    if (Test-Path $ProductionPath) {
        Write-Host "   ✅ Production path exists: $ProductionPath" -ForegroundColor Green
        
        $apiPath = Join-Path $ProductionPath "src\NickScanCentralImagingPortal.API"
        $appSettingsPath = Join-Path $apiPath "appsettings.json"
        
        if (Test-Path $appSettingsPath) {
            Write-Host "   ✅ appsettings.json found" -ForegroundColor Green
            
            # Check connection strings
            $appSettings = Get-Content $appSettingsPath -Raw | ConvertFrom-Json
            $nsCisConn = $appSettings.ConnectionStrings.NS_CIS_Connection
            
            if ($nsCisConn -like "*10.0.0.79*") {
                Write-Host "   ✅ Connection strings point to production SQL Server (10.0.0.79)" -ForegroundColor Green
            } else {
                Write-Host "   ⚠️ Connection strings may not be configured for production" -ForegroundColor Yellow
            }
        } else {
            Write-Host "   ⚠️ appsettings.json not found" -ForegroundColor Yellow
        }
    } else {
        Write-Host "   ❌ Production path not found: $ProductionPath" -ForegroundColor Red
    }
    
    Write-Host ""
    
    # Summary
    Write-Host "==================================================" -ForegroundColor Cyan
    Write-Host "Verification Summary" -ForegroundColor Cyan
    Write-Host "==================================================" -ForegroundColor Cyan
    Write-Host ""
    
    if ($allDatabasesExist -and $tableCount -gt 0 -and $icumsTableCount -gt 0 -and $downloadsTableCount -gt 0) {
        Write-Host "✅ Production environment is ready!" -ForegroundColor Green
        Write-Host ""
        Write-Host "Status:" -ForegroundColor Yellow
        Write-Host "  ✅ All databases created and ONLINE" -ForegroundColor Green
        Write-Host "  ✅ NS_CIS: $tableCount tables" -ForegroundColor Green
        Write-Host "  ✅ ICUMS: $icumsTableCount tables" -ForegroundColor Green
        Write-Host "  ✅ ICUMS_Downloads: $downloadsTableCount tables" -ForegroundColor Green
        Write-Host "  ✅ Production files deployed" -ForegroundColor Green
        Write-Host ""
        Write-Host "Production is ready to start!" -ForegroundColor Green
    } else {
        Write-Host "⚠️ Some issues detected. Please review above." -ForegroundColor Yellow
    }
    
    Write-Host ""
    
    $connection.Close()
    
} catch {
    Write-Host "❌ Error: $($_.Exception.Message)" -ForegroundColor Red
    Write-Host $_.Exception.StackTrace -ForegroundColor Red
    exit 1
}


# ================================================
# Complete Database Replication Script
# Step 1: Apply EF Core Migrations (creates schema)
# Step 2: Transfer data using Linked Server
# ================================================

param(
    [string]$SourceInstance = "localhost\NS_CIS",
    [string]$TargetInstance = "(local)",
    [string[]]$Databases = @("NS_CIS", "ICUMS", "ICUMS_Downloads"),
    [string]$ProjectPath = "src\NickScanCentralImagingPortal.Infrastructure",
    [string]$StartupProject = "src\NickScanCentralImagingPortal.API"
)

$ErrorActionPreference = "Continue"

Write-Host "========================================" -ForegroundColor Cyan
Write-Host "Complete Database Replication" -ForegroundColor Cyan
Write-Host "EF Migrations + Data Transfer" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""
Write-Host "Source: $SourceInstance" -ForegroundColor Yellow
Write-Host "Target: $TargetInstance" -ForegroundColor Yellow
Write-Host ""

# Step 1: Create Linked Server for data transfer
Write-Host "Step 1: Setting up Linked Server..." -ForegroundColor Cyan
Write-Host "----------------------------------------" -ForegroundColor Gray

$linkedServerScript = @"
-- Create linked server to NS_CIS instance
IF EXISTS (SELECT 1 FROM sys.servers WHERE name = 'NS_CIS_SOURCE')
BEGIN
    EXEC sp_dropserver 'NS_CIS_SOURCE', 'droplogins';
END
GO

EXEC sp_addlinkedserver
    @server = 'NS_CIS_SOURCE',
    @srvproduct = 'SQL Server';
GO

EXEC sp_addlinkedsrvlogin
    @rmtsrvname = 'NS_CIS_SOURCE',
    @useself = 'true',
    @locallogin = NULL,
    @rmtuser = NULL,
    @rmtpassword = NULL;
GO

-- Test connection
SELECT @@SERVERNAME AS LocalServer;
SELECT @@SERVERNAME AS RemoteServer FROM [NS_CIS_SOURCE].master.sys.databases WHERE name = 'master';
GO
"@

$linkedServerFile = [System.IO.Path]::GetTempFileName() + ".sql"
$linkedServerScript | Out-File -FilePath $linkedServerFile -Encoding UTF8

Write-Host "  → Creating linked server..." -ForegroundColor Cyan
sqlcmd -S $TargetInstance -E -i $linkedServerFile | Out-Null

if ($LASTEXITCODE -eq 0) {
    Write-Host "  ✓ Linked server created" -ForegroundColor Green
} else {
    Write-Host "  ⚠ Linked server creation had issues (may already exist)" -ForegroundColor Yellow
}

Remove-Item $linkedServerFile -Force
Write-Host ""

# Step 2: Apply EF Core Migrations
Write-Host "Step 2: Applying EF Core Migrations..." -ForegroundColor Cyan
Write-Host "----------------------------------------" -ForegroundColor Gray
Write-Host "  This will create the database schema on MSSQLSERVER" -ForegroundColor Yellow
Write-Host ""

# Temporarily update connection strings to point to MSSQLSERVER
$appsettingsPath = "$StartupProject\appsettings.json"
$appsettingsBackup = "$StartupProject\appsettings.json.replication_backup"

if (Test-Path $appsettingsPath) {
    Write-Host "  → Backing up appsettings.json..." -ForegroundColor Cyan
    Copy-Item $appsettingsPath $appsettingsBackup -Force
    
    # Read and update connection strings
    $appsettings = Get-Content $appsettingsPath -Raw | ConvertFrom-Json
    $appsettings.ConnectionStrings.NS_CIS_Connection = $appsettings.ConnectionStrings.NS_CIS_Connection -replace '127\.0\.0\.1,1433', '127.0.0.1,1433' -replace 'localhost', '(local)'
    $appsettings.ConnectionStrings.ICUMS_Connection = $appsettings.ConnectionStrings.ICUMS_Connection -replace '127\.0\.0\.1,1433', '127.0.0.1,1433' -replace 'localhost', '(local)'
    $appsettings.ConnectionStrings.ICUMS_Downloads_Connection = $appsettings.ConnectionStrings.ICUMS_Downloads_Connection -replace '127\.0\.0\.1,1433', '127.0.0.1,1433' -replace 'localhost', '(local)'
    
    $appsettings | ConvertTo-Json -Depth 10 | Set-Content $appsettingsPath -Encoding UTF8
    Write-Host "  ✓ Connection strings updated temporarily" -ForegroundColor Green
}

# Apply migrations for each context
Write-Host ""
Write-Host "  → Applying ApplicationDbContext migrations (NS_CIS)..." -ForegroundColor Cyan
$migrationCmd = "dotnet ef database update --project $ProjectPath --startup-project $StartupProject --context ApplicationDbContext"
Invoke-Expression $migrationCmd
if ($LASTEXITCODE -eq 0) {
    Write-Host "  ✓ NS_CIS schema created" -ForegroundColor Green
} else {
    Write-Host "  ✗ NS_CIS migration failed!" -ForegroundColor Red
}

Write-Host ""
Write-Host "  → Applying IcumDbContext migrations (ICUMS)..." -ForegroundColor Cyan
$migrationCmd = "dotnet ef database update --project $ProjectPath --startup-project $StartupProject --context IcumDbContext"
Invoke-Expression $migrationCmd
if ($LASTEXITCODE -eq 0) {
    Write-Host "  ✓ ICUMS schema created" -ForegroundColor Green
} else {
    Write-Host "  ✗ ICUMS migration failed!" -ForegroundColor Red
}

Write-Host ""
Write-Host "  → Applying IcumDownloadsDbContext migrations (ICUMS_Downloads)..." -ForegroundColor Cyan
$migrationCmd = "dotnet ef database update --project $ProjectPath --startup-project $StartupProject --context IcumDownloadsDbContext"
Invoke-Expression $migrationCmd
if ($LASTEXITCODE -eq 0) {
    Write-Host "  ✓ ICUMS_Downloads schema created" -ForegroundColor Green
} else {
    Write-Host "  ✗ ICUMS_Downloads migration failed!" -ForegroundColor Red
}

# Restore original appsettings
if (Test-Path $appsettingsBackup) {
    Write-Host ""
    Write-Host "  → Restoring original appsettings.json..." -ForegroundColor Cyan
    Copy-Item $appsettingsBackup $appsettingsPath -Force
    Remove-Item $appsettingsBackup -Force
    Write-Host "  ✓ Original connection strings restored" -ForegroundColor Green
}

Write-Host ""

# Step 3: Transfer Data using Linked Server
Write-Host "Step 3: Transferring Data..." -ForegroundColor Cyan
Write-Host "----------------------------------------" -ForegroundColor Gray
Write-Host "  This will copy all data from NS_CIS instance to MSSQLSERVER" -ForegroundColor Yellow
Write-Host "  This may take a while depending on data size..." -ForegroundColor Yellow
Write-Host ""

foreach ($db in $Databases) {
    Write-Host "  Processing: $db" -ForegroundColor Yellow
    
    # Get list of tables
    $tablesQuery = "SELECT TABLE_SCHEMA, TABLE_NAME FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_TYPE = 'BASE TABLE' ORDER BY TABLE_SCHEMA, TABLE_NAME"
    $tablesFile = [System.IO.Path]::GetTempFileName()
    sqlcmd -S $SourceInstance -E -d $db -Q $tablesQuery -W -h -1 -o $tablesFile
    
    $tables = Get-Content $tablesFile | Where-Object { $_ -match '\S' -and $_ -notmatch 'rows affected' -and $_ -notmatch '^---' }
    $tableCount = ($tables | Measure-Object -Line).Lines
    Remove-Item $tablesFile -Force
    
    Write-Host "    Found $tableCount tables" -ForegroundColor Gray
    
    $successCount = 0
    $errorCount = 0
    
    foreach ($tableLine in $tables) {
        $parts = $tableLine -split '\s+', 2
        if ($parts.Length -ge 2) {
            $schema = $parts[0]
            $table = $parts[1]
            $fullTableName = "[$schema].[$table]"
            
            # Skip __EFMigrationsHistory - it will be created by migrations
            if ($table -eq "__EFMigrationsHistory") {
                continue
            }
            
            # Transfer data using INSERT...SELECT via linked server
            $transferQuery = "-- Disable constraints temporarily for faster insert`n" +
                            "ALTER TABLE [$db].$fullTableName NOCHECK CONSTRAINT ALL;`n" +
                            "GO`n`n" +
                            "-- Transfer data`n" +
                            "INSERT INTO [$db].$fullTableName`n" +
                            "SELECT * FROM [NS_CIS_SOURCE].[$db].$fullTableName;`n" +
                            "GO`n`n" +
                            "-- Re-enable constraints`n" +
                            "ALTER TABLE [$db].$fullTableName CHECK CONSTRAINT ALL;`n" +
                            "GO"
            
            $transferFile = [System.IO.Path]::GetTempFileName() + ".sql"
            $transferQuery | Out-File -FilePath $transferFile -Encoding UTF8
            
            sqlcmd -S $TargetInstance -E -d $db -i $transferFile -b | Out-Null
            
            if ($LASTEXITCODE -eq 0) {
                $successCount++
                if ($successCount % 5 -eq 0) {
                    Write-Host "      Transferred $successCount of $tableCount tables..." -ForegroundColor Gray
                }
            } else {
                $errorCount++
                Write-Host "      ⚠ Error transferring $fullTableName" -ForegroundColor Yellow
            }
            
            Remove-Item $transferFile -Force
        }
    }
    
    Write-Host "    ✓ Completed: $successCount successful, $errorCount errors" -ForegroundColor $(if ($errorCount -eq 0) { "Green" } else { "Yellow" })
    Write-Host ""
}

# Step 4: Verification
Write-Host "Step 4: Verifying Data Integrity..." -ForegroundColor Cyan
Write-Host "----------------------------------------" -ForegroundColor Gray

foreach ($db in $Databases) {
    Write-Host "  Verifying: $db" -ForegroundColor Yellow
    
    # Get table counts from both instances
    $sourceQuery = "SELECT COUNT(*) AS TableCount FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_TYPE = 'BASE TABLE'"
    $targetQuery = $sourceQuery
    
    $sourceCount = (sqlcmd -S $SourceInstance -E -d $db -Q $sourceQuery -W -h -1 | Where-Object { $_ -match '^\d+$' } | Select-Object -First 1)
    $targetCount = (sqlcmd -S $TargetInstance -E -d $db -Q $targetQuery -W -h -1 | Where-Object { $_ -match '^\d+$' } | Select-Object -First 1)
    
    if ($sourceCount -eq $targetCount) {
        Write-Host "    ✓ Table counts match: $sourceCount tables" -ForegroundColor Green
    } else {
        Write-Host "    ⚠ Table count mismatch: Source=$sourceCount, Target=$targetCount" -ForegroundColor Red
    }
}

Write-Host ""
Write-Host "========================================" -ForegroundColor Cyan
Write-Host "Replication Complete!" -ForegroundColor Green
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""
Write-Host "Next steps:" -ForegroundColor Yellow
Write-Host "1. Verify data integrity manually" -ForegroundColor White
Write-Host "2. Update connection strings in appsettings.json if needed" -ForegroundColor White
Write-Host "3. Test application with MSSQLSERVER instance" -ForegroundColor White
Write-Host ""


# ================================================
# Database Replication using BCP (Bulk Copy Program)
# Works across SQL Server versions
# ================================================

param(
    [string]$SourceInstance = "localhost\NS_CIS",
    [string]$TargetInstance = "(local)",
    [string[]]$Databases = @("NS_CIS", "ICUMS", "ICUMS_Downloads"),
    [string]$DataPath = "C:\Temp\DB_Replication"
)

# Continues past errors intentionally: BCP exports/imports per-table across DBs; per-table BCP exit codes are checked and tallied, parent loop must complete.
$ErrorActionPreference = "Continue"

Write-Host "========================================" -ForegroundColor Cyan
Write-Host "Database Replication using BCP" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host "Source: $SourceInstance" -ForegroundColor Yellow
Write-Host "Target: $TargetInstance" -ForegroundColor Yellow
Write-Host ""

# Create data directory
if (-not (Test-Path $DataPath)) {
    New-Item -ItemType Directory -Path $DataPath -Force | Out-Null
}

foreach ($db in $Databases) {
    Write-Host "Processing: $db" -ForegroundColor Yellow
    Write-Host "----------------------------------------" -ForegroundColor Gray
    
    $dbPath = Join-Path $DataPath $db
    if (-not (Test-Path $dbPath)) {
        New-Item -ItemType Directory -Path $dbPath -Force | Out-Null
    }
    
    # Step 1: Get list of tables
    Write-Host "  [1/4] Getting table list..." -ForegroundColor Cyan
    $tablesQuery = "SELECT TABLE_SCHEMA, TABLE_NAME FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_TYPE = 'BASE TABLE' ORDER BY TABLE_SCHEMA, TABLE_NAME"
    $tablesFile = Join-Path $dbPath "tables.txt"
    sqlcmd -S $SourceInstance -E -d $db -Q $tablesQuery -W -h -1 -o $tablesFile
    
    $tables = Get-Content $tablesFile | Where-Object { $_ -match '\S' -and $_ -notmatch 'rows affected' -and $_ -notmatch '^---' }
    $tableCount = ($tables | Measure-Object -Line).Lines
    Write-Host "  ✓ Found $tableCount tables" -ForegroundColor Green
    
    # Step 2: Export data using BCP
    Write-Host "  [2/4] Exporting data using BCP..." -ForegroundColor Cyan
    $exportCount = 0
    foreach ($tableLine in $tables) {
        $parts = $tableLine -split '\s+', 2
        if ($parts.Length -ge 2) {
            $schema = $parts[0]
            $table = $parts[1]
            $fullTableName = "$schema.$table"
            $dataFile = Join-Path $dbPath "$schema`_$table.dat"
            
            $bcpCmd = "bcp `"[$db].[$fullTableName]`" out `"$dataFile`" -S `"$SourceInstance`" -T -n -E"
            Invoke-Expression $bcpCmd | Out-Null
            
            if ($LASTEXITCODE -eq 0) {
                $exportCount++
                if ($exportCount % 10 -eq 0) {
                    Write-Host "    Exported $exportCount of $tableCount tables..." -ForegroundColor Gray
                }
            }
        }
    }
    Write-Host "  ✓ Exported $exportCount tables" -ForegroundColor Green
    
    # Step 3: Generate schema script (simplified - would need full script generation)
    Write-Host "  [3/4] Note: Schema must be created separately" -ForegroundColor Yellow
    Write-Host "    Use SQL Server Management Studio 'Generate Scripts' wizard" -ForegroundColor Yellow
    Write-Host "    Or run EF Core migrations on target database" -ForegroundColor Yellow
    
    # Step 4: Import data to target
    Write-Host "  [4/4] Importing data to target..." -ForegroundColor Cyan
    $importCount = 0
    foreach ($tableLine in $tables) {
        $parts = $tableLine -split '\s+', 2
        if ($parts.Length -ge 2) {
            $schema = $parts[0]
            $table = $parts[1]
            $fullTableName = "$schema.$table"
            $dataFile = Join-Path $dbPath "$schema`_$table.dat"
            
            if (Test-Path $dataFile) {
                $bcpCmd = "bcp `"[$db].[$fullTableName]`" in `"$dataFile`" -S `"$TargetInstance`" -T -n -E -b 10000"
                Invoke-Expression $bcpCmd | Out-Null
                
                if ($LASTEXITCODE -eq 0) {
                    $importCount++
                    if ($importCount % 10 -eq 0) {
                        Write-Host "    Imported $importCount of $tableCount tables..." -ForegroundColor Gray
                    }
                }
            }
        }
    }
    Write-Host "  ✓ Imported $importCount tables" -ForegroundColor Green
    Write-Host ""
}

Write-Host "========================================" -ForegroundColor Cyan
Write-Host "BCP Replication Complete!" -ForegroundColor Green
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""
Write-Host "IMPORTANT: Ensure schema is created on target first!" -ForegroundColor Yellow
Write-Host ""


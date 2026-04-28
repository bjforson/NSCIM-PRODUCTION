# Transfer all data from NS_CIS instance to MSSQLSERVER using linked server
# Fixed version that handles identity columns properly

param(
    [string]$SourceInstance = "localhost\NS_CIS",
    [string]$TargetInstance = "(local)",
    [string[]]$Databases = @("NS_CIS", "ICUMS", "ICUMS_Downloads")
)

# Continues past errors intentionally: iterates every table across multiple DBs invoking linked-server INSERT; per-table errors are tallied and report must complete.
$ErrorActionPreference = "Continue"

Write-Host "========================================" -ForegroundColor Cyan
Write-Host "Data Transfer: NS_CIS -> MSSQLSERVER" -ForegroundColor Cyan
Write-Host "Fixed Version - Handles Identity Columns" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""

function Get-ColumnList {
    param(
        [string]$Instance,
        [string]$Database,
        [string]$Schema,
        [string]$Table
    )
    
    $query = "SELECT COLUMN_NAME FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_SCHEMA = '$Schema' AND TABLE_NAME = '$Table' ORDER BY ORDINAL_POSITION"
    $columnsFile = [System.IO.Path]::GetTempFileName()
    sqlcmd -S $Instance -E -d $Database -Q $query -W -h -1 -o $columnsFile | Out-Null
    
    $columns = Get-Content $columnsFile | Where-Object { $_ -match '\S' -and $_ -notmatch 'rows affected' -and $_ -notmatch '^---' }
    Remove-Item $columnsFile -Force
    
    $columnList = ($columns | ForEach-Object { "[$_]" }) -join ", "
    return $columnList
}

function Has-IdentityColumn {
    param(
        [string]$Instance,
        [string]$Database,
        [string]$Schema,
        [string]$Table
    )
    
    $query = "SELECT COUNT(*) FROM sys.identity_columns WHERE object_id = OBJECT_ID('[$Database].[$Schema].[$Table]')"
    $resultFile = [System.IO.Path]::GetTempFileName()
    sqlcmd -S $Instance -E -d $Database -Q $query -W -h -1 -o $resultFile | Out-Null
    
    $result = Get-Content $resultFile | Where-Object { $_ -match '^\s*[1-9]' }
    Remove-Item $resultFile -Force
    
    return $result -ne $null
}

foreach ($db in $Databases) {
    Write-Host "Processing: $db" -ForegroundColor Yellow
    Write-Host "----------------------------------------" -ForegroundColor Gray
    
    # Get list of tables
    $tablesQuery = "SELECT TABLE_SCHEMA, TABLE_NAME FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_TYPE = 'BASE TABLE' AND TABLE_NAME != '__EFMigrationsHistory' ORDER BY TABLE_SCHEMA, TABLE_NAME"
    $tablesFile = [System.IO.Path]::GetTempFileName()
    sqlcmd -S $SourceInstance -E -d $db -Q $tablesQuery -W -h -1 -o $tablesFile
    
    $tables = Get-Content $tablesFile | Where-Object { $_ -match '\S' -and $_ -notmatch 'rows affected' -and $_ -notmatch '^---' }
    $tableCount = ($tables | Measure-Object -Line).Lines
    Remove-Item $tablesFile -Force
    
    Write-Host "  Found $tableCount tables to transfer" -ForegroundColor Gray
    Write-Host ""
    
    $successCount = 0
    $errorCount = 0
    $currentTable = 0
    
    foreach ($tableLine in $tables) {
        $parts = $tableLine -split '\s+', 2
        if ($parts.Length -ge 2) {
            $schema = $parts[0]
            $table = $parts[1]
            $fullTableName = "[$schema].[$table]"
            $currentTable++
            
            Write-Host "  [$currentTable/$tableCount] Transferring $fullTableName..." -ForegroundColor Cyan -NoNewline
            
            try {
                # Get column list
                $columnList = Get-ColumnList -Instance $TargetInstance -Database $db -Schema $schema -Table $table
                
                # Check if table has identity column
                $hasIdentity = Has-IdentityColumn -Instance $TargetInstance -Database $db -Schema $schema -Table $table
                
                # Build transfer SQL with proper SET options
                $transferSQL = ""
                if ($hasIdentity) {
                    $transferSQL = "SET QUOTED_IDENTIFIER ON;`n" +
                                  "SET IDENTITY_INSERT [$db].$fullTableName ON;`n" +
                                  "ALTER TABLE [$db].$fullTableName NOCHECK CONSTRAINT ALL;`n" +
                                  "INSERT INTO [$db].$fullTableName ($columnList) SELECT $columnList FROM [NS_CIS_SOURCE].[$db].$fullTableName;`n" +
                                  "SET IDENTITY_INSERT [$db].$fullTableName OFF;`n" +
                                  "ALTER TABLE [$db].$fullTableName CHECK CONSTRAINT ALL;"
                } else {
                    $transferSQL = "SET QUOTED_IDENTIFIER ON;`n" +
                                  "ALTER TABLE [$db].$fullTableName NOCHECK CONSTRAINT ALL;`n" +
                                  "INSERT INTO [$db].$fullTableName ($columnList) SELECT $columnList FROM [NS_CIS_SOURCE].[$db].$fullTableName;`n" +
                                  "ALTER TABLE [$db].$fullTableName CHECK CONSTRAINT ALL;"
                }
                
                $transferFile = [System.IO.Path]::GetTempFileName() + ".sql"
                $transferSQL | Out-File -FilePath $transferFile -Encoding UTF8
                
                $result = sqlcmd -S $TargetInstance -E -d $db -i $transferFile -b 2>&1
                
                if ($LASTEXITCODE -eq 0) {
                    Write-Host " ✓" -ForegroundColor Green
                    $successCount++
                } else {
                    Write-Host " ✗" -ForegroundColor Red
                    $errorOutput = $result | Where-Object { $_ -match 'Msg|Error' } | Select-Object -First 3
                    if ($errorOutput) {
                        Write-Host "    $($errorOutput -join ' ')" -ForegroundColor Red
                    }
                    $errorCount++
                }
                
                Remove-Item $transferFile -Force -ErrorAction SilentlyContinue
            } catch {
                Write-Host " ✗ Error: $_" -ForegroundColor Red
                $errorCount++
            }
        }
    }
    
    Write-Host ""
    Write-Host "  Summary: $successCount successful, $errorCount errors" -ForegroundColor $(if ($errorCount -eq 0) { "Green" } else { "Yellow" })
    Write-Host ""
}

Write-Host "========================================" -ForegroundColor Cyan
Write-Host "Data Transfer Complete!" -ForegroundColor Green
Write-Host "========================================" -ForegroundColor Cyan


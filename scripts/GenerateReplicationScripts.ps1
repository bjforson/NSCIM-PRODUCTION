# ============================================================================
# Generate SQL Scripts to Replicate Database Structure
# FROM SQL Server 2022 TO SQL Server 2014
# ============================================================================

param(
    [string]$SourceServer = "localhost\NS_CIS", # SQL Server 2022 (SOURCE)
    [string]$TargetServer = "127.0.0.1,1433",   # SQL Server 2014 (TARGET)
    [string[]]$Databases = @("NS_CIS", "ICUMS", "ICUMS_Downloads"),
    [string]$OutputDir = "scripts\sql\replication"
)

# Continues past errors intentionally: loops over multiple databases scripting structure to .sql files; per-DB script-generation errors must not abort the rest.
$ErrorActionPreference = "Continue"

Write-Host "========================================" -ForegroundColor Cyan
Write-Host "Database Structure Replication Script Generator" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host "Source (2022): $SourceServer" -ForegroundColor Green
Write-Host "Target (2014): $TargetServer" -ForegroundColor Yellow
Write-Host ""

# Create output directory
if (-not (Test-Path $OutputDir)) {
    New-Item -ItemType Directory -Path $OutputDir -Force | Out-Null
    Write-Host "Created output directory: $OutputDir" -ForegroundColor Green
}

foreach ($dbName in $Databases) {
    Write-Host ""
    Write-Host "========================================" -ForegroundColor Cyan
    Write-Host "Processing Database: $dbName" -ForegroundColor Cyan
    Write-Host "========================================" -ForegroundColor Cyan
    
    # Check if source database exists
    $checkQuery = "SELECT COUNT(*) FROM sys.databases WHERE name = '$dbName'"
    $sourceExists = $false
    try {
        $result = sqlcmd -S $SourceServer -E -Q $checkQuery -h -1 -W 2>&1
        if ($LASTEXITCODE -eq 0 -and $result -match "^1") {
            $sourceExists = $true
        }
    }
    catch {
        Write-Host "Error checking source database: $_" -ForegroundColor Red
    }
    
    if (-not $sourceExists) {
        Write-Host "⚠ Source database $dbName does not exist on $SourceServer" -ForegroundColor Yellow
        Write-Host "   Please restore/attach the database to the 2022 instance first" -ForegroundColor Gray
        continue
    }
    
    Write-Host "✓ Source database exists" -ForegroundColor Green
    
    # Generate script to get table structure from source
    $scriptFile = Join-Path $OutputDir "Replicate_${dbName}_Structure.sql"
    
    Write-Host "📝 Generating replication script: $scriptFile" -ForegroundColor Yellow
    
    # Create SQL script header
    $sqlScript = @"
-- ============================================================================
-- Replicate $dbName Database Structure
-- Generated: $(Get-Date -Format "yyyy-MM-dd HH:mm:ss")
-- Source: $SourceServer (SQL Server 2022)
-- Target: $TargetServer (SQL Server 2014)
-- ============================================================================
-- This script replicates the database structure from SQL Server 2022 to 2014
-- Run this script on the TARGET server (2014) after comparing structures
-- ============================================================================

USE [$dbName];
GO

"@
    
    # Get table list from source
    Write-Host "   Getting table list from source..." -ForegroundColor Gray
    $tableQuery = "SELECT TABLE_SCHEMA, TABLE_NAME FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_TYPE = 'BASE TABLE' ORDER BY TABLE_SCHEMA, TABLE_NAME"
    
    try {
        $tables = sqlcmd -S $SourceServer -d $dbName -E -Q $tableQuery -W -s "|" -h -1 2>&1
        
        if ($LASTEXITCODE -eq 0) {
            $tableCount = 0
            foreach ($line in $tables) {
                if ($line -match "^([^|]+)\|(.+)$") {
                    $schema = $matches[1].Trim()
                    $table = $matches[2].Trim()
                    $tableCount++
                    
                    Write-Host "   Found table: $schema.$table" -ForegroundColor Gray
                    
                    # Get column definitions
                    $columnQuery = @"
SELECT 
    COLUMN_NAME,
    DATA_TYPE,
    ISNULL(CAST(CHARACTER_MAXIMUM_LENGTH AS VARCHAR), '') AS MAX_LENGTH,
    ISNULL(CAST(NUMERIC_PRECISION AS VARCHAR), '') AS PRECISION,
    ISNULL(CAST(NUMERIC_SCALE AS VARCHAR), '') AS SCALE,
    IS_NULLABLE,
    ISNULL(COLUMN_DEFAULT, '') AS DEFAULT_VALUE
FROM INFORMATION_SCHEMA.COLUMNS
WHERE TABLE_SCHEMA = '$schema' AND TABLE_NAME = '$table'
ORDER BY ORDINAL_POSITION
"@
                    
                    $columns = sqlcmd -S $SourceServer -d $dbName -E -Q $columnQuery -W -s "|" -h -1 2>&1
                    
                    # Add table creation comment
                    $sqlScript += @"

-- ============================================================================
-- Table: $schema.$table
-- ============================================================================
"@
                    
                    # Check if table exists on target
                    $sqlScript += @"
IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = '$table' AND schema_id = SCHEMA_ID('$schema'))
BEGIN
    PRINT 'Creating table: $schema.$table';
    
    CREATE TABLE [$schema].[$table] (
"@
                    
                    $firstCol = $true
                    foreach ($colLine in $columns) {
                        if ($colLine -match "^([^|]+)\|([^|]+)\|([^|]*)\|([^|]*)\|([^|]*)\|([^|]+)\|([^|]*)$") {
                            $colName = $matches[1].Trim()
                            $dataType = $matches[2].Trim()
                            $maxLength = $matches[3].Trim()
                            $precision = $matches[4].Trim()
                            $scale = $matches[5].Trim()
                            $isNullable = $matches[6].Trim()
                            $defaultValue = $matches[7].Trim()
                            
                            if (-not $firstCol) {
                                $sqlScript += ",`n"
                            }
                            $firstCol = $false
                            
                            # Build column definition
                            $colDef = "        [$colName] $dataType"
                            
                            # Add length/precision
                            if ($dataType -match "^(varchar|nvarchar|char|nchar|varbinary|binary)$") {
                                if ($maxLength -eq "-1") {
                                    $colDef += "(MAX)"
                                }
                                elseif ($maxLength -ne "") {
                                    $colDef += "($maxLength)"
                                }
                            }
                            elseif ($dataType -match "^(decimal|numeric)$") {
                                if ($precision -ne "" -and $scale -ne "") {
                                    $colDef += "($precision,$scale)"
                                }
                            }
                            
                            # Nullability
                            if ($isNullable -eq "NO") {
                                $colDef += " NOT NULL"
                            }
                            else {
                                $colDef += " NULL"
                            }
                            
                            # Default value
                            if ($defaultValue -ne "") {
                                $colDef += " DEFAULT $defaultValue"
                            }
                            
                            $sqlScript += $colDef
                        }
                    }
                    
                    $sqlScript += @"
    );
    
    PRINT 'Table $schema.$table created successfully';
END
ELSE
BEGIN
    PRINT 'Table $schema.$table already exists - checking for missing columns...';
    
"@
                    
                    # Add ALTER TABLE statements for missing columns
                    foreach ($colLine in $columns) {
                        if ($colLine -match "^([^|]+)\|") {
                            $colName = $matches[1].Trim()
                            $sqlScript += @"
    IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('$schema.$table') AND name = '$colName')
    BEGIN
        -- TODO: Add column $colName
        PRINT 'Column $colName needs to be added to $schema.$table';
    END
    
"@
                        }
                    }
                    
                    $sqlScript += "END`nGO`n`n"
                }
            }
            
            Write-Host "   Found $tableCount tables" -ForegroundColor Green
        }
    }
    catch {
        Write-Host "   Error getting tables: $_" -ForegroundColor Red
    }
    
    # Write script to file
    $sqlScript | Out-File -FilePath $scriptFile -Encoding UTF8
    Write-Host "✅ Script generated: $scriptFile" -ForegroundColor Green
}

Write-Host ""
Write-Host "========================================" -ForegroundColor Cyan
Write-Host "Script Generation Complete" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""
Write-Host "Next steps:" -ForegroundColor Yellow
Write-Host "1. Review the generated SQL scripts in: $OutputDir" -ForegroundColor Gray
Write-Host "2. Run the scripts on the TARGET server (2014) to replicate structure" -ForegroundColor Gray
Write-Host "3. Verify all tables and columns are created correctly" -ForegroundColor Gray












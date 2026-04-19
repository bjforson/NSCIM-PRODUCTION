# ============================================================================
# Compare and Replicate Database Structure
# Replicates SQL Server 2022 database structure to SQL Server 2014
# SOURCE: SQL Server 2022 (has the complete structure)
# TARGET: SQL Server 2014 (needs to match 2022)
# ============================================================================

param(
    [string]$SourceServer = "localhost\NS_CIS", # SQL Server 2022 (SOURCE)
    [string]$TargetServer = "127.0.0.1,1433",   # SQL Server 2014 (TARGET)
    [string[]]$Databases = @("NS_CIS", "ICUMS", "ICUMS_Downloads")
)

$ErrorActionPreference = "Stop"

Write-Host "========================================" -ForegroundColor Cyan
Write-Host "Database Structure Replication Tool" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host "Source (2022): $SourceServer" -ForegroundColor Green
Write-Host "Target (2014): $TargetServer" -ForegroundColor Yellow
Write-Host ""
Write-Host "Replicating FROM 2022 TO 2014..." -ForegroundColor Cyan
Write-Host ""

# Function to get all tables from a database
function Get-DatabaseTables {
    param(
        [string]$Server,
        [string]$Database
    )
    
    $query = @"
SELECT TABLE_SCHEMA, TABLE_NAME 
FROM INFORMATION_SCHEMA.TABLES 
WHERE TABLE_TYPE = 'BASE TABLE' 
ORDER BY TABLE_SCHEMA, TABLE_NAME
"@
    
    try {
        $result = sqlcmd -S $Server -d $Database -E -Q $query -W -s "," -h -1
        $tables = @()
        foreach ($line in $result) {
            if ($line -match "^([^,]+),(.+)$") {
                $tables += [PSCustomObject]@{
                    Schema = $matches[1].Trim()
                    Name = $matches[2].Trim()
                }
            }
        }
        return $tables
    }
    catch {
        Write-Host "Error getting tables from $Database on $Server : $_" -ForegroundColor Red
        return @()
    }
}

# Function to get table columns
function Get-TableColumns {
    param(
        [string]$Server,
        [string]$Database,
        [string]$Schema,
        [string]$TableName
    )
    
    $query = @"
SELECT 
    COLUMN_NAME,
    DATA_TYPE,
    CHARACTER_MAXIMUM_LENGTH,
    NUMERIC_PRECISION,
    NUMERIC_SCALE,
    IS_NULLABLE,
    COLUMN_DEFAULT
FROM INFORMATION_SCHEMA.COLUMNS
WHERE TABLE_SCHEMA = '$Schema' AND TABLE_NAME = '$TableName'
ORDER BY ORDINAL_POSITION
"@
    
    try {
        $result = sqlcmd -S $Server -d $Database -E -Q $query -W -s "," -h -1
        $columns = @()
        foreach ($line in $result) {
            if ($line -match "^([^,]+),([^,]+),([^,]*),([^,]*),([^,]*),([^,]+),([^,]*)$") {
                $columns += [PSCustomObject]@{
                    ColumnName = $matches[1].Trim()
                    DataType = $matches[2].Trim()
                    MaxLength = $matches[3].Trim()
                    Precision = $matches[4].Trim()
                    Scale = $matches[5].Trim()
                    IsNullable = $matches[6].Trim()
                    DefaultValue = $matches[7].Trim()
                }
            }
        }
        return $columns
    }
    catch {
        Write-Host "Error getting columns for $Schema.$TableName : $_" -ForegroundColor Red
        return @()
    }
}

# Function to check if database exists
function Test-DatabaseExists {
    param(
        [string]$Server,
        [string]$Database
    )
    
    $query = "SELECT COUNT(*) FROM sys.databases WHERE name = '$Database'"
    try {
        $result = sqlcmd -S $Server -E -Q $query -h -1 -W 2>&1
        if ($LASTEXITCODE -eq 0) {
            return ($result -match "^1")
        }
        return $false
    }
    catch {
        return $false
    }
}

# Function to create database
function New-Database {
    param(
        [string]$Server,
        [string]$Database
    )
    
    $query = @"
IF NOT EXISTS (SELECT * FROM sys.databases WHERE name = '$Database')
BEGIN
    CREATE DATABASE [$Database];
    PRINT 'Database $Database created successfully';
END
ELSE
BEGIN
    PRINT 'Database $Database already exists';
END
"@
    
    try {
        sqlcmd -S $Server -E -Q $query
        Write-Host "✓ Database $Database created/verified" -ForegroundColor Green
        return $true
    }
    catch {
        Write-Host "✗ Error creating database $Database : $_" -ForegroundColor Red
        return $false
    }
}

# Main comparison logic
foreach ($dbName in $Databases) {
    Write-Host ""
    Write-Host "========================================" -ForegroundColor Cyan
    Write-Host "Processing Database: $dbName" -ForegroundColor Cyan
    Write-Host "========================================" -ForegroundColor Cyan
    
    # Check if source database exists
    if (-not (Test-DatabaseExists -Server $SourceServer -Database $dbName)) {
        Write-Host "⚠ Source database $dbName does not exist on $SourceServer" -ForegroundColor Yellow
        continue
    }
    
    # Create target database if it doesn't exist
    if (-not (Test-DatabaseExists -Server $TargetServer -Database $dbName)) {
        Write-Host "📝 Creating database $dbName on target server..." -ForegroundColor Yellow
        New-Database -Server $TargetServer -Database $dbName
    }
    else {
        Write-Host "✓ Database $dbName exists on target" -ForegroundColor Green
    }
    
    # Get tables from source
    Write-Host "📊 Getting table list from source..." -ForegroundColor Yellow
    $sourceTables = Get-DatabaseTables -Server $SourceServer -Database $dbName
    Write-Host "   Found $($sourceTables.Count) tables" -ForegroundColor Gray
    
    # Get tables from target
    Write-Host "📊 Getting table list from target..." -ForegroundColor Yellow
    $targetTables = Get-DatabaseTables -Server $TargetServer -Database $dbName
    Write-Host "   Found $($targetTables.Count) tables" -ForegroundColor Gray
    
    # Compare tables
    $missingTables = @()
    foreach ($sourceTable in $sourceTables) {
        $tableKey = "$($sourceTable.Schema).$($sourceTable.Name)"
        $found = $targetTables | Where-Object { $_.Schema -eq $sourceTable.Schema -and $_.Name -eq $sourceTable.Name }
        
        if (-not $found) {
            $missingTables += $sourceTable
            Write-Host "   ⚠ Missing table: $tableKey" -ForegroundColor Yellow
        }
        else {
            Write-Host "   ✓ Table exists: $tableKey" -ForegroundColor Green
            
            # Compare columns
            $sourceColumns = Get-TableColumns -Server $SourceServer -Database $dbName -Schema $sourceTable.Schema -TableName $sourceTable.Name
            $targetColumns = Get-TableColumns -Server $TargetServer -Database $dbName -Schema $sourceTable.Schema -TableName $sourceTable.Name
            
            $sourceColumnNames = $sourceColumns | Select-Object -ExpandProperty ColumnName
            $targetColumnNames = $targetColumns | Select-Object -ExpandProperty ColumnName
            
            $missingColumns = $sourceColumnNames | Where-Object { $_ -notin $targetColumnNames }
            if ($missingColumns) {
                Write-Host "      ⚠ Missing columns: $($missingColumns -join ', ')" -ForegroundColor Yellow
            }
        }
    }
    
    if ($missingTables.Count -eq 0) {
        Write-Host "✅ All tables exist in target database" -ForegroundColor Green
    }
    else {
        Write-Host "⚠ Found $($missingTables.Count) missing tables" -ForegroundColor Yellow
    }
}

Write-Host ""
Write-Host "========================================" -ForegroundColor Cyan
Write-Host "Comparison Complete" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""
Write-Host "Next steps:" -ForegroundColor Yellow
Write-Host "1. Review the differences above" -ForegroundColor Gray
Write-Host "2. Use SQL scripts to create missing tables/columns" -ForegroundColor Gray
Write-Host "3. Run this script again to verify replication" -ForegroundColor Gray


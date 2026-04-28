# Database Replication Verification Script
# Verifies replication status between source (localhost\NS_CIS) and target (localhost) instances
# Checks: Database existence, schema match, and data row counts

param(
    [string]$SourceInstance = "localhost\NS_CIS",
    [string]$TargetInstance = "localhost",
    [string[]]$Databases = @("NS_CIS", "ICUMS", "ICUMS_Downloads")
)

# Continues past errors intentionally: per-table source/target row count comparison across multiple DBs; per-table errors are recorded and reporting must continue.
$ErrorActionPreference = "Continue"

Write-Host "========================================" -ForegroundColor Cyan
Write-Host "Database Replication Verification" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host "Source: $SourceInstance" -ForegroundColor Yellow
Write-Host "Target: $TargetInstance" -ForegroundColor Yellow
Write-Host "Databases: $($Databases -join ', ')" -ForegroundColor Yellow
Write-Host "Generated: $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')" -ForegroundColor Gray
Write-Host ""

# Helper function to execute SQL queries
function Execute-SqlQuery {
    param(
        [string]$Instance,
        [string]$Database,
        [string]$Query
    )
    
    try {
        $tempFile = [System.IO.Path]::GetTempFileName() + ".sql"
        $outputFile = [System.IO.Path]::GetTempFileName()
        $Query | Out-File -FilePath $tempFile -Encoding UTF8
        
        $result = sqlcmd -S $Instance -E -d $Database -i $tempFile -W -h -1 -o $outputFile 2>&1
        Remove-Item $tempFile -Force -ErrorAction SilentlyContinue
        
        if (Test-Path $outputFile) {
            $content = Get-Content $outputFile -Raw
            Remove-Item $outputFile -Force -ErrorAction SilentlyContinue
            
            # Remove sqlcmd metadata lines like "(1 rows affected)"
            $lines = $content -split "`n" | Where-Object { 
                $_ -notmatch '^\(\d+ rows? affected\)' -and 
                $_.Trim() -ne '' -and
                $_ -notmatch '^\-{2,}' # Remove separator lines
            }
            
            return ($lines -join "`n").Trim()
        }
        return $null
    } catch {
        Write-Host "    Error executing query on $Instance : $_" -ForegroundColor Red
        return $null
    }
}

# Check if database exists
function Test-DatabaseExists {
    param([string]$Instance, [string]$Database)
    
    $query = "SELECT COUNT(*) FROM sys.databases WHERE name = '$Database'"
    $result = Execute-SqlQuery -Instance $Instance -Database "master" -Query $query
    
    if ($result) {
        # Extract just the number, removing any extra text
        $result = ($result -split "`n" | Select-Object -First 1).Trim()
        if ($result -match '^\s*(\d+)\s*') {
            return [int]$matches[1] -eq 1
        }
    }
    return $false
}

# Get table list
function Get-TableList {
    param([string]$Instance, [string]$Database)
    
    $query = @"
SELECT TABLE_SCHEMA, TABLE_NAME 
FROM INFORMATION_SCHEMA.TABLES 
WHERE TABLE_TYPE = 'BASE TABLE' 
  AND TABLE_NAME != '__EFMigrationsHistory'
ORDER BY TABLE_SCHEMA, TABLE_NAME
"@
    
    $result = Execute-SqlQuery -Instance $Instance -Database $Database -Query $query
    if (-not $result) { return @() }
    
    $tables = @()
    $lines = $result -split "`n" | Where-Object { 
        $_.Trim() -ne "" -and 
        $_ -notmatch '^\s*\-{2,}' -and
        $_ -notmatch 'rows? affected'
    }
    
    foreach ($line in $lines) {
        $trimmed = $line.Trim()
        if ($trimmed -match '^\s*(\S+)\s+(.+)$') {
            $tables += @{
                Schema = $matches[1].Trim()
                Name = $matches[2].Trim()
            }
        }
    }
    
    return $tables
}

# Get row count for a table
function Get-TableRowCount {
    param([string]$Instance, [string]$Database, [string]$Schema, [string]$Table)
    
    $query = "SELECT COUNT(*) FROM [$Schema].[$Table]"
    $result = Execute-SqlQuery -Instance $Instance -Database $Database -Query $query
    
    if ($result -and $result -match '^\s*(\d+)\s*$') {
        return [long]$matches[1]
    }
    return -1
}

# Get column count for schema comparison
function Get-TableColumnCount {
    param([string]$Instance, [string]$Database, [string]$Schema, [string]$Table)
    
    $query = @"
SELECT COUNT(*) 
FROM INFORMATION_SCHEMA.COLUMNS 
WHERE TABLE_SCHEMA = '$Schema' AND TABLE_NAME = '$Table'
"@
    
    $result = Execute-SqlQuery -Instance $Instance -Database $Database -Query $query
    if ($result -and $result -match '^\s*(\d+)\s*$') {
        return [int]$matches[1]
    }
    return -1
}

# Main verification logic
$totalResults = @{
    Databases = 0
    Complete = 0
    Incomplete = 0
    Missing = 0
    SchemaMismatches = 0
    Tables = @()
}

foreach ($db in $Databases) {
    Write-Host "DATABASE: $db" -ForegroundColor Cyan
    Write-Host ("=" * 70) -ForegroundColor Gray
    
    # Check if databases exist
    $sourceExists = Test-DatabaseExists -Instance $SourceInstance -Database $db
    $targetExists = Test-DatabaseExists -Instance $TargetInstance -Database $db
    
    if (-not $sourceExists) {
        Write-Host "  ❌ Source database does not exist!" -ForegroundColor Red
        Write-Host ""
        continue
    }
    
    if (-not $targetExists) {
        Write-Host "  ❌ Target database does not exist!" -ForegroundColor Red
        $totalResults.Missing++
        Write-Host ""
        continue
    }
    
    Write-Host "  ✅ Both databases exist" -ForegroundColor Green
    
    # Get table lists
    Write-Host "  Getting table list from source..." -ForegroundColor Gray
    $sourceTables = Get-TableList -Instance $SourceInstance -Database $db
    Write-Host "  Getting table list from target..." -ForegroundColor Gray
    $targetTables = Get-TableList -Instance $TargetInstance -Database $db
    
    $targetTableDict = @{}
    foreach ($tbl in $targetTables) {
        $key = "$($tbl.Schema).$($tbl.Name)"
        $targetTableDict[$key] = $tbl
    }
    
    Write-Host "  Source tables: $($sourceTables.Count)" -ForegroundColor Gray
    Write-Host "  Target tables: $($targetTables.Count)" -ForegroundColor Gray
    Write-Host ""
    
    Write-Host "  Verifying tables..." -ForegroundColor Yellow
    Write-Host ("  " + ("-" * 68)) -ForegroundColor Gray
    Write-Host ("  {0,-50} {1,10} {2,10} {3,10}" -f "Table", "Source", "Target", "Status") -ForegroundColor Gray
    Write-Host ("  " + ("-" * 68)) -ForegroundColor Gray
    
    $dbComplete = 0
    $dbIncomplete = 0
    $dbMissing = 0
    
    foreach ($sourceTable in $sourceTables) {
        $tableKey = "$($sourceTable.Schema).$($sourceTable.Name)"
        $fullTableName = "[$($sourceTable.Schema)].[$($sourceTable.Name)]"
        
        # Check if table exists in target
        if (-not $targetTableDict.ContainsKey($tableKey)) {
            $status = "Missing"
            $color = "Red"
            $dbMissing++
            $sourceCount = Get-TableRowCount -Instance $SourceInstance -Database $db -Schema $sourceTable.Schema -Table $sourceTable.Name
            $targetCount = 0
            Write-Host ("  {0,-50} {1,10} {2,10} {3,10}" -f $fullTableName, $sourceCount, "-", $status) -ForegroundColor $color
        } else {
            # Table exists, check row counts
            $sourceCount = Get-TableRowCount -Instance $SourceInstance -Database $db -Schema $sourceTable.Schema -Table $sourceTable.Name
            $targetCount = Get-TableRowCount -Instance $TargetInstance -Database $db -Schema $sourceTable.Schema -Table $sourceTable.Name
            
            # Check schema (column count)
            $sourceColCount = Get-TableColumnCount -Instance $SourceInstance -Database $db -Schema $sourceTable.Schema -Table $sourceTable.Name
            $targetColCount = Get-TableColumnCount -Instance $TargetInstance -Database $db -Schema $sourceTable.Schema -Table $sourceTable.Name
            
            if ($sourceCount -eq $targetCount -and $sourceColCount -eq $targetColCount) {
                $status = "Complete"
                $color = "Green"
                $dbComplete++
            } elseif ($sourceColCount -ne $targetColCount) {
                $status = "Schema Mismatch"
                $color = "Red"
                $dbIncomplete++
                $totalResults.SchemaMismatches++
            } else {
                $status = "Incomplete"
                $color = "Yellow"
                $dbIncomplete++
            }
            
            Write-Host ("  {0,-50} {1,10} {2,10} {3,10}" -f $fullTableName, $sourceCount, $targetCount, $status) -ForegroundColor $color
            
            $totalResults.Tables += @{
                Database = $db
                Table = $fullTableName
                SourceCount = $sourceCount
                TargetCount = $targetCount
                SourceCols = $sourceColCount
                TargetCols = $targetColCount
                Status = $status
            }
        }
    }
    
    Write-Host ("  " + ("-" * 68)) -ForegroundColor Gray
    Write-Host ("  Summary: Complete: $dbComplete, Incomplete: $dbIncomplete, Missing: $dbMissing") -ForegroundColor $(if ($dbIncomplete -eq 0 -and $dbMissing -eq 0) { "Green" } else { "Yellow" })
    Write-Host ""
    
    $totalResults.Databases++
    $totalResults.Complete += $dbComplete
    $totalResults.Incomplete += $dbIncomplete
    $totalResults.Missing += $dbMissing
}

# Overall Summary
Write-Host "========================================" -ForegroundColor Cyan
Write-Host "OVERALL SUMMARY" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host "Databases Checked: $($totalResults.Databases)" -ForegroundColor Gray
Write-Host "Complete Tables: $($totalResults.Complete)" -ForegroundColor Green
Write-Host "Incomplete Tables: $($totalResults.Incomplete)" -ForegroundColor Yellow
Write-Host "Missing Tables: $($totalResults.Missing)" -ForegroundColor Red
Write-Host "Schema Mismatches: $($totalResults.SchemaMismatches)" -ForegroundColor Red
Write-Host ""

if ($totalResults.Incomplete -eq 0 -and $totalResults.Missing -eq 0 -and $totalResults.SchemaMismatches -eq 0) {
    Write-Host "✅ REPLICATION IS COMPLETE!" -ForegroundColor Green
    exit 0
} else {
    Write-Host "⚠️  REPLICATION IS INCOMPLETE" -ForegroundColor Yellow
    Write-Host ""
    Write-Host "Issues found:" -ForegroundColor Yellow
    if ($totalResults.Missing -gt 0) {
        Write-Host "  - $($totalResults.Missing) missing table(s)" -ForegroundColor Red
    }
    if ($totalResults.Incomplete -gt 0) {
        Write-Host "  - $($totalResults.Incomplete) incomplete table(s) (data mismatch)" -ForegroundColor Yellow
    }
    if ($totalResults.SchemaMismatches -gt 0) {
        Write-Host "  - $($totalResults.SchemaMismatches) schema mismatch(es)" -ForegroundColor Red
    }
    exit 1
}


# Check AnalysisGroups Table Constraints
# This script checks what constraints exist on the AnalysisGroups table

# Load connection string from appsettings.json
$appsettingsPath = "src\NickScanCentralImagingPortal.API\appsettings.json"
if (-not (Test-Path $appsettingsPath)) {
    Write-Host "Error: appsettings.json not found" -ForegroundColor Red
    exit 1
}

$appsettings = Get-Content $appsettingsPath | ConvertFrom-Json
$connString = $appsettings.ConnectionStrings.NS_CIS_Connection

# Parse connection string
$builder = New-Object System.Data.SqlClient.SqlConnectionStringBuilder($connString)
$server = $builder.DataSource
$database = $builder.InitialCatalog
$serverParts = $server -split ","
$serverName = $serverParts[0]
$port = if ($serverParts.Length -gt 1) { $serverParts[1] } else { "1433" }

$sqlcmdPath = "C:\Program Files\Microsoft SQL Server\Client SDK\ODBC\170\Tools\Binn\SQLCMD.EXE"
if (-not (Test-Path $sqlcmdPath)) {
    Write-Host "Error: sqlcmd.exe not found" -ForegroundColor Red
    exit 1
}

Write-Host "`n=== CHECKING ANALYSISGROUPS TABLE CONSTRAINTS ===" -ForegroundColor Cyan
Write-Host ""

# Query 1: Check indexes on AnalysisGroups
Write-Host "[1/3] Checking indexes on AnalysisGroups table..." -ForegroundColor Yellow
$sql1 = @"
SELECT 
    i.name AS IndexName,
    i.type_desc AS IndexType,
    i.is_unique AS IsUnique,
    i.is_primary_key AS IsPrimaryKey,
    STRING_AGG(c.name, ', ') WITHIN GROUP (ORDER BY ic.key_ordinal) AS ColumnNames
FROM sys.indexes i
INNER JOIN sys.index_columns ic ON i.object_id = ic.object_id AND i.index_id = ic.index_id
INNER JOIN sys.columns c ON ic.object_id = c.object_id AND ic.column_id = c.column_id
WHERE i.object_id = OBJECT_ID('AnalysisGroups')
GROUP BY i.name, i.type_desc, i.is_unique, i.is_primary_key
ORDER BY i.is_primary_key DESC, i.is_unique DESC, i.name;
"@

# Use alternative query for SQL Server 2014 (no STRING_AGG)
$sql1Alt = @"
SELECT 
    i.name AS IndexName,
    i.type_desc AS IndexType,
    i.is_unique AS IsUnique,
    i.is_primary_key AS IsPrimaryKey,
    c.name AS ColumnName,
    ic.key_ordinal AS ColumnOrder
FROM sys.indexes i
INNER JOIN sys.index_columns ic ON i.object_id = ic.object_id AND i.index_id = ic.index_id
INNER JOIN sys.columns c ON ic.object_id = c.object_id AND ic.column_id = c.column_id
WHERE i.object_id = OBJECT_ID('AnalysisGroups')
ORDER BY i.name, ic.key_ordinal;
"@

$results1 = & $sqlcmdPath -S "$serverName,$port" -d $database -E -Q $sql1Alt -W -s "," | Out-String
$dataLines1 = $results1.Split("`n") | Where-Object { 
    $_ -notmatch '^-+$' -and 
    $_ -notmatch '^IndexName,' -and 
    $_ -notmatch 'rows affected' -and 
    $_ -notmatch '^$' -and
    $_.Trim() -ne '' 
}

if ($dataLines1.Count -gt 0) {
    Write-Host "Indexes on AnalysisGroups:" -ForegroundColor Cyan
    Write-Host "IndexName,IndexType,IsUnique,IsPrimaryKey,ColumnName,ColumnOrder" -ForegroundColor Yellow
    $dataLines1 | ForEach-Object { Write-Host $_ -ForegroundColor White }
} else {
    Write-Host "No indexes found" -ForegroundColor Red
}

# Query 2: Check for unique constraints
Write-Host "`n[2/3] Checking unique constraints..." -ForegroundColor Yellow
$sql2 = @"
SELECT 
    kc.name AS ConstraintName,
    kc.type_desc AS ConstraintType,
    c.name AS ColumnName,
    kc.is_system_named AS IsSystemNamed
FROM sys.key_constraints kc
INNER JOIN sys.index_columns ic ON kc.parent_object_id = ic.object_id AND kc.unique_index_id = ic.index_id
INNER JOIN sys.columns c ON ic.object_id = c.object_id AND ic.column_id = c.column_id
WHERE kc.parent_object_id = OBJECT_ID('AnalysisGroups')
    AND kc.type = 'UQ'  -- Unique constraint
ORDER BY kc.name, ic.key_ordinal;
"@

$results2 = & $sqlcmdPath -S "$serverName,$port" -d $database -E -Q $sql2 -W -s "," | Out-String
$dataLines2 = $results2.Split("`n") | Where-Object { 
    $_ -notmatch '^-+$' -and 
    $_ -notmatch '^ConstraintName,' -and 
    $_ -notmatch 'rows affected' -and 
    $_ -notmatch '^$' -and
    $_.Trim() -ne '' 
}

if ($dataLines2.Count -gt 0) {
    Write-Host "Unique Constraints:" -ForegroundColor Cyan
    Write-Host "ConstraintName,ConstraintType,ColumnName,IsSystemNamed" -ForegroundColor Yellow
    $dataLines2 | ForEach-Object { Write-Host $_ -ForegroundColor White }
} else {
    Write-Host "No unique constraints found (other than primary key)" -ForegroundColor Green
}

# Query 3: Check for duplicate GroupIdentifiers with different ScannerTypes
Write-Host "`n[3/3] Checking for duplicate GroupIdentifiers with different ScannerTypes..." -ForegroundColor Yellow
$sql3 = @"
SELECT 
    GroupIdentifier,
    COUNT(DISTINCT ScannerType) as ScannerTypeCount,
    COUNT(*) as TotalGroups,
    STRING_AGG(ScannerType, ', ') AS ScannerTypes
FROM AnalysisGroups
WHERE GroupIdentifier IS NOT NULL
GROUP BY GroupIdentifier
HAVING COUNT(DISTINCT ScannerType) > 1
ORDER BY ScannerTypeCount DESC, GroupIdentifier;
"@

# Use alternative for SQL Server 2014
$sql3Alt = @"
SELECT 
    ag1.GroupIdentifier,
    COUNT(DISTINCT ag1.ScannerType) as ScannerTypeCount,
    COUNT(*) as TotalGroups,
    MIN(ag1.ScannerType) as ScannerType1,
    MAX(ag1.ScannerType) as ScannerType2
FROM AnalysisGroups ag1
WHERE ag1.GroupIdentifier IS NOT NULL
GROUP BY ag1.GroupIdentifier
HAVING COUNT(DISTINCT ag1.ScannerType) > 1
ORDER BY ScannerTypeCount DESC, ag1.GroupIdentifier;
"@

$results3 = & $sqlcmdPath -S "$serverName,$port" -d $database -E -Q $sql3Alt -W -s "," | Out-String
$dataLines3 = $results3.Split("`n") | Where-Object { 
    $_ -notmatch '^-+$' -and 
    $_ -notmatch '^GroupIdentifier,' -and 
    $_ -notmatch 'rows affected' -and 
    $_ -notmatch '^$' -and
    $_.Trim() -ne '' 
}

if ($dataLines3.Count -gt 0) {
    Write-Host "❌ FOUND duplicate GroupIdentifiers with different ScannerTypes:" -ForegroundColor Red
    Write-Host "GroupIdentifier,ScannerTypeCount,TotalGroups,ScannerType1,ScannerType2" -ForegroundColor Yellow
    $dataLines3 | ForEach-Object { Write-Host $_ -ForegroundColor Red }
    Write-Host "`nTotal: $($dataLines3.Count) GroupIdentifiers have multiple ScannerTypes" -ForegroundColor Red
} else {
    Write-Host "✅ No duplicate GroupIdentifiers with different ScannerTypes found" -ForegroundColor Green
}

Write-Host "`n=== ANALYSIS COMPLETE ===" -ForegroundColor Cyan
Write-Host "Done!" -ForegroundColor Green


# Get Detailed Index Information for AnalysisGroups
# This script gets the actual index names and details

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

Write-Host "`n=== ANALYSISGROUPS INDEX DETAILS ===" -ForegroundColor Cyan
Write-Host ""

# Query: Get all indexes with their columns
$sql = @"
SELECT 
    i.name AS IndexName,
    i.type_desc AS IndexType,
    CASE WHEN i.is_unique = 1 THEN 'YES' ELSE 'NO' END AS IsUnique,
    CASE WHEN i.is_primary_key = 1 THEN 'YES' ELSE 'NO' END AS IsPrimaryKey,
    c.name AS ColumnName,
    ic.key_ordinal AS ColumnOrder,
    CASE WHEN ic.is_descending_key = 1 THEN 'DESC' ELSE 'ASC' END AS SortOrder
FROM sys.indexes i
INNER JOIN sys.index_columns ic ON i.object_id = ic.object_id AND i.index_id = ic.index_id
INNER JOIN sys.columns c ON ic.object_id = c.object_id AND ic.column_id = c.column_id
WHERE i.object_id = OBJECT_ID('AnalysisGroups')
ORDER BY i.name, ic.key_ordinal;
"@

$results = & $sqlcmdPath -S "$serverName,$port" -d $database -E -Q $sql -W -s "," | Out-String
$dataLines = $results.Split("`n") | Where-Object { 
    $_ -notmatch '^-+$' -and 
    $_ -notmatch '^IndexName,' -and 
    $_ -notmatch 'rows affected' -and 
    $_ -notmatch '^$' -and
    $_.Trim() -ne '' 
}

if ($dataLines.Count -gt 0) {
    Write-Host "Indexes on AnalysisGroups:" -ForegroundColor Cyan
    Write-Host "IndexName,IndexType,IsUnique,IsPrimaryKey,ColumnName,ColumnOrder,SortOrder" -ForegroundColor Yellow
    $dataLines | ForEach-Object { Write-Host $_ -ForegroundColor White }
    
    # Check for unique index on GroupIdentifier only
    $uniqueGroupIdIndex = $dataLines | Where-Object { 
        $_ -match 'GroupIdentifier' -and 
        ($_ -match 'YES.*IsUnique' -or $_ -match 'IsUnique.*YES')
    }
    
    if ($uniqueGroupIdIndex) {
        Write-Host "`n❌ FOUND: Unique index on GroupIdentifier only" -ForegroundColor Red
        Write-Host "This prevents multiple groups with same GroupIdentifier but different ScannerTypes" -ForegroundColor Red
    } else {
        Write-Host "`n✅ No unique index on GroupIdentifier only found" -ForegroundColor Green
    }
} else {
    Write-Host "No indexes found (unexpected)" -ForegroundColor Red
}

Write-Host "`nDone!" -ForegroundColor Green


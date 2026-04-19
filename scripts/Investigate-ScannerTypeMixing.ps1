# Investigate Why Containers Have Multiple Scanner Types
# This script investigates the root cause of scanner type mixing

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

Write-Host "`n=== INVESTIGATING SCANNER TYPE MIXING ===" -ForegroundColor Cyan
Write-Host ""

# Query 1: Check if containers exist in BOTH scanner tables
Write-Host "[1/5] Checking if containers exist in BOTH AseScans and FS6000Scans..." -ForegroundColor Yellow
$sql1 = @"
SELECT 
    ccs.ContainerNumber,
    ccs.GroupIdentifier,
    CASE WHEN ase.ContainerNumber IS NOT NULL THEN 1 ELSE 0 END AS InAseScans,
    CASE WHEN fs.ContainerNumber IS NOT NULL THEN 1 ELSE 0 END AS InFs6000Scans,
    CASE WHEN ase.ContainerNumber IS NOT NULL AND fs.ContainerNumber IS NOT NULL THEN 'BOTH' ELSE 'ONE' END AS ScannerPresence
FROM ContainerCompletenessStatuses ccs
LEFT JOIN AseScans ase ON ase.ContainerNumber = ccs.ContainerNumber
LEFT JOIN FS6000Scans fs ON fs.ContainerNumber = ccs.ContainerNumber
WHERE ccs.GroupIdentifier IN (
    SELECT GroupIdentifier
    FROM ContainerCompletenessStatuses
    WHERE GroupIdentifier IS NOT NULL
    GROUP BY GroupIdentifier
    HAVING COUNT(DISTINCT ScannerType) > 1
)
GROUP BY ccs.ContainerNumber, ccs.GroupIdentifier, ase.ContainerNumber, fs.ContainerNumber
HAVING CASE WHEN ase.ContainerNumber IS NOT NULL AND fs.ContainerNumber IS NOT NULL THEN 1 ELSE 0 END = 1
ORDER BY ccs.GroupIdentifier, ccs.ContainerNumber;
"@

$results1 = & $sqlcmdPath -S "$serverName,$port" -d $database -E -Q $sql1 -W -s "," | Out-String
$dataLines1 = $results1.Split("`n") | Where-Object { 
    $_ -notmatch '^-+$' -and 
    $_ -notmatch '^ContainerNumber,' -and 
    $_ -notmatch 'rows affected' -and 
    $_ -notmatch '^$' -and
    $_.Trim() -ne '' 
}

if ($dataLines1.Count -gt 0) {
    Write-Host "❌ FOUND containers in BOTH scanner tables:" -ForegroundColor Red
    Write-Host "ContainerNumber,GroupIdentifier,InAseScans,InFs6000Scans,ScannerPresence" -ForegroundColor Yellow
    $dataLines1 | ForEach-Object { Write-Host $_ -ForegroundColor Red }
    Write-Host "`nTotal: $($dataLines1.Count) containers exist in BOTH scanner tables" -ForegroundColor Red
} else {
    Write-Host "✅ No containers found in BOTH scanner tables" -ForegroundColor Green
}

# Query 2: Check ContainerCompletenessStatus records for mixed groups
Write-Host "`n[2/5] Checking ContainerCompletenessStatus records for mixed groups..." -ForegroundColor Yellow
$sql2 = @"
SELECT 
    GroupIdentifier,
    ContainerNumber,
    ScannerType,
    InspectionId,
    ScanDate,
    HasImageData,
    CreatedAt
FROM ContainerCompletenessStatuses
WHERE GroupIdentifier IN (
    SELECT GroupIdentifier
    FROM ContainerCompletenessStatuses
    WHERE GroupIdentifier IS NOT NULL
    GROUP BY GroupIdentifier
    HAVING COUNT(DISTINCT ScannerType) > 1
)
ORDER BY GroupIdentifier, ContainerNumber, ScannerType;
"@

$results2 = & $sqlcmdPath -S "$serverName,$port" -d $database -E -Q $sql2 -W -s "," | Out-String
$dataLines2 = $results2.Split("`n") | Where-Object { 
    $_ -notmatch '^-+$' -and 
    $_ -notmatch '^GroupIdentifier,' -and 
    $_ -notmatch 'rows affected' -and 
    $_ -notmatch '^$' -and
    $_.Trim() -ne '' 
}

if ($dataLines2.Count -gt 0) {
    Write-Host "❌ FOUND ContainerCompletenessStatus records with mixed scanner types:" -ForegroundColor Red
    Write-Host "GroupIdentifier,ContainerNumber,ScannerType,InspectionId,ScanDate,HasImageData,CreatedAt" -ForegroundColor Yellow
    $dataLines2 | Select-Object -First 20 | ForEach-Object { Write-Host $_ -ForegroundColor Red }
    if ($dataLines2.Count -gt 20) {
        Write-Host "... and $($dataLines2.Count - 20) more records" -ForegroundColor DarkGray
    }
    Write-Host "`nTotal: $($dataLines2.Count) records" -ForegroundColor Red
} else {
    Write-Host "✅ No mixed records found" -ForegroundColor Green
}

# Query 3: Check if same container has multiple ContainerCompletenessStatus records with different ScannerTypes
Write-Host "`n[3/5] Checking if same container has multiple records with different ScannerTypes..." -ForegroundColor Yellow
$sql3 = @"
SELECT 
    ContainerNumber,
    COUNT(DISTINCT ScannerType) as ScannerTypeCount,
    MIN(ScannerType) as ScannerType1,
    MAX(ScannerType) as ScannerType2,
    COUNT(*) as TotalRecords,
    STRING_AGG(GroupIdentifier, ', ') as GroupIdentifiers
FROM ContainerCompletenessStatuses
WHERE ContainerNumber IN (
    SELECT ContainerNumber
    FROM ContainerCompletenessStatuses
    GROUP BY ContainerNumber
    HAVING COUNT(DISTINCT ScannerType) > 1
)
GROUP BY ContainerNumber
ORDER BY ScannerTypeCount DESC, ContainerNumber;
"@

$results3 = & $sqlcmdPath -S "$serverName,$port" -d $database -E -Q $sql3 -W -s "," | Out-String
$dataLines3 = $results3.Split("`n") | Where-Object { 
    $_ -notmatch '^-+$' -and 
    $_ -notmatch '^ContainerNumber,' -and 
    $_ -notmatch 'rows affected' -and 
    $_ -notmatch '^$' -and
    $_.Trim() -ne '' 
}

if ($dataLines3.Count -gt 0) {
    Write-Host "❌ FOUND containers with multiple ScannerType records:" -ForegroundColor Red
    Write-Host "ContainerNumber,ScannerTypeCount,ScannerType1,ScannerType2,TotalRecords,GroupIdentifiers" -ForegroundColor Yellow
    $dataLines3 | ForEach-Object { Write-Host $_ -ForegroundColor Red }
    Write-Host "`nTotal: $($dataLines3.Count) containers have multiple ScannerType records" -ForegroundColor Red
} else {
    Write-Host "✅ No containers with multiple ScannerType records" -ForegroundColor Green
}

# Query 4: Check ContainerScanQueue for duplicate entries
Write-Host "`n[4/5] Checking ContainerScanQueue for same container with different ScannerTypes..." -ForegroundColor Yellow
$sql4 = @"
SELECT 
    ContainerNumber,
    COUNT(DISTINCT ScannerType) as ScannerTypeCount,
    MIN(ScannerType) as ScannerType1,
    MAX(ScannerType) as ScannerType2,
    COUNT(*) as TotalQueueItems,
    MIN(QueuedAt) as FirstQueued,
    MAX(QueuedAt) as LastQueued
FROM ContainerScanQueues
WHERE ContainerNumber IN (
    SELECT ContainerNumber
    FROM ContainerScanQueues
    GROUP BY ContainerNumber
    HAVING COUNT(DISTINCT ScannerType) > 1
)
GROUP BY ContainerNumber
ORDER BY ScannerTypeCount DESC, ContainerNumber;
"@

$results4 = & $sqlcmdPath -S "$serverName,$port" -d $database -E -Q $sql4 -W -s "," | Out-String
$dataLines4 = $results4.Split("`n") | Where-Object { 
    $_ -notmatch '^-+$' -and 
    $_ -notmatch '^ContainerNumber,' -and 
    $_ -notmatch 'rows affected' -and 
    $_ -notmatch '^$' -and
    $_.Trim() -ne '' 
}

if ($dataLines4.Count -gt 0) {
    Write-Host "❌ FOUND queue items with same container but different ScannerTypes:" -ForegroundColor Red
    Write-Host "ContainerNumber,ScannerTypeCount,ScannerType1,ScannerType2,TotalQueueItems,FirstQueued,LastQueued" -ForegroundColor Yellow
    $dataLines4 | ForEach-Object { Write-Host $_ -ForegroundColor Red }
    Write-Host "`nTotal: $($dataLines4.Count) containers queued with multiple ScannerTypes" -ForegroundColor Red
} else {
    Write-Host "✅ No queue items with multiple ScannerTypes" -ForegroundColor Green
}

# Query 5: Sample one mixed group to see details
Write-Host "`n[5/5] Sample analysis of one mixed group..." -ForegroundColor Yellow
$sql5 = @"
SELECT TOP 1
    ccs.GroupIdentifier,
    ccs.ContainerNumber,
    ccs.ScannerType,
    ccs.InspectionId,
    ccs.ScanDate,
    ccs.HasImageData,
    CASE WHEN ase.ContainerNumber IS NOT NULL THEN 'YES' ELSE 'NO' END AS InAseScans,
    CASE WHEN fs.ContainerNumber IS NOT NULL THEN 'YES' ELSE 'NO' END AS InFs6000Scans
FROM ContainerCompletenessStatuses ccs
LEFT JOIN AseScans ase ON ase.ContainerNumber = ccs.ContainerNumber
LEFT JOIN FS6000Scans fs ON fs.ContainerNumber = ccs.ContainerNumber
WHERE ccs.GroupIdentifier IN (
    SELECT TOP 1 GroupIdentifier
    FROM ContainerCompletenessStatuses
    WHERE GroupIdentifier IS NOT NULL
    GROUP BY GroupIdentifier
    HAVING COUNT(DISTINCT ScannerType) > 1
    ORDER BY GroupIdentifier
)
ORDER BY ccs.ContainerNumber, ccs.ScannerType;
"@

$results5 = & $sqlcmdPath -S "$serverName,$port" -d $database -E -Q $sql5 -W -s "," | Out-String
$dataLines5 = $results5.Split("`n") | Where-Object { 
    $_ -notmatch '^-+$' -and 
    $_ -notmatch '^GroupIdentifier,' -and 
    $_ -notmatch 'rows affected' -and 
    $_ -notmatch '^$' -and
    $_.Trim() -ne '' 
}

if ($dataLines5.Count -gt 0) {
    Write-Host "Sample mixed group details:" -ForegroundColor Cyan
    Write-Host "GroupIdentifier,ContainerNumber,ScannerType,InspectionId,ScanDate,HasImageData,InAseScans,InFs6000Scans" -ForegroundColor Yellow
    $dataLines5 | ForEach-Object { Write-Host $_ -ForegroundColor White }
}

Write-Host "`n=== SUMMARY ===" -ForegroundColor Cyan
Write-Host "Investigation complete. Review results above to identify root cause." -ForegroundColor White
Write-Host "`nDone!" -ForegroundColor Green


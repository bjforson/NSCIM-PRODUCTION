# Check Scanner Type Mixing in AnalysisGroups
# This script verifies if any AnalysisGroups have containers with different scanner types
# which would indicate a design flaw

# Load connection string from appsettings.json
$appsettingsPath = "src\NickScanCentralImagingPortal.API\appsettings.json"
if (-not (Test-Path $appsettingsPath)) {
    Write-Host "Error: appsettings.json not found at $appsettingsPath" -ForegroundColor Red
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
    Write-Host "Error: sqlcmd.exe not found at $sqlcmdPath. Please ensure SQL Server Client Tools are installed." -ForegroundColor Red
    exit 1
}

Write-Host "`n=== SCANNER TYPE MIXING CHECK ===" -ForegroundColor Cyan
Write-Host "Server: $serverName,$port" -ForegroundColor DarkCyan
Write-Host "Database: $database" -ForegroundColor DarkCyan
Write-Host ""

# Query 1: Check if any GroupIdentifier has containers with different ScannerTypes in ContainerCompletenessStatuses
Write-Host "`n[1/3] Checking ContainerCompletenessStatuses for mixed scanner types..." -ForegroundColor Yellow
$sql1 = @"
SELECT 
    GroupIdentifier, 
    COUNT(DISTINCT ScannerType) as ScannerTypeCount,
    MIN(ScannerType) as ScannerType1,
    MAX(ScannerType) as ScannerType2
FROM ContainerCompletenessStatuses
WHERE GroupIdentifier IS NOT NULL 
    AND GroupIdentifier <> ''
GROUP BY GroupIdentifier
HAVING COUNT(DISTINCT ScannerType) > 1
ORDER BY ScannerTypeCount DESC, GroupIdentifier;
"@

Write-Host "Running query..." -ForegroundColor DarkGray
$results1 = & $sqlcmdPath -S "$serverName,$port" -d $database -E -Q $sql1 -W -s "," | Out-String

# Filter out header and empty lines
$dataLines1 = $results1.Split("`n") | Where-Object { 
    $_ -notmatch '^-+$' -and 
    $_ -notmatch '^GroupIdentifier,' -and 
    $_ -notmatch 'rows affected' -and 
    $_ -notmatch '^$' -and
    $_.Trim() -ne '' 
}

if ($dataLines1.Count -gt 0) {
    Write-Host "`n❌ FOUND MIXED SCANNER TYPES in ContainerCompletenessStatuses:" -ForegroundColor Red
    Write-Host "GroupIdentifier,ScannerTypeCount,ScannerType1,ScannerType2" -ForegroundColor Yellow
    $dataLines1 | ForEach-Object {
        Write-Host $_ -ForegroundColor Red
    }
    Write-Host "`nTotal groups with mixed scanner types: $($dataLines1.Count)" -ForegroundColor Red
} else {
    Write-Host "✅ No mixed scanner types found in ContainerCompletenessStatuses" -ForegroundColor Green
}

# Query 2: Check if any AnalysisGroup has AnalysisRecords with different ScannerTypes
Write-Host "`n[2/3] Checking AnalysisGroups for mixed scanner types in AnalysisRecords..." -ForegroundColor Yellow
$sql2 = @"
SELECT 
    ag.GroupIdentifier, 
    ag.ScannerType as GroupScannerType,
    COUNT(DISTINCT ar.ScannerType) as RecordScannerTypeCount,
    MIN(ar.ScannerType) as RecordScannerType1,
    MAX(ar.ScannerType) as RecordScannerType2,
    COUNT(DISTINCT ar.ContainerNumber) as ContainerCount
FROM AnalysisGroups ag
INNER JOIN AnalysisRecords ar ON ar.GroupId = ag.Id
GROUP BY ag.GroupIdentifier, ag.ScannerType
HAVING COUNT(DISTINCT ar.ScannerType) > 1
ORDER BY RecordScannerTypeCount DESC, ag.GroupIdentifier;
"@

Write-Host "Running query..." -ForegroundColor DarkGray
$results2 = & $sqlcmdPath -S "$serverName,$port" -d $database -E -Q $sql2 -W -s "," | Out-String

# Filter out header and empty lines
$dataLines2 = $results2.Split("`n") | Where-Object { 
    $_ -notmatch '^-+$' -and 
    $_ -notmatch '^GroupIdentifier,' -and 
    $_ -notmatch 'rows affected' -and 
    $_ -notmatch '^$' -and
    $_.Trim() -ne '' 
}

if ($dataLines2.Count -gt 0) {
    Write-Host "`n❌ FOUND MIXED SCANNER TYPES in AnalysisGroups:" -ForegroundColor Red
    Write-Host "GroupIdentifier,GroupScannerType,RecordScannerTypeCount,RecordScannerType1,RecordScannerType2,ContainerCount" -ForegroundColor Yellow
    $dataLines2 | ForEach-Object {
        Write-Host $_ -ForegroundColor Red
    }
    Write-Host "`nTotal groups with mixed scanner types: $($dataLines2.Count)" -ForegroundColor Red
} else {
    Write-Host "✅ No mixed scanner types found in AnalysisGroups" -ForegroundColor Green
}

# Query 3: Check if any AnalysisGroup has ImageAnalysisDecisions with different ScannerTypes
Write-Host "`n[3/3] Checking ImageAnalysisDecisions for mixed scanner types per group..." -ForegroundColor Yellow
$sql3 = @"
SELECT 
    ag.GroupIdentifier,
    ag.ScannerType as GroupScannerType,
    COUNT(DISTINCT iad.ScannerType) as DecisionScannerTypeCount,
    MIN(iad.ScannerType) as DecisionScannerType1,
    MAX(iad.ScannerType) as DecisionScannerType2,
    COUNT(DISTINCT iad.ContainerNumber) as ContainerCount
FROM AnalysisGroups ag
INNER JOIN AnalysisRecords ar ON ar.GroupId = ag.Id
INNER JOIN ImageAnalysisDecisions iad ON iad.ContainerNumber = ar.ContainerNumber 
    AND iad.GroupIdentifier = ag.GroupIdentifier
GROUP BY ag.GroupIdentifier, ag.ScannerType
HAVING COUNT(DISTINCT iad.ScannerType) > 1
ORDER BY DecisionScannerTypeCount DESC, ag.GroupIdentifier;
"@

Write-Host "Running query..." -ForegroundColor DarkGray
$results3 = & $sqlcmdPath -S "$serverName,$port" -d $database -E -Q $sql3 -W -s "," | Out-String

# Filter out header and empty lines
$dataLines3 = $results3.Split("`n") | Where-Object { 
    $_ -notmatch '^-+$' -and 
    $_ -notmatch '^GroupIdentifier,' -and 
    $_ -notmatch 'rows affected' -and 
    $_ -notmatch '^$' -and
    $_.Trim() -ne '' 
}

if ($dataLines3.Count -gt 0) {
    Write-Host "`n❌ FOUND MIXED SCANNER TYPES in ImageAnalysisDecisions:" -ForegroundColor Red
    Write-Host "GroupIdentifier,GroupScannerType,DecisionScannerTypeCount,DecisionScannerType1,DecisionScannerType2,ContainerCount" -ForegroundColor Yellow
    $dataLines3 | ForEach-Object {
        Write-Host $_ -ForegroundColor Red
    }
    Write-Host "`nTotal groups with mixed scanner types in decisions: $($dataLines3.Count)" -ForegroundColor Red
} else {
    Write-Host "✅ No mixed scanner types found in ImageAnalysisDecisions" -ForegroundColor Green
}

# Summary
Write-Host "`n=== SUMMARY ===" -ForegroundColor Cyan
$totalIssues = $dataLines1.Count + $dataLines2.Count + $dataLines3.Count

if ($totalIssues -eq 0) {
    Write-Host "✅ NO DESIGN FLAW DETECTED" -ForegroundColor Green
    Write-Host "All groups have consistent scanner types across all containers." -ForegroundColor Green
} else {
    Write-Host "❌ DESIGN FLAW DETECTED" -ForegroundColor Red
    Write-Host "Total issues found: $totalIssues" -ForegroundColor Red
    Write-Host "`nRecommendation: Investigate and fix the root cause of scanner type mixing." -ForegroundColor Yellow
    Write-Host "Possible causes:" -ForegroundColor Yellow
    Write-Host "  1. Manual intake with incorrect scanner type" -ForegroundColor Yellow
    Write-Host "  2. ContainerCompletenessStatus has wrong scanner type" -ForegroundColor Yellow
    Write-Host "  3. Database constraint allows mixing (unique index on GroupIdentifier only)" -ForegroundColor Yellow
}

Write-Host "`nDone!" -ForegroundColor Green


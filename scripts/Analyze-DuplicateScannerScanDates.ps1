# Analyze Scan Dates for Containers in Both Scanner Tables
# This helps understand the pattern to build fail-safe logic

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

Write-Host "`n=== ANALYZING SCAN DATES FOR DUPLICATE SCANNER CONTAINERS ===" -ForegroundColor Cyan
Write-Host ""

# Query 1: Get scan dates from both scanner tables for containers in both
Write-Host "[1/4] Getting scan dates from both scanner tables..." -ForegroundColor Yellow
$sql1 = @"
SELECT 
    c.ContainerNumber,
    ase.ScanTime as AseScanTime,
    fs.ScanTime as Fs6000ScanTime,
    DATEDIFF(MINUTE, ase.ScanTime, fs.ScanTime) as TimeDifferenceMinutes,
    CASE 
        WHEN ase.ScanTime < fs.ScanTime THEN 'ASE_FIRST'
        WHEN fs.ScanTime < ase.ScanTime THEN 'FS6000_FIRST'
        WHEN ase.ScanTime = fs.ScanTime THEN 'SAME_TIME'
        ELSE 'UNKNOWN'
    END as ScanOrder,
    CASE 
        WHEN ABS(DATEDIFF(MINUTE, ase.ScanTime, fs.ScanTime)) <= 5 THEN 'WITHIN_5_MIN'
        WHEN ABS(DATEDIFF(MINUTE, ase.ScanTime, fs.ScanTime)) <= 30 THEN 'WITHIN_30_MIN'
        WHEN ABS(DATEDIFF(MINUTE, ase.ScanTime, fs.ScanTime)) <= 60 THEN 'WITHIN_1_HOUR'
        WHEN ABS(DATEDIFF(MINUTE, ase.ScanTime, fs.ScanTime)) <= 1440 THEN 'WITHIN_1_DAY'
        ELSE 'MORE_THAN_1_DAY'
    END as TimeDifferenceCategory,
    CASE WHEN ase.ImageDisplayName IS NOT NULL AND ase.ImageDisplayName <> '' THEN 1 ELSE 0 END as AseHasImage,
    CASE WHEN fs.Id IS NOT NULL THEN 
        (SELECT COUNT(*) FROM FS6000Images WHERE ScanId = fs.Id)
    ELSE 0 END as Fs6000ImageCount
FROM (
    SELECT DISTINCT ContainerNumber
    FROM AseScans
    WHERE ContainerNumber IN (SELECT ContainerNumber FROM FS6000Scans)
) c
INNER JOIN AseScans ase ON ase.ContainerNumber = c.ContainerNumber
INNER JOIN FS6000Scans fs ON fs.ContainerNumber = c.ContainerNumber
ORDER BY c.ContainerNumber, ase.ScanTime, fs.ScanTime;
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
    Write-Host "`nFound $($dataLines1.Count) scan pairs:" -ForegroundColor Cyan
    Write-Host "ContainerNumber,AseScanTime,Fs6000ScanTime,TimeDifferenceMinutes,ScanOrder,TimeDifferenceCategory,AseHasImage,Fs6000ImageCount" -ForegroundColor Yellow
    $dataLines1 | Select-Object -First 20 | ForEach-Object { Write-Host $_ -ForegroundColor White }
    if ($dataLines1.Count -gt 20) {
        Write-Host "... and $($dataLines1.Count - 20) more pairs" -ForegroundColor DarkGray
    }
} else {
    Write-Host "No data found" -ForegroundColor Red
}

# Query 2: Summary statistics
Write-Host "`n[2/4] Analyzing scan order patterns..." -ForegroundColor Yellow
$sql2 = @"
SELECT 
    CASE 
        WHEN ase.ScanTime < fs.ScanTime THEN 'ASE_FIRST'
        WHEN fs.ScanTime < ase.ScanTime THEN 'FS6000_FIRST'
        WHEN ase.ScanTime = fs.ScanTime THEN 'SAME_TIME'
        ELSE 'UNKNOWN'
    END as ScanOrder,
    COUNT(*) as Count,
    AVG(ABS(DATEDIFF(MINUTE, ase.ScanTime, fs.ScanTime))) as AvgTimeDifferenceMinutes,
    MIN(ABS(DATEDIFF(MINUTE, ase.ScanTime, fs.ScanTime))) as MinTimeDifferenceMinutes,
    MAX(ABS(DATEDIFF(MINUTE, ase.ScanTime, fs.ScanTime))) as MaxTimeDifferenceMinutes
FROM (
    SELECT DISTINCT ContainerNumber
    FROM AseScans
    WHERE ContainerNumber IN (SELECT ContainerNumber FROM FS6000Scans)
) c
INNER JOIN AseScans ase ON ase.ContainerNumber = c.ContainerNumber
INNER JOIN FS6000Scans fs ON fs.ContainerNumber = c.ContainerNumber
GROUP BY 
    CASE 
        WHEN ase.ScanTime < fs.ScanTime THEN 'ASE_FIRST'
        WHEN fs.ScanTime < ase.ScanTime THEN 'FS6000_FIRST'
        WHEN ase.ScanTime = fs.ScanTime THEN 'SAME_TIME'
        ELSE 'UNKNOWN'
    END
ORDER BY Count DESC;
"@

$results2 = & $sqlcmdPath -S "$serverName,$port" -d $database -E -Q $sql2 -W -s "," | Out-String
$dataLines2 = $results2.Split("`n") | Where-Object { 
    $_ -notmatch '^-+$' -and 
    $_ -notmatch '^ScanOrder,' -and 
    $_ -notmatch 'rows affected' -and 
    $_ -notmatch '^$' -and
    $_.Trim() -ne '' 
}

if ($dataLines2.Count -gt 0) {
    Write-Host "`nScan Order Statistics:" -ForegroundColor Cyan
    Write-Host "ScanOrder,Count,AvgTimeDifferenceMinutes,MinTimeDifferenceMinutes,MaxTimeDifferenceMinutes" -ForegroundColor Yellow
    $dataLines2 | ForEach-Object { Write-Host $_ -ForegroundColor White }
}

# Query 3: Time difference categories
Write-Host "`n[3/4] Analyzing time difference categories..." -ForegroundColor Yellow
$sql3 = @"
SELECT 
    TimeDiffCategory,
    COUNT(*) as Count,
    AVG(TimeDiffMinutes) as AvgMinutes,
    MIN(TimeDiffMinutes) as MinMinutes,
    MAX(TimeDiffMinutes) as MaxMinutes
FROM (
    SELECT 
        c.ContainerNumber,
        ABS(DATEDIFF(MINUTE, ase.ScanTime, fs.ScanTime)) as TimeDiffMinutes,
        CASE 
            WHEN ABS(DATEDIFF(MINUTE, ase.ScanTime, fs.ScanTime)) <= 5 THEN 'WITHIN_5_MIN'
            WHEN ABS(DATEDIFF(MINUTE, ase.ScanTime, fs.ScanTime)) <= 30 THEN 'WITHIN_30_MIN'
            WHEN ABS(DATEDIFF(MINUTE, ase.ScanTime, fs.ScanTime)) <= 60 THEN 'WITHIN_1_HOUR'
            WHEN ABS(DATEDIFF(MINUTE, ase.ScanTime, fs.ScanTime)) <= 1440 THEN 'WITHIN_1_DAY'
            ELSE 'MORE_THAN_1_DAY'
        END as TimeDiffCategory
    FROM (
        SELECT DISTINCT ContainerNumber
        FROM AseScans
        WHERE ContainerNumber IN (SELECT ContainerNumber FROM FS6000Scans)
    ) c
    INNER JOIN AseScans ase ON ase.ContainerNumber = c.ContainerNumber
    INNER JOIN FS6000Scans fs ON fs.ContainerNumber = c.ContainerNumber
) sub
GROUP BY TimeDiffCategory
ORDER BY 
    CASE TimeDiffCategory
        WHEN 'WITHIN_5_MIN' THEN 1
        WHEN 'WITHIN_30_MIN' THEN 2
        WHEN 'WITHIN_1_HOUR' THEN 3
        WHEN 'WITHIN_1_DAY' THEN 4
        ELSE 5
    END;
"@

$results3 = & $sqlcmdPath -S "$serverName,$port" -d $database -E -Q $sql3 -W -s "," | Out-String
$dataLines3 = $results3.Split("`n") | Where-Object { 
    $_ -notmatch '^-+$' -and 
    $_ -notmatch '^TimeDifferenceCategory,' -and 
    $_ -notmatch 'rows affected' -and 
    $_ -notmatch '^$' -and
    $_.Trim() -ne '' 
}

if ($dataLines3.Count -gt 0) {
    Write-Host "`nTime Difference Categories:" -ForegroundColor Cyan
    Write-Host "TimeDifferenceCategory,Count,AvgMinutes" -ForegroundColor Yellow
    $dataLines3 | ForEach-Object { Write-Host $_ -ForegroundColor White }
}

# Query 4: Image availability comparison
Write-Host "`n[4/4] Comparing image availability..." -ForegroundColor Yellow
$sql4 = @"
SELECT 
    ImageAvailability,
    COUNT(*) as Count
FROM (
    SELECT 
        c.ContainerNumber,
        CASE 
            WHEN (ase.ImageDisplayName IS NOT NULL AND ase.ImageDisplayName <> '') 
                 AND EXISTS (SELECT 1 FROM FS6000Images WHERE ScanId = fs.Id) 
            THEN 'BOTH_HAVE_IMAGES'
            WHEN (ase.ImageDisplayName IS NOT NULL AND ase.ImageDisplayName <> '') 
            THEN 'ASE_ONLY'
            WHEN EXISTS (SELECT 1 FROM FS6000Images WHERE ScanId = fs.Id) 
            THEN 'FS6000_ONLY'
            ELSE 'NEITHER'
        END as ImageAvailability
    FROM (
        SELECT DISTINCT ContainerNumber
        FROM AseScans
        WHERE ContainerNumber IN (SELECT ContainerNumber FROM FS6000Scans)
    ) c
    INNER JOIN AseScans ase ON ase.ContainerNumber = c.ContainerNumber
    INNER JOIN FS6000Scans fs ON fs.ContainerNumber = c.ContainerNumber
) sub
GROUP BY ImageAvailability
ORDER BY Count DESC;
"@

$results4 = & $sqlcmdPath -S "$serverName,$port" -d $database -E -Q $sql4 -W -s "," | Out-String
$dataLines4 = $results4.Split("`n") | Where-Object { 
    $_ -notmatch '^-+$' -and 
    $_ -notmatch '^ImageAvailability,' -and 
    $_ -notmatch 'rows affected' -and 
    $_ -notmatch '^$' -and
    $_.Trim() -ne '' 
}

if ($dataLines4.Count -gt 0) {
    Write-Host "`nImage Availability Comparison:" -ForegroundColor Cyan
    Write-Host "ImageAvailability,Count" -ForegroundColor Yellow
    $dataLines4 | ForEach-Object { Write-Host $_ -ForegroundColor White }
}

Write-Host "`n=== ANALYSIS COMPLETE ===" -ForegroundColor Cyan
Write-Host "Review the patterns above to build fail-safe logic." -ForegroundColor White
Write-Host "`nDone!" -ForegroundColor Green


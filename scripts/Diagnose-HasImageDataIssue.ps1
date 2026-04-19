# Diagnose HasImageData Check Issue
# This script checks for discrepancies between ImageDisplayName and ScanImage in AseScans
# Usage: .\Diagnose-HasImageDataIssue.ps1 -ContainerNumber "CAAU7470710" (optional)

param(
    [Parameter(Mandatory=$false)]
    [string]$ContainerNumber
)

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

Write-Host ""
Write-Host "Diagnosing HasImageData Check Issue" -ForegroundColor Cyan
Write-Host ""

if ($ContainerNumber) {
    Write-Host "Checking specific container: $ContainerNumber" -ForegroundColor Yellow
    Write-Host ""
    
    # Check specific container
    $sql = @"
DECLARE @ContainerNumber NVARCHAR(50) = '$ContainerNumber';

-- Check AseScans record
SELECT 
    ContainerNumber,
    CASE WHEN ImageDisplayName IS NOT NULL AND ImageDisplayName <> '' THEN 'Yes' ELSE 'No' END AS HasImageDisplayName,
    CASE WHEN ScanImage IS NOT NULL AND DATALENGTH(ScanImage) > 0 THEN 'Yes' ELSE 'No' END AS HasScanImageData,
    DATALENGTH(ScanImage) AS ScanImageSizeBytes,
    ScanTime,
    ImageDisplayName
FROM AseScans
WHERE ContainerNumber = @ContainerNumber;

-- Check ContainerCompletenessStatus
SELECT 
    ContainerNumber,
    ScannerType,
    HasImageData,
    ImageDataCompleteness,
    Status,
    WorkflowStage,
    UpdatedAt
FROM ContainerCompletenessStatuses
WHERE ContainerNumber = @ContainerNumber;
"@
    
    & $sqlcmdPath -S "$serverName,$port" -d $database -E -Q $sql -W
}
else {
    Write-Host "Checking for data quality issues across all ASE containers..." -ForegroundColor Yellow
    Write-Host ""
    
    # Check for discrepancies
    $sql = @"
-- Find ASE containers with ImageDisplayName but no ScanImage data
SELECT 
    'Has ImageDisplayName but NO ScanImage data' AS IssueType,
    COUNT(*) AS Count
FROM AseScans
WHERE ImageDisplayName IS NOT NULL 
    AND ImageDisplayName <> ''
    AND (ScanImage IS NULL OR DATALENGTH(ScanImage) = 0)

UNION ALL

-- Find ASE containers with ScanImage data but no ImageDisplayName
SELECT 
    'Has ScanImage data but NO ImageDisplayName' AS IssueType,
    COUNT(*) AS Count
FROM AseScans
WHERE (ImageDisplayName IS NULL OR ImageDisplayName = '')
    AND ScanImage IS NOT NULL 
    AND DATALENGTH(ScanImage) > 0

UNION ALL

-- Find ASE containers with both
SELECT 
    'Has BOTH ImageDisplayName and ScanImage data' AS IssueType,
    COUNT(*) AS Count
FROM AseScans
WHERE ImageDisplayName IS NOT NULL 
    AND ImageDisplayName <> ''
    AND ScanImage IS NOT NULL 
    AND DATALENGTH(ScanImage) > 0

UNION ALL

-- Find ASE containers with neither
SELECT 
    'Has NEITHER ImageDisplayName nor ScanImage data' AS IssueType,
    COUNT(*) AS Count
FROM AseScans
WHERE (ImageDisplayName IS NULL OR ImageDisplayName = '')
    AND (ScanImage IS NULL OR DATALENGTH(ScanImage) = 0);
"@
    
    Write-Host "Data Quality Summary:" -ForegroundColor Yellow
    & $sqlcmdPath -S "$serverName,$port" -d $database -E -Q $sql -W
    
    Write-Host ""
    Write-Host "Checking ContainerCompletenessStatus HasImageData vs actual image availability..." -ForegroundColor Yellow
    Write-Host ""
    
    # Check ContainerCompletenessStatus vs actual images
    $sql2 = @"
-- Compare ContainerCompletenessStatus.HasImageData with actual AseScans data
SELECT 
    ccs.ContainerNumber,
    ccs.ScannerType,
    ccs.HasImageData AS StatusSaysHasImage,
    CASE WHEN a.ImageDisplayName IS NOT NULL AND a.ImageDisplayName <> '' THEN 'Yes' ELSE 'No' END AS ActuallyHasImageDisplayName,
    CASE WHEN a.ScanImage IS NOT NULL AND DATALENGTH(a.ScanImage) > 0 THEN 'Yes' ELSE 'No' END AS ActuallyHasScanImage,
    CASE 
        WHEN ccs.HasImageData = 1 AND (a.ImageDisplayName IS NULL OR a.ImageDisplayName = '') THEN 'DISCREPANCY: Status says Yes, but no ImageDisplayName'
        WHEN ccs.HasImageData = 0 AND (a.ImageDisplayName IS NOT NULL AND a.ImageDisplayName <> '') THEN 'DISCREPANCY: Status says No, but ImageDisplayName exists'
        ELSE 'OK'
    END AS Status
FROM ContainerCompletenessStatuses ccs
LEFT JOIN AseScans a ON a.ContainerNumber = ccs.ContainerNumber AND ccs.ScannerType = 'ASE'
WHERE ccs.ScannerType = 'ASE'
    AND (
        (ccs.HasImageData = 1 AND (a.ImageDisplayName IS NULL OR a.ImageDisplayName = ''))
        OR
        (ccs.HasImageData = 0 AND (a.ImageDisplayName IS NOT NULL AND a.ImageDisplayName <> ''))
    )
ORDER BY ccs.UpdatedAt DESC;
"@
    
    & $sqlcmdPath -S "$serverName,$port" -d $database -E -Q $sql2 -W
}

Write-Host ""
Write-Host "Done!" -ForegroundColor Green


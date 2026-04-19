# Refresh HasImageData for ASE Containers
# This script updates ContainerCompletenessStatus.HasImageData for ASE containers
# based on actual ImageDisplayName availability
# Usage: .\Refresh-HasImageDataForASE.ps1 [-DryRun]

param(
    [Parameter(Mandatory=$false)]
    [switch]$DryRun
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
if ($DryRun) {
    Write-Host "DRY RUN MODE - No changes will be made" -ForegroundColor Yellow
} else {
    Write-Host "LIVE MODE - Changes will be applied" -ForegroundColor Red
}
Write-Host "Refreshing HasImageData for ASE Containers" -ForegroundColor Cyan
Write-Host ""

# SQL to refresh HasImageData
$sql = @"
-- Update ContainerCompletenessStatus.HasImageData for ASE containers
-- based on actual ImageDisplayName availability

DECLARE @UpdatedCount INT = 0;
DECLARE @FixedCount INT = 0;
DECLARE @AlreadyCorrectCount INT = 0;

-- Find containers that need fixing
-- HasImageData = 0 but ImageDisplayName exists (should be 1)
UPDATE ccs
SET 
    ccs.HasImageData = 1,
    ccs.ImageDataCompleteness = 100,
    ccs.UpdatedAt = GETUTCDATE()
FROM ContainerCompletenessStatuses ccs
INNER JOIN AseScans a ON a.ContainerNumber = ccs.ContainerNumber
WHERE ccs.ScannerType = 'ASE'
    AND ccs.HasImageData = 0
    AND a.ImageDisplayName IS NOT NULL
    AND a.ImageDisplayName <> '';

SET @FixedCount = @@ROWCOUNT;

-- Find containers that are already correct (for reporting)
SELECT @AlreadyCorrectCount = COUNT(*)
FROM ContainerCompletenessStatuses ccs
INNER JOIN AseScans a ON a.ContainerNumber = ccs.ContainerNumber
WHERE ccs.ScannerType = 'ASE'
    AND (
        (ccs.HasImageData = 1 AND a.ImageDisplayName IS NOT NULL AND a.ImageDisplayName <> '')
        OR
        (ccs.HasImageData = 0 AND (a.ImageDisplayName IS NULL OR a.ImageDisplayName = ''))
    );

SET @UpdatedCount = @FixedCount;

PRINT '=== REFRESH RESULTS ===';
PRINT 'Containers Fixed: ' + CAST(@FixedCount AS NVARCHAR);
PRINT 'Containers Already Correct: ' + CAST(@AlreadyCorrectCount AS NVARCHAR);
PRINT 'Total Containers Checked: ' + CAST((@FixedCount + @AlreadyCorrectCount) AS NVARCHAR);
"@

if ($DryRun) {
    # Dry run: Just show what would be updated
    Write-Host "DRY RUN: Checking what would be updated..." -ForegroundColor Yellow
    Write-Host ""
    
    $dryRunSql = @"
-- Count containers that would be fixed
SELECT 
    'Would Fix' AS Action,
    COUNT(*) AS Count
FROM ContainerCompletenessStatuses ccs
INNER JOIN AseScans a ON a.ContainerNumber = ccs.ContainerNumber
WHERE ccs.ScannerType = 'ASE'
    AND ccs.HasImageData = 0
    AND a.ImageDisplayName IS NOT NULL
    AND a.ImageDisplayName <> ''

UNION ALL

-- Count containers that are already correct
SELECT 
    'Already Correct' AS Action,
    COUNT(*) AS Count
FROM ContainerCompletenessStatuses ccs
INNER JOIN AseScans a ON a.ContainerNumber = ccs.ContainerNumber
WHERE ccs.ScannerType = 'ASE'
    AND (
        (ccs.HasImageData = 1 AND a.ImageDisplayName IS NOT NULL AND a.ImageDisplayName <> '')
        OR
        (ccs.HasImageData = 0 AND (a.ImageDisplayName IS NULL OR a.ImageDisplayName = ''))
    );
"@
    
    & $sqlcmdPath -S "$serverName,$port" -d $database -E -Q $dryRunSql -W
    Write-Host ""
    Write-Host "DRY RUN complete - no changes made" -ForegroundColor Yellow
} else {
    Write-Host "Applying fix..." -ForegroundColor Yellow
    & $sqlcmdPath -S "$serverName,$port" -d $database -E -Q $sql -W
    Write-Host ""
    Write-Host "Done! HasImageData has been refreshed for ASE containers." -ForegroundColor Green
}

Write-Host ""


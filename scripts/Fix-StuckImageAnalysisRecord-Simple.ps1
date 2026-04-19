# Fix Stuck Image Analysis Record - Simple Version
# Usage: .\Fix-StuckImageAnalysisRecord-Simple.ps1 -GroupIdentifier "80925590007"

param(
    [Parameter(Mandatory=$true)]
    [string]$GroupIdentifier
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
Write-Host "Fixing Stuck Record: $GroupIdentifier" -ForegroundColor Cyan
Write-Host ""

# Step 1: Check if all containers WITH images have decisions
$checkSql = @"
DECLARE @GroupIdentifier NVARCHAR(150) = '$GroupIdentifier';
DECLARE @ContainersWithImages INT;
DECLARE @DecidedWithImages INT;
DECLARE @ContainersWithoutImages INT;
DECLARE @TotalContainers INT;

-- Count containers
SELECT 
    @ContainersWithImages = COUNT(DISTINCT CASE WHEN ccs.HasImageData = 1 THEN ccs.ContainerNumber END),
    @ContainersWithoutImages = COUNT(DISTINCT CASE WHEN ccs.HasImageData = 0 THEN ccs.ContainerNumber END),
    @DecidedWithImages = COUNT(DISTINCT CASE WHEN ccs.HasImageData = 1 AND iad.Decision IN ('Normal', 'Abnormal') THEN ccs.ContainerNumber END),
    @TotalContainers = COUNT(DISTINCT ccs.ContainerNumber)
FROM ContainerCompletenessStatuses ccs
LEFT JOIN ImageAnalysisDecisions iad ON iad.ContainerNumber = ccs.ContainerNumber 
    AND iad.GroupIdentifier = ccs.GroupIdentifier
    AND iad.Decision IN ('Normal', 'Abnormal')
WHERE ccs.GroupIdentifier = @GroupIdentifier;

SELECT 
    @ContainersWithImages AS ContainersWithImages,
    @ContainersWithoutImages AS ContainersWithoutImages,
    @DecidedWithImages AS DecidedWithImages,
    @TotalContainers AS TotalContainers,
    CASE 
        WHEN @ContainersWithImages = 0 THEN 'No containers with images - can proceed'
        WHEN @DecidedWithImages = @ContainersWithImages THEN 'All containers with images have decisions - can proceed'
        ELSE 'Cannot proceed - containers with images are missing decisions'
    END AS Status
"@

Write-Host "Checking record status..." -ForegroundColor Yellow
$checkResult = & $sqlcmdPath -S "$serverName,$port" -d $database -E -Q $checkSql -W -s "," -h -1
Write-Host $checkResult

# Step 2: Fix the record
$fixSql = @"
DECLARE @GroupIdentifier NVARCHAR(150) = '$GroupIdentifier';
DECLARE @GroupId UNIQUEIDENTIFIER;
DECLARE @ContainersWithImages INT;
DECLARE @ContainersWithoutImages INT;
DECLARE @DecidedWithImages INT;
DECLARE @TotalContainers INT;

-- Get group ID
SELECT @GroupId = Id
FROM AnalysisGroups
WHERE GroupIdentifier = @GroupIdentifier;

IF @GroupId IS NULL
BEGIN
    PRINT 'Error: Group not found';
    RETURN;
END

-- Count containers
SELECT 
    @ContainersWithImages = COUNT(DISTINCT CASE WHEN ccs.HasImageData = 1 THEN ccs.ContainerNumber END),
    @ContainersWithoutImages = COUNT(DISTINCT CASE WHEN ccs.HasImageData = 0 THEN ccs.ContainerNumber END),
    @DecidedWithImages = COUNT(DISTINCT CASE WHEN ccs.HasImageData = 1 AND iad.Decision IN ('Normal', 'Abnormal') THEN ccs.ContainerNumber END),
    @TotalContainers = COUNT(DISTINCT ccs.ContainerNumber)
FROM ContainerCompletenessStatuses ccs
LEFT JOIN ImageAnalysisDecisions iad ON iad.ContainerNumber = ccs.ContainerNumber 
    AND iad.GroupIdentifier = ccs.GroupIdentifier
    AND iad.Decision IN ('Normal', 'Abnormal')
WHERE ccs.GroupIdentifier = @GroupIdentifier;

-- If all containers have no images OR all containers with images have decisions, move to PartiallyCompleted
IF (@ContainersWithImages = 0 OR @DecidedWithImages = @ContainersWithImages)
BEGIN
    UPDATE AnalysisGroups
    SET Status = 'PartiallyCompleted',
        PartiallyCompletedDate = GETUTCDATE(),
        TotalContainerCount = @TotalContainers,
        SubmittedContainerCount = @DecidedWithImages,
        PendingContainerCount = @ContainersWithoutImages,
        UpdatedAtUtc = GETUTCDATE()
    WHERE Id = @GroupId;
    
    PRINT 'Success: Record moved to PartiallyCompleted status';
    PRINT 'Total Containers: ' + CAST(@TotalContainers AS NVARCHAR);
    PRINT 'Containers with Images: ' + CAST(@ContainersWithImages AS NVARCHAR);
    PRINT 'Containers without Images: ' + CAST(@ContainersWithoutImages AS NVARCHAR);
    PRINT 'Decided Containers: ' + CAST(@DecidedWithImages AS NVARCHAR);
END
ELSE
BEGIN
    PRINT 'Error: Cannot fix - ' + CAST((@ContainersWithImages - @DecidedWithImages) AS NVARCHAR) + ' container(s) with images are missing decisions';
END

-- Show updated status
SELECT GroupIdentifier, Status, PartiallyCompletedDate, TotalContainerCount, SubmittedContainerCount, PendingContainerCount
FROM AnalysisGroups
WHERE Id = @GroupId;
"@

Write-Host ""
Write-Host "Applying fix..." -ForegroundColor Yellow
& $sqlcmdPath -S "$serverName,$port" -d $database -E -Q $fixSql -W

Write-Host ""
Write-Host "Done!" -ForegroundColor Green


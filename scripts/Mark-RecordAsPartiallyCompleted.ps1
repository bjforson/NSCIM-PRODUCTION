# Mark Record as PartiallyCompleted (when all containers have no images)
# Usage: .\Mark-RecordAsPartiallyCompleted.ps1 -GroupIdentifier "80825510038"

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
Write-Host "Marking Record as PartiallyCompleted: $GroupIdentifier" -ForegroundColor Cyan
Write-Host ""

# SQL to mark record as PartiallyCompleted
$sql = @"
DECLARE @GroupIdentifier NVARCHAR(150) = '$GroupIdentifier';
DECLARE @GroupId UNIQUEIDENTIFIER;
DECLARE @CurrentStatus NVARCHAR(50);
DECLARE @ContainersWithImages INT = 0;
DECLARE @ContainersWithoutImages INT = 0;
DECLARE @TotalContainers INT = 0;
DECLARE @DecidedWithImages INT = 0;

-- Get group info
SELECT 
    @GroupId = Id,
    @CurrentStatus = Status
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
    @TotalContainers = COUNT(DISTINCT ccs.ContainerNumber),
    @DecidedWithImages = COUNT(DISTINCT CASE WHEN ccs.HasImageData = 1 AND iad.Decision IN ('Normal', 'Abnormal') THEN ccs.ContainerNumber END)
FROM ContainerCompletenessStatuses ccs
LEFT JOIN ImageAnalysisDecisions iad ON iad.ContainerNumber = ccs.ContainerNumber 
    AND iad.GroupIdentifier = ccs.GroupIdentifier
    AND iad.Decision IN ('Normal', 'Abnormal')
WHERE ccs.GroupIdentifier = @GroupIdentifier;

-- Validate: Can only mark as PartiallyCompleted if:
-- 1. All containers have no images, OR
-- 2. Has containers without images AND all containers WITH images are decided AND at AuditCompleted status
IF @ContainersWithImages = 0
BEGIN
    -- All containers have no images - mark as PartiallyCompleted immediately
    UPDATE AnalysisGroups
    SET Status = 'PartiallyCompleted',
        PartiallyCompletedDate = GETUTCDATE(),
        TotalContainerCount = @TotalContainers,
        SubmittedContainerCount = 0,  -- No containers with images to submit
        PendingContainerCount = @ContainersWithoutImages,
        UpdatedAtUtc = GETUTCDATE()
    WHERE Id = @GroupId;
    
    PRINT '✅ Success: Record marked as PartiallyCompleted';
    PRINT '   Reason: All containers have NO images';
    PRINT '   Total Containers: ' + CAST(@TotalContainers AS NVARCHAR);
    PRINT '   Containers with Images: 0';
    PRINT '   Containers without Images: ' + CAST(@ContainersWithoutImages AS NVARCHAR);
END
ELSE IF @ContainersWithoutImages > 0 AND @DecidedWithImages = @ContainersWithImages AND @CurrentStatus = 'AuditCompleted'
BEGIN
    -- Has containers without images, all containers WITH images are decided, and at AuditCompleted
    UPDATE AnalysisGroups
    SET Status = 'PartiallyCompleted',
        PartiallyCompletedDate = GETUTCDATE(),
        TotalContainerCount = @TotalContainers,
        SubmittedContainerCount = @ContainersWithImages,
        PendingContainerCount = @ContainersWithoutImages,
        UpdatedAtUtc = GETUTCDATE()
    WHERE Id = @GroupId;
    
    PRINT '✅ Success: Record marked as PartiallyCompleted';
    PRINT '   Reason: Has containers without images, all containers WITH images are decided';
    PRINT '   Total Containers: ' + CAST(@TotalContainers AS NVARCHAR);
    PRINT '   Containers with Images: ' + CAST(@ContainersWithImages AS NVARCHAR);
    PRINT '   Containers without Images: ' + CAST(@ContainersWithoutImages AS NVARCHAR);
END
ELSE
BEGIN
    PRINT '❌ Error: Cannot mark as PartiallyCompleted';
    PRINT '   Current Status: ' + @CurrentStatus;
    PRINT '   Containers with Images: ' + CAST(@ContainersWithImages AS NVARCHAR);
    PRINT '   Containers without Images: ' + CAST(@ContainersWithoutImages AS NVARCHAR);
    PRINT '   Decided Containers with Images: ' + CAST(@DecidedWithImages AS NVARCHAR);
    
    IF @ContainersWithImages > 0 AND @DecidedWithImages < @ContainersWithImages
    BEGIN
        PRINT '   ⚠️  Blocker: ' + CAST((@ContainersWithImages - @DecidedWithImages) AS NVARCHAR) + ' container(s) WITH images are still undecided';
    END
    ELSE IF @ContainersWithoutImages = 0
    BEGIN
        PRINT '   ⚠️  Blocker: All containers have images - should be Completed, not PartiallyCompleted';
    END
    ELSE IF @CurrentStatus <> 'AuditCompleted'
    BEGIN
        PRINT '   ⚠️  Blocker: Status must be AuditCompleted (current: ' + @CurrentStatus + ')';
        PRINT '   → Record needs to progress through workflow first';
    END
END

-- Show updated status
SELECT GroupIdentifier, Status, PartiallyCompletedDate, TotalContainerCount, SubmittedContainerCount, PendingContainerCount
FROM AnalysisGroups
WHERE Id = @GroupId;
"@

Write-Host "Marking record as PartiallyCompleted..." -ForegroundColor Yellow
& $sqlcmdPath -S "$serverName,$port" -d $database -E -Q $sql -W

Write-Host ""
Write-Host "Done!" -ForegroundColor Green


# Analyze Why Record Should Be PartiallyCompleted
# Usage: .\Analyze-PartialRecord.ps1 -GroupIdentifier "80825510038"

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
Write-Host "Analyzing Record: $GroupIdentifier" -ForegroundColor Cyan
Write-Host ""

# SQL to analyze the record
$sql = @"
DECLARE @GroupIdentifier NVARCHAR(150) = '$GroupIdentifier';
DECLARE @GroupId UNIQUEIDENTIFIER;
DECLARE @CurrentStatus NVARCHAR(50);
DECLARE @ContainersWithImages INT = 0;
DECLARE @ContainersWithoutImages INT = 0;
DECLARE @DecidedWithImages INT = 0;
DECLARE @UndecidedWithImages INT = 0;
DECLARE @TotalContainers INT = 0;

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
    @DecidedWithImages = COUNT(DISTINCT CASE WHEN ccs.HasImageData = 1 AND iad.Decision IN ('Normal', 'Abnormal') THEN ccs.ContainerNumber END),
    @UndecidedWithImages = COUNT(DISTINCT CASE WHEN ccs.HasImageData = 1 AND (iad.Decision NOT IN ('Normal', 'Abnormal') OR iad.Decision IS NULL) THEN ccs.ContainerNumber END),
    @TotalContainers = COUNT(DISTINCT ccs.ContainerNumber)
FROM ContainerCompletenessStatuses ccs
LEFT JOIN ImageAnalysisDecisions iad ON iad.ContainerNumber = ccs.ContainerNumber 
    AND iad.GroupIdentifier = ccs.GroupIdentifier
    AND iad.Decision IN ('Normal', 'Abnormal')
WHERE ccs.GroupIdentifier = @GroupIdentifier;

-- Analysis
PRINT '=== ANALYSIS ===';
PRINT 'Current Status: ' + @CurrentStatus;
PRINT 'Total Containers: ' + CAST(@TotalContainers AS NVARCHAR);
PRINT 'Containers WITH Images: ' + CAST(@ContainersWithImages AS NVARCHAR);
PRINT 'Containers WITHOUT Images: ' + CAST(@ContainersWithoutImages AS NVARCHAR);
PRINT 'Decided Containers WITH Images: ' + CAST(@DecidedWithImages AS NVARCHAR);
PRINT 'Undecided Containers WITH Images: ' + CAST(@UndecidedWithImages AS NVARCHAR);
PRINT '';

-- Determine why it should be PartiallyCompleted
IF @ContainersWithImages = 0
BEGIN
    PRINT '✅ REASON: All containers have NO images - should be PartiallyCompleted';
    PRINT '   - All containers without images can remain undecided';
    PRINT '   - Record should progress through workflow and end at PartiallyCompleted';
END
ELSE IF @ContainersWithoutImages > 0 AND @UndecidedWithImages = 0
BEGIN
    PRINT '✅ REASON: Has containers without images AND all containers WITH images are decided';
    PRINT '   - Should be PartiallyCompleted after going through audit';
END
ELSE IF @UndecidedWithImages > 0
BEGIN
    PRINT '❌ BLOCKER: ' + CAST(@UndecidedWithImages AS NVARCHAR) + ' container(s) WITH images are still undecided';
    PRINT '   - Cannot progress until all containers WITH images have decisions';
END

-- Check if it should be PartiallyCompleted NOW
IF @ContainersWithImages = 0 AND @CurrentStatus IN ('Ready', 'AnalystCompleted', 'AuditCompleted')
BEGIN
    PRINT '';
    PRINT '✅ ACTION: Record can be moved to PartiallyCompleted NOW';
    PRINT '   - All containers have no images';
    PRINT '   - No need to wait for decisions or audit';
END
ELSE IF @ContainersWithoutImages > 0 AND @UndecidedWithImages = 0 AND @CurrentStatus = 'AuditCompleted'
BEGIN
    PRINT '';
    PRINT '✅ ACTION: Record should be moved to PartiallyCompleted by SubmissionWorker';
    PRINT '   - Has containers without images';
    PRINT '   - All containers with images are decided';
    PRINT '   - At AuditCompleted status - ready for submission';
END
ELSE
BEGIN
    PRINT '';
    PRINT '⚠️  ACTION: Record needs to progress through workflow first';
    PRINT '   - Current status: ' + @CurrentStatus;
    IF @CurrentStatus = 'Ready'
    BEGIN
        PRINT '   - Next: AnalystCompleted (after decisions are saved)';
    END
    ELSE IF @CurrentStatus = 'AnalystCompleted'
    BEGIN
        PRINT '   - Next: AuditAssigned (automatic via AssignmentWorker)';
    END
    ELSE IF @CurrentStatus = 'AuditAssigned'
    BEGIN
        PRINT '   - Next: AuditCompleted (after audit decision)';
    END
    ELSE IF @CurrentStatus = 'AuditCompleted'
    BEGIN
        PRINT '   - Next: PartiallyCompleted (automatic via SubmissionWorker)';
    END
END
"@

& $sqlcmdPath -S "$serverName,$port" -d $database -E -Q $sql -W

Write-Host ""


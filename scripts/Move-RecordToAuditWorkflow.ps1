# Move Record to Audit Workflow
# This moves a record from PartiallyCompleted back to AnalystCompleted so it can go through audit
# Usage: .\Move-RecordToAuditWorkflow.ps1 -GroupIdentifier "80925590007"

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
Write-Host "Moving Record to Audit Workflow: $GroupIdentifier" -ForegroundColor Cyan
Write-Host ""

# SQL to move record to AnalystCompleted so it can progress through audit
$sql = @"
DECLARE @GroupIdentifier NVARCHAR(150) = '$GroupIdentifier';
DECLARE @GroupId UNIQUEIDENTIFIER;
DECLARE @ContainersWithImages INT;
DECLARE @DecidedWithImages INT;

-- Get group ID
SELECT @GroupId = Id
FROM AnalysisGroups
WHERE GroupIdentifier = @GroupIdentifier;

IF @GroupId IS NULL
BEGIN
    PRINT 'Error: Group not found';
    RETURN;
END

-- Count containers with images and decisions
SELECT 
    @ContainersWithImages = COUNT(DISTINCT CASE WHEN ccs.HasImageData = 1 THEN ccs.ContainerNumber END),
    @DecidedWithImages = COUNT(DISTINCT CASE WHEN ccs.HasImageData = 1 AND iad.Decision IN ('Normal', 'Abnormal') THEN ccs.ContainerNumber END)
FROM ContainerCompletenessStatuses ccs
LEFT JOIN ImageAnalysisDecisions iad ON iad.ContainerNumber = ccs.ContainerNumber 
    AND iad.GroupIdentifier = ccs.GroupIdentifier
    AND iad.Decision IN ('Normal', 'Abnormal')
WHERE ccs.GroupIdentifier = @GroupIdentifier;

-- If all containers with images have decisions (or no containers have images), move to AnalystCompleted
IF (@ContainersWithImages = 0 OR @DecidedWithImages = @ContainersWithImages)
BEGIN
    -- Move to AnalystCompleted
    UPDATE AnalysisGroups
    SET Status = 'AnalystCompleted',
        PartiallyCompletedDate = NULL,  -- Clear partial completion date
        TotalContainerCount = NULL,     -- Will be set after submission
        SubmittedContainerCount = NULL,
        PendingContainerCount = NULL,
        UpdatedAtUtc = GETUTCDATE()
    WHERE Id = @GroupId;
    
    -- Release any active Analyst assignments
    UPDATE a
    SET a.State = 'Released',
        a.UpdatedAtUtc = GETUTCDATE()
    FROM AnalysisAssignments a
    WHERE a.GroupId = @GroupId
        AND a.Role = 'Analyst'
        AND a.State = 'Active';
    
    -- Update WorkflowStage to 'Audit' for containers that have decisions
    UPDATE ccs
    SET ccs.WorkflowStage = 'Audit',
        ccs.UpdatedAt = GETUTCDATE()
    FROM ContainerCompletenessStatuses ccs
    INNER JOIN ImageAnalysisDecisions iad ON iad.ContainerNumber = ccs.ContainerNumber
        AND iad.GroupIdentifier = ccs.GroupIdentifier
        AND iad.Decision IN ('Normal', 'Abnormal')
    WHERE ccs.GroupIdentifier = @GroupIdentifier
        AND ccs.WorkflowStage <> 'Audit';
    
    PRINT 'Success: Record moved to AnalystCompleted - will progress to AuditAssigned next';
    PRINT 'Containers with Images: ' + CAST(@ContainersWithImages AS NVARCHAR);
    PRINT 'Decided Containers: ' + CAST(@DecidedWithImages AS NVARCHAR);
END
ELSE
BEGIN
    PRINT 'Error: Cannot move - ' + CAST((@ContainersWithImages - @DecidedWithImages) AS NVARCHAR) + ' container(s) with images are missing decisions';
END

-- Show updated status
SELECT GroupIdentifier, Status, PartiallyCompletedDate, TotalContainerCount, SubmittedContainerCount, PendingContainerCount
FROM AnalysisGroups
WHERE Id = @GroupId;
"@

Write-Host "Moving record to AnalystCompleted..." -ForegroundColor Yellow
& $sqlcmdPath -S "$serverName,$port" -d $database -E -Q $sql -W

Write-Host ""
Write-Host "Record will now progress through:" -ForegroundColor Green
Write-Host "1. AnalystCompleted (current)" -ForegroundColor White
Write-Host "2. AuditAssigned (automatic via AssignmentWorker)" -ForegroundColor White
Write-Host "3. AuditCompleted (after audit decision)" -ForegroundColor White
Write-Host "4. PartiallyCompleted (after submission, if containers missing images)" -ForegroundColor White
Write-Host ""
Write-Host "Done!" -ForegroundColor Green


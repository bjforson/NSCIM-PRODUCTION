# Fix stuck record 40725469702
# Container HLBU2411725 has no image and is preventing progression
# All containers WITH images have decisions and are in Audit stage
# Group should be PartiallyCompleted, container should be PartiallyCompleted

param(
    [Parameter(Mandatory=$false)]
    [string]$GroupIdentifier = "40725469702"
)

$ErrorActionPreference = "Stop"

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
    Write-Host "Error: sqlcmd.exe not found at $sqlcmdPath" -ForegroundColor Red
    exit 1
}

Write-Host "`n=== Fixing Stuck Record $GroupIdentifier ===" -ForegroundColor Cyan
Write-Host "Server: $serverName`:$port" -ForegroundColor Gray
Write-Host "Database: $database`n" -ForegroundColor Gray

# Escape GroupIdentifier for SQL
$escapedGroupId = $GroupIdentifier.Replace("'", "''")

try {
    # Step 1: Verify current state
    Write-Host "1. Verifying current state..." -ForegroundColor Yellow
    
    $verifySql = @"
-- Get group info
SELECT Id, GroupIdentifier, Status, TotalContainerCount, SubmittedContainerCount, PendingContainerCount
FROM AnalysisGroups WITH (NOLOCK)
WHERE GroupIdentifier = '$escapedGroupId';

-- Get containers
SELECT ContainerNumber, HasImageData, WorkflowStage, Status
FROM ContainerCompletenessStatuses WITH (NOLOCK)
WHERE GroupIdentifier = '$escapedGroupId'
ORDER BY ContainerNumber;

-- Get decisions
SELECT ContainerNumber, Decision
FROM ImageAnalysisDecisions WITH (NOLOCK)
WHERE GroupIdentifier = '$escapedGroupId'
ORDER BY ContainerNumber;
"@
    
    $verifyOutput = & $sqlcmdPath -S "$serverName,$port" -d $database -E -Q $verifySql -W -s "|" -h -1 2>&1
    Write-Host $verifyOutput
    
    # Step 2: Update AnalysisGroup to PartiallyCompleted
    Write-Host "`n2. Updating AnalysisGroup to PartiallyCompleted..." -ForegroundColor Yellow
    
    $updateGroupSql = @"
DECLARE @GroupId UNIQUEIDENTIFIER;
DECLARE @TotalContainers INT;
DECLARE @ContainersWithImages INT;
DECLARE @ContainersWithoutImages INT;

-- Get group ID
SELECT @GroupId = Id
FROM AnalysisGroups
WHERE GroupIdentifier = '$escapedGroupId';

-- Count containers
SELECT 
    @TotalContainers = COUNT(DISTINCT ContainerNumber),
    @ContainersWithImages = COUNT(DISTINCT CASE WHEN HasImageData = 1 THEN ContainerNumber END),
    @ContainersWithoutImages = COUNT(DISTINCT CASE WHEN HasImageData = 0 THEN ContainerNumber END)
FROM ContainerCompletenessStatuses
WHERE GroupIdentifier = '$escapedGroupId';

-- Update AnalysisGroup
UPDATE AnalysisGroups
SET Status = 'PartiallyCompleted',
    PartiallyCompletedDate = GETUTCDATE(),
    TotalContainerCount = @TotalContainers,
    SubmittedContainerCount = @ContainersWithImages,
    PendingContainerCount = @ContainersWithoutImages,
    UpdatedAtUtc = GETUTCDATE()
WHERE Id = @GroupId;

SELECT 'Group updated' as Result, @TotalContainers as TotalContainers, @ContainersWithImages as WithImages, @ContainersWithoutImages as WithoutImages;
"@
    
    $updateOutput = & $sqlcmdPath -S "$serverName,$port" -d $database -E -Q $updateGroupSql -W -s "|" -h -1 2>&1
    Write-Host $updateOutput
    
    # Step 3: Update container without image to PartiallyCompleted
    Write-Host "`n3. Updating container HLBU2411725 to PartiallyCompleted..." -ForegroundColor Yellow
    
    $updateContainerSql = @"
-- Update container without image
UPDATE ContainerCompletenessStatuses
SET WorkflowStage = 'PartiallyCompleted',
    UpdatedAt = GETUTCDATE()
WHERE ContainerNumber = 'HLBU2411725'
  AND GroupIdentifier = '$escapedGroupId'
  AND HasImageData = 0;

SELECT 'Container updated' as Result, @@ROWCOUNT as RowsAffected;
"@
    
    $updateContainerOutput = & $sqlcmdPath -S "$serverName,$port" -d $database -E -Q $updateContainerSql -W -s "|" -h -1 2>&1
    Write-Host $updateContainerOutput
    
    # Step 4: Verify final state
    Write-Host "`n4. Verifying final state..." -ForegroundColor Yellow
    
    $finalSql = @"
-- Get updated group status
SELECT GroupIdentifier, Status, PartiallyCompletedDate, TotalContainerCount, SubmittedContainerCount, PendingContainerCount
FROM AnalysisGroups
WHERE GroupIdentifier = '$escapedGroupId';

-- Get updated container statuses
SELECT ContainerNumber, HasImageData, WorkflowStage, Status
FROM ContainerCompletenessStatuses
WHERE GroupIdentifier = '$escapedGroupId'
ORDER BY ContainerNumber;
"@
    
    $finalOutput = & $sqlcmdPath -S "$serverName,$port" -d $database -E -Q $finalSql -W -s "|" -h -1 2>&1
    Write-Host $finalOutput
    
    Write-Host "`n✅ Fix completed successfully!" -ForegroundColor Green
    Write-Host "   - AnalysisGroup.Status = PartiallyCompleted" -ForegroundColor White
    Write-Host "   - HLBU2411725.WorkflowStage = PartiallyCompleted" -ForegroundColor White
    Write-Host "   - Group should now be eligible for audit queue" -ForegroundColor White
    
} catch {
    Write-Host "`n❌ Error: $_" -ForegroundColor Red
    Write-Host "Stack trace: $($_.ScriptStackTrace)" -ForegroundColor Red
    exit 1
}

Write-Host "`n=== DONE ===" -ForegroundColor Cyan


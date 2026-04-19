# Fix Stuck Image Analysis Record
# This script will:
# 1. Check if all containers WITH images have decisions
# 2. If yes, move the record to the appropriate next status
# 3. If containers are missing images, move to PartiallyCompleted

param(
    [Parameter(Mandatory=$true)]
    [string]$GroupIdentifier,
    
    [Parameter(Mandatory=$false)]
    [switch]$Force
)

# Load connection string from appsettings.json
$appsettingsPath = "src\NickScanCentralImagingPortal.API\appsettings.json"
if (-not (Test-Path $appsettingsPath)) {
    Write-Host "❌ Error: appsettings.json not found at $appsettingsPath" -ForegroundColor Red
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

Write-Host "`n🔧 Fixing Stuck Image Analysis Record: $GroupIdentifier" -ForegroundColor Cyan
Write-Host "Server: $serverName`:$port" -ForegroundColor Gray
Write-Host "Database: $database`n" -ForegroundColor Gray

# Step 1: Check current state
Write-Host "📊 Step 1: Checking current state..." -ForegroundColor Yellow

$checkSql = @"
DECLARE @GroupIdentifier NVARCHAR(150) = '$GroupIdentifier';

-- Get AnalysisGroup
SELECT 
    ag.Id,
    ag.Status,
    ag.ScannerType
FROM AnalysisGroups ag
WHERE ag.GroupIdentifier = @GroupIdentifier;

-- Get containers with images and their decision status
SELECT 
    ccs.ContainerNumber,
    ccs.HasImageData,
    CASE WHEN iad.Decision IN ('Normal', 'Abnormal') THEN 1 ELSE 0 END AS HasDecision
FROM ContainerCompletenessStatuses ccs
LEFT JOIN ImageAnalysisDecisions iad ON iad.ContainerNumber = ccs.ContainerNumber 
    AND iad.GroupIdentifier = ccs.GroupIdentifier
    AND iad.Decision IN ('Normal', 'Abnormal')
WHERE ccs.GroupIdentifier = @GroupIdentifier;

-- Count summary
SELECT 
    COUNT(DISTINCT CASE WHEN ccs.HasImageData = 1 THEN ccs.ContainerNumber END) AS ContainersWithImages,
    COUNT(DISTINCT CASE WHEN ccs.HasImageData = 0 THEN ccs.ContainerNumber END) AS ContainersWithoutImages,
    COUNT(DISTINCT CASE WHEN ccs.HasImageData = 1 AND iad.Decision IN ('Normal', 'Abnormal') THEN ccs.ContainerNumber END) AS DecidedContainersWithImages,
    COUNT(DISTINCT CASE WHEN ccs.HasImageData = 1 AND (iad.Decision NOT IN ('Normal', 'Abnormal') OR iad.Decision IS NULL) THEN ccs.ContainerNumber END) AS UndecidedContainersWithImages
FROM ContainerCompletenessStatuses ccs
LEFT JOIN ImageAnalysisDecisions iad ON iad.ContainerNumber = ccs.ContainerNumber 
    AND iad.GroupIdentifier = ccs.GroupIdentifier
    AND iad.Decision IN ('Normal', 'Abnormal')
WHERE ccs.GroupIdentifier = @GroupIdentifier;
"@

$checkFile = [System.IO.Path]::GetTempFileName() + ".sql"
$checkSql | Out-File -FilePath $checkFile -Encoding UTF8

# Execute check (we'll parse results manually)
$results = & "C:\Program Files\Microsoft SQL Server\Client SDK\ODBC\170\Tools\Binn\SQLCMD.EXE" -S "$serverName,$port" -d $database -E -Q $checkSql -h -1 -W

# Step 2: Determine action based on status
Write-Host "`n🔍 Step 2: Analyzing situation..." -ForegroundColor Yellow

# Get current status
$statusSql = "SELECT Status FROM AnalysisGroups WHERE GroupIdentifier = '$GroupIdentifier';"
$currentStatus = & "C:\Program Files\Microsoft SQL Server\Client SDK\ODBC\170\Tools\Binn\SQLCMD.EXE" -S "$serverName,$port" -d $database -E -Q $statusSql -h -1 -W | Select-Object -First 1

Write-Host "Current Status: $currentStatus" -ForegroundColor Cyan

# Get container counts
$countSql = @"
SELECT 
    COUNT(DISTINCT CASE WHEN ccs.HasImageData = 1 THEN ccs.ContainerNumber END) AS WithImages,
    COUNT(DISTINCT CASE WHEN ccs.HasImageData = 0 THEN ccs.ContainerNumber END) AS WithoutImages,
    COUNT(DISTINCT CASE WHEN ccs.HasImageData = 1 AND iad.Decision IN ('Normal', 'Abnormal') THEN ccs.ContainerNumber END) AS DecidedWithImages,
    COUNT(DISTINCT CASE WHEN ccs.HasImageData = 1 AND (iad.Decision NOT IN ('Normal', 'Abnormal') OR iad.Decision IS NULL) THEN ccs.ContainerNumber END) AS UndecidedWithImages
FROM ContainerCompletenessStatuses ccs
LEFT JOIN ImageAnalysisDecisions iad ON iad.ContainerNumber = ccs.ContainerNumber 
    AND iad.GroupIdentifier = ccs.GroupIdentifier
    AND iad.Decision IN ('Normal', 'Abnormal')
WHERE ccs.GroupIdentifier = '$GroupIdentifier';
"@

$countResult = & "C:\Program Files\Microsoft SQL Server\Client SDK\ODBC\170\Tools\Binn\SQLCMD.EXE" -S "$serverName,$port" -d $database -E -Q $countSql -h -1 -W -s ","

# Parse counts (simplified - actual parsing would be more complex)
Write-Host "`nContainer Summary:" -ForegroundColor Cyan
Write-Host $countResult -ForegroundColor White

# Step 3: Determine fix
Write-Host "`n💡 Step 3: Determining fix..." -ForegroundColor Yellow

$fixSql = @"
DECLARE @GroupIdentifier NVARCHAR(150) = '$GroupIdentifier';
DECLARE @GroupId UNIQUEIDENTIFIER;
DECLARE @CurrentStatus NVARCHAR(20);
DECLARE @ContainersWithImages INT;
DECLARE @ContainersWithoutImages INT;
DECLARE @DecidedWithImages INT;
DECLARE @UndecidedWithImages INT;
DECLARE @TotalContainers INT;
DECLARE @NewStatus NVARCHAR(20);
DECLARE @Action NVARCHAR(100);

-- Get group info
SELECT @GroupId = Id, @CurrentStatus = Status
FROM AnalysisGroups
WHERE GroupIdentifier = @GroupIdentifier;

IF @GroupId IS NULL
BEGIN
    PRINT '❌ Error: Group not found';
    RETURN;
END

-- Get counts
SELECT 
    @ContainersWithImages = COUNT(DISTINCT CASE WHEN ccs.HasImageData = 1 THEN ccs.ContainerNumber END),
    @ContainersWithoutImages = COUNT(DISTINCT CASE WHEN ccs.HasImageData = 0 THEN ccs.ContainerNumber END),
    @DecidedWithImages = COUNT(DISTINCT CASE WHEN ccs.HasImageData = 1 AND iad.Decision IN ('Normal', 'Abnormal') THEN ccs.ContainerNumber END),
    @UndecidedWithImages = COUNT(DISTINCT CASE WHEN ccs.HasImageData = 1 AND (iad.Decision NOT IN ('Normal', 'Abnormal') OR iad.Decision IS NULL) THEN ccs.ContainerNumber END)
FROM ContainerCompletenessStatuses ccs
LEFT JOIN ImageAnalysisDecisions iad ON iad.ContainerNumber = ccs.ContainerNumber 
    AND iad.GroupIdentifier = ccs.GroupIdentifier
    AND iad.Decision IN ('Normal', 'Abnormal')
WHERE ccs.GroupIdentifier = @GroupIdentifier;

SET @TotalContainers = @ContainersWithImages + @ContainersWithoutImages;

-- Determine action
IF @UndecidedWithImages > 0
BEGIN
    SET @Action = 'Cannot fix: ' + CAST(@UndecidedWithImages AS NVARCHAR) + ' container(s) with images are still undecided';
    PRINT @Action;
    RETURN;
END

IF @ContainersWithoutImages > 0 AND @CurrentStatus IN ('AnalystCompleted', 'AuditCompleted')
BEGIN
    -- Has containers without images and is ready for submission
    SET @NewStatus = 'PartiallyCompleted';
    SET @Action = 'Moving to PartiallyCompleted (has ' + CAST(@ContainersWithoutImages AS NVARCHAR) + ' container(s) without images)';
    
    UPDATE AnalysisGroups
    SET Status = @NewStatus,
        PartiallyCompletedDate = GETUTCDATE(),
        TotalContainerCount = @TotalContainers,
        SubmittedContainerCount = @ContainersWithImages,
        PendingContainerCount = @ContainersWithoutImages,
        UpdatedAtUtc = GETUTCDATE()
    WHERE Id = @GroupId;
    
    PRINT '✅ ' + @Action;
END
ELSE IF @CurrentStatus = 'Ready' AND @DecidedWithImages = @ContainersWithImages
BEGIN
    -- All containers with images are decided, can move to AnalystCompleted
    SET @NewStatus = 'AnalystCompleted';
    SET @Action = 'Moving to AnalystCompleted (all containers with images have decisions)';
    
    UPDATE AnalysisGroups
    SET Status = @NewStatus,
        UpdatedAtUtc = GETUTCDATE()
    WHERE Id = @GroupId;
    
    PRINT '✅ ' + @Action;
END
ELSE IF @CurrentStatus = 'AnalystCompleted' AND @DecidedWithImages = @ContainersWithImages
BEGIN
    -- Can move to AuditAssigned or AuditCompleted
    SET @NewStatus = 'AuditAssigned';
    SET @Action = 'Moving to AuditAssigned (ready for audit)';
    
    UPDATE AnalysisGroups
    SET Status = @NewStatus,
        UpdatedAtUtc = GETUTCDATE()
    WHERE Id = @GroupId;
    
    PRINT '✅ ' + @Action;
END
ELSE
BEGIN
    SET @Action = 'No action needed or cannot determine next step. Current status: ' + @CurrentStatus;
    PRINT @Action;
END

-- Show final status
SELECT 
    GroupIdentifier,
    Status,
    PartiallyCompletedDate,
    TotalContainerCount,
    SubmittedContainerCount,
    PendingContainerCount
FROM AnalysisGroups
WHERE Id = @GroupId;
"@

if ($Force) {
    Write-Host "`n⚡ Executing fix (Force mode)..." -ForegroundColor Yellow
    & "C:\Program Files\Microsoft SQL Server\Client SDK\ODBC\170\Tools\Binn\SQLCMD.EXE" -S "$serverName,$port" -d $database -E -Q $fixSql -W
} else {
    Write-Host "`n⚠️  Dry run mode. Use -Force to execute the fix." -ForegroundColor Yellow
    Write-Host "`nSQL that would be executed:" -ForegroundColor Cyan
    Write-Host $fixSql -ForegroundColor Gray
}

# Cleanup
Remove-Item $checkFile -ErrorAction SilentlyContinue

Write-Host "`n✅ Done!" -ForegroundColor Green


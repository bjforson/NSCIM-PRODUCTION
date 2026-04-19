# Fix ALL stuck records where containers without images are preventing progression
# This script finds all AnalysisGroups that:
# 1. Have containers WITH images that all have decisions (in Audit stage)
# 2. Have containers WITHOUT images (stuck in ImageAnalysis stage)
# 3. Are stuck in Ready/AnalystAssigned status (should be PartiallyCompleted)
# 4. Updates them to PartiallyCompleted status

param(
    [Parameter(Mandatory=$false)]
    [switch]$DryRun = $false,
    [Parameter(Mandatory=$false)]
    [string]$GroupIdentifier = $null  # Optional: fix specific group only
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

Write-Host "`n=== Finding Stuck Records (Containers Without Images) ===" -ForegroundColor Cyan
if ($DryRun) {
    Write-Host "⚠️  DRY RUN MODE - No changes will be made" -ForegroundColor Yellow
}
Write-Host "Server: $serverName`:$port" -ForegroundColor Gray
Write-Host "Database: $database`n" -ForegroundColor Gray

try {
    # Step 1: Find all stuck groups
    Write-Host "1. Finding stuck records..." -ForegroundColor Yellow
    
    $findSql = @"
-- Find groups that match the stuck scenario:
-- 1. Status is Ready or AnalystAssigned
-- 2. Has containers WITH images that all have decisions (in Audit stage)
-- 3. Has containers WITHOUT images (stuck in ImageAnalysis stage)

WITH GroupStats AS (
    SELECT 
        ag.Id as GroupId,
        ag.GroupIdentifier,
        ag.Status as GroupStatus,
        ag.ScannerType,
        -- Count containers
        COUNT(DISTINCT ccs.ContainerNumber) as TotalContainers,
        COUNT(DISTINCT CASE WHEN ccs.HasImageData = 1 THEN ccs.ContainerNumber END) as ContainersWithImages,
        COUNT(DISTINCT CASE WHEN ccs.HasImageData = 0 THEN ccs.ContainerNumber END) as ContainersWithoutImages,
        -- Count decisions for containers with images
        COUNT(DISTINCT CASE WHEN ccs.HasImageData = 1 AND iad.Decision IN ('Normal', 'Abnormal') THEN ccs.ContainerNumber END) as DecidedWithImages,
        -- Count containers with images in Audit stage
        COUNT(DISTINCT CASE WHEN ccs.HasImageData = 1 AND ccs.WorkflowStage = 'Audit' THEN ccs.ContainerNumber END) as WithImagesInAudit,
        -- Count containers without images stuck in ImageAnalysis
        COUNT(DISTINCT CASE WHEN ccs.HasImageData = 0 AND ccs.WorkflowStage = 'ImageAnalysis' THEN ccs.ContainerNumber END) as WithoutImagesStuck
    FROM AnalysisGroups ag
    INNER JOIN AnalysisRecords ar ON ar.GroupId = ag.Id
    INNER JOIN ContainerCompletenessStatuses ccs ON ccs.ContainerNumber = ar.ContainerNumber 
        AND ccs.GroupIdentifier = ag.GroupIdentifier
    LEFT JOIN ImageAnalysisDecisions iad ON iad.ContainerNumber = ccs.ContainerNumber
        AND (iad.GroupIdentifier = ag.GroupIdentifier OR iad.GroupIdentifier LIKE ag.GroupIdentifier + '%')
        AND iad.Decision IN ('Normal', 'Abnormal')
    WHERE ag.Status IN ('Ready', 'AnalystAssigned')
    GROUP BY ag.Id, ag.GroupIdentifier, ag.Status, ag.ScannerType
)
SELECT 
    GroupIdentifier,
    GroupStatus,
    ScannerType,
    TotalContainers,
    ContainersWithImages,
    ContainersWithoutImages,
    DecidedWithImages,
    WithImagesInAudit,
    WithoutImagesStuck,
    CASE 
        WHEN ContainersWithImages > 0 
            AND DecidedWithImages = ContainersWithImages 
            AND WithImagesInAudit = ContainersWithImages
            AND ContainersWithoutImages > 0
            AND WithoutImagesStuck > 0
        THEN 'STUCK - Needs Fix'
        ELSE 'OK'
    END as StatusCheck
FROM GroupStats
WHERE ContainersWithImages > 0 
    AND DecidedWithImages = ContainersWithImages 
    AND WithImagesInAudit = ContainersWithImages
    AND ContainersWithoutImages > 0
    AND WithoutImagesStuck > 0
ORDER BY GroupIdentifier;
"@
    
    # Add filter if specific group requested
    if ($GroupIdentifier) {
        $escapedGroupId = $GroupIdentifier.Replace("'", "''")
        # Add filter to the outer WHERE clause
        $findSql = $findSql.Replace("ORDER BY GroupIdentifier;", "AND GroupIdentifier = '$escapedGroupId' ORDER BY GroupIdentifier;")
    }
    
    # Create temp file for SQL (to avoid parameter issues)
    $tempSqlFile = [System.IO.Path]::GetTempFileName() + ".sql"
    $findSql | Out-File -FilePath $tempSqlFile -Encoding UTF8
    
    $findOutput = & $sqlcmdPath -S "$serverName,$port" -d $database -E -i $tempSqlFile -W -s "|" -h -1 2>&1
    
    # Parse results
    $stuckGroups = @()
    foreach ($line in $findOutput) {
        # Skip header lines, empty lines, and status messages
        if ($line -match "rows affected" -or $line -match "GroupIdentifier" -or [string]::IsNullOrWhiteSpace($line) -or $line -match "^---") {
            continue
        }
        # Parse pipe-delimited data lines
        if ($line -match "^[A-Z0-9]") {
            $parts = $line -split "\|" | Where-Object { $_ -ne "" -and $_ -notmatch "^\s*$" }
            if ($parts.Count -ge 9) {
                try {
                    $stuckGroups += @{
                        GroupIdentifier = $parts[0].Trim()
                        GroupStatus = $parts[1].Trim()
                        ScannerType = $parts[2].Trim()
                        TotalContainers = [int]$parts[3].Trim()
                        ContainersWithImages = [int]$parts[4].Trim()
                        ContainersWithoutImages = [int]$parts[5].Trim()
                        DecidedWithImages = [int]$parts[6].Trim()
                        WithImagesInAudit = [int]$parts[7].Trim()
                        WithoutImagesStuck = [int]$parts[8].Trim()
                    }
                } catch {
                    Write-Host "   ⚠️  Error parsing line: $line" -ForegroundColor Yellow
                }
            }
        }
    }
    
    Remove-Item $tempSqlFile -ErrorAction SilentlyContinue
    
    if ($stuckGroups.Count -eq 0) {
        Write-Host "   ✅ No stuck records found!" -ForegroundColor Green
        Write-Host "`n=== DONE ===" -ForegroundColor Cyan
        exit 0
    }
    
    Write-Host "   Found $($stuckGroups.Count) stuck record(s):" -ForegroundColor Yellow
    foreach ($group in $stuckGroups) {
        Write-Host "      - $($group.GroupIdentifier): $($group.ContainersWithImages) with images (all decided), $($group.ContainersWithoutImages) without images (stuck)" -ForegroundColor White
    }
    
    if ($DryRun) {
        Write-Host "`n⚠️  DRY RUN - Would fix $($stuckGroups.Count) record(s)" -ForegroundColor Yellow
        Write-Host "`n=== DONE (DRY RUN) ===" -ForegroundColor Cyan
        exit 0
    }
    
    # Step 2: Fix each stuck group
    Write-Host "`n2. Fixing stuck records..." -ForegroundColor Yellow
    
    $fixedCount = 0
    $errorCount = 0
    
    foreach ($group in $stuckGroups) {
        $groupId = $group.GroupIdentifier
        $escapedGroupId = $groupId.Replace("'", "''")
        
        Write-Host "`n   Fixing: $groupId" -ForegroundColor Cyan
        
        try {
            $fixSql = @"
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;

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

-- Update containers WITHOUT images to PartiallyCompleted
UPDATE ContainerCompletenessStatuses
SET WorkflowStage = 'PartiallyCompleted',
    UpdatedAt = GETUTCDATE()
WHERE GroupIdentifier = '$escapedGroupId'
  AND HasImageData = 0
  AND WorkflowStage = 'ImageAnalysis';

SELECT 'Fixed' as Result, @TotalContainers as Total, @ContainersWithImages as WithImages, @ContainersWithoutImages as WithoutImages;
"@
            
            $fixOutput = & $sqlcmdPath -S "$serverName,$port" -d $database -E -Q $fixSql -W -s "|" -h -1 2>&1
            
            if ($fixOutput -match "Fixed") {
                Write-Host "      ✅ Fixed successfully" -ForegroundColor Green
                $fixedCount++
            } else {
                Write-Host "      ⚠️  Unexpected output: $($fixOutput -join ' ')" -ForegroundColor Yellow
                $errorCount++
            }
        } catch {
            Write-Host "      ❌ Error: $_" -ForegroundColor Red
            $errorCount++
        }
    }
    
    # Step 3: Summary
    Write-Host "`n=== SUMMARY ===" -ForegroundColor Cyan
    Write-Host "Total stuck records found: $($stuckGroups.Count)" -ForegroundColor White
    Write-Host "Successfully fixed: $fixedCount" -ForegroundColor Green
    if ($errorCount -gt 0) {
        Write-Host "Errors: $errorCount" -ForegroundColor Red
    }
    
    Write-Host "`n✅ Fix completed!" -ForegroundColor Green
    Write-Host "   - All stuck records have been updated to PartiallyCompleted" -ForegroundColor White
    Write-Host "   - Containers without images have been updated to PartiallyCompleted stage" -ForegroundColor White
    Write-Host "   - Groups should now be eligible for audit queue" -ForegroundColor White
    
} catch {
    Write-Host "`n❌ Error: $_" -ForegroundColor Red
    Write-Host "Stack trace: $($_.ScriptStackTrace)" -ForegroundColor Red
    exit 1
}

Write-Host "`n=== DONE ===" -ForegroundColor Cyan


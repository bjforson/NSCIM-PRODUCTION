# Update Container WorkflowStage to Audit
# This updates containers without images to Audit stage so the record can progress
# Usage: .\Update-ContainerWorkflowStage.ps1 -GroupIdentifier "80925590007"

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
Write-Host "Updating Container WorkflowStage: $GroupIdentifier" -ForegroundColor Cyan
Write-Host ""

# SQL to update containers without images to Audit stage
$sql = @"
DECLARE @GroupIdentifier NVARCHAR(150) = '$GroupIdentifier';

-- Update all containers in this group to Audit stage (even if they don't have decisions, if they have no images)
UPDATE ccs
SET ccs.WorkflowStage = 'Audit',
    ccs.UpdatedAt = GETUTCDATE()
FROM ContainerCompletenessStatuses ccs
WHERE ccs.GroupIdentifier = @GroupIdentifier
    AND ccs.WorkflowStage <> 'Audit'
    AND ccs.HasImageData = 0;  -- Only update containers without images

PRINT 'Updated containers without images to Audit stage';

-- Show updated status
SELECT ContainerNumber, HasImageData, WorkflowStage, Status
FROM ContainerCompletenessStatuses
WHERE GroupIdentifier = @GroupIdentifier
ORDER BY ContainerNumber;
"@

Write-Host "Updating containers without images to Audit stage..." -ForegroundColor Yellow
& $sqlcmdPath -S "$serverName,$port" -d $database -E -Q $sql -W

Write-Host ""
Write-Host "Done! The record should now be available for audit assignment." -ForegroundColor Green


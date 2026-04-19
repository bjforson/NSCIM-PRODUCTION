# Check Stuck Image Analysis Record
# Usage: .\Check-StuckImageAnalysisRecord.ps1 -GroupIdentifier "80925590007"

param(
    [Parameter(Mandatory=$true)]
    [string]$GroupIdentifier
)

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

Write-Host ""
Write-Host "Checking Image Analysis Record: $GroupIdentifier" -ForegroundColor Cyan
Write-Host "Server: $serverName`:$port" -ForegroundColor Gray
Write-Host "Database: $database" -ForegroundColor Gray
Write-Host ""

$sqlcmdPath = "C:\Program Files\Microsoft SQL Server\Client SDK\ODBC\170\Tools\Binn\SQLCMD.EXE"

Write-Host "Analysis Group Status:" -ForegroundColor Yellow
$sql1 = "SELECT Id, GroupIdentifier, Status, ScannerType, PartiallyCompletedDate, TotalContainerCount, SubmittedContainerCount, PendingContainerCount, CreatedAtUtc, UpdatedAtUtc FROM AnalysisGroups WHERE GroupIdentifier = '$GroupIdentifier';"
& $sqlcmdPath -S "$serverName,$port" -d $database -E -Q $sql1 -W -s ","

Write-Host ""
Write-Host "Container Status:" -ForegroundColor Yellow
$sql2 = "SELECT ContainerNumber, HasImageData, HasICUMSData, WorkflowStage, Status FROM ContainerCompletenessStatuses WHERE GroupIdentifier = '$GroupIdentifier' ORDER BY ContainerNumber;"
& $sqlcmdPath -S "$serverName,$port" -d $database -E -Q $sql2 -W -s ","

Write-Host ""
Write-Host "Decisions Made:" -ForegroundColor Yellow
$sql3 = "SELECT ContainerNumber, Decision, ReviewedBy, ReviewedAt, IsConsolidated FROM ImageAnalysisDecisions WHERE GroupIdentifier = '$GroupIdentifier' ORDER BY ContainerNumber;"
& $sqlcmdPath -S "$serverName,$port" -d $database -E -Q $sql3 -W -s ","

Write-Host ""
Write-Host "Summary:" -ForegroundColor Yellow
$sql4 = "SELECT CASE WHEN ccs.HasImageData = 1 THEN 'Has Images' ELSE 'No Images' END AS ImageStatus, COUNT(DISTINCT ccs.ContainerNumber) AS ContainerCount, COUNT(DISTINCT CASE WHEN iad.Decision IN ('Normal', 'Abnormal') THEN ccs.ContainerNumber END) AS DecidedCount, COUNT(DISTINCT CASE WHEN iad.Decision NOT IN ('Normal', 'Abnormal') OR iad.Decision IS NULL THEN ccs.ContainerNumber END) AS UndecidedCount FROM ContainerCompletenessStatuses ccs LEFT JOIN ImageAnalysisDecisions iad ON iad.ContainerNumber = ccs.ContainerNumber AND iad.GroupIdentifier = ccs.GroupIdentifier AND iad.Decision IN ('Normal', 'Abnormal') WHERE ccs.GroupIdentifier = '$GroupIdentifier' GROUP BY CASE WHEN ccs.HasImageData = 1 THEN 'Has Images' ELSE 'No Images' END;"
& $sqlcmdPath -S "$serverName,$port" -d $database -E -Q $sql4 -W -s ","

Write-Host ""
Write-Host "Next Steps:" -ForegroundColor Green
Write-Host "1. If all containers WITH images have decisions, the record should be able to progress" -ForegroundColor White
Write-Host "2. If stuck, you can manually move it to PartiallyCompleted status" -ForegroundColor White
Write-Host "3. Use Fix-StuckImageAnalysisRecord.ps1 to fix the record" -ForegroundColor White

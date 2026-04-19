# Quick check of WorkflowStage for a specific group
param(
    [Parameter(Mandatory=$true)]
    [string]$GroupIdentifier
)

$appsettingsPath = "src\NickScanCentralImagingPortal.API\appsettings.json"
$appsettings = Get-Content $appsettingsPath | ConvertFrom-Json
$connectionString = $appsettings.ConnectionStrings.NS_CIS_Connection

$connection = New-Object System.Data.SqlClient.SqlConnection($connectionString)
$connection.Open()

$query = @"
SELECT 
    ContainerNumber,
    ScannerType,
    WorkflowStage,
    Status,
    GroupIdentifier
FROM ContainerCompletenessStatuses
WHERE GroupIdentifier = @GroupIdentifier
ORDER BY ContainerNumber
"@

$command = New-Object System.Data.SqlClient.SqlCommand($query, $connection)
$command.Parameters.AddWithValue("@GroupIdentifier", $GroupIdentifier)
$reader = $command.ExecuteReader()

Write-Host "WorkflowStage for Group: $GroupIdentifier" -ForegroundColor Cyan
Write-Host ""

$allAudit = $true
$count = 0

while ($reader.Read()) {
    $count++
    $container = $reader["ContainerNumber"]
    $scanner = $reader["ScannerType"]
    $stage = $reader["WorkflowStage"]
    $status = $reader["Status"]
    
    if ($stage -ne "Audit") {
        $allAudit = $false
    }
    
    $color = if ($stage -eq "Audit") { "Green" } else { "Yellow" }
    Write-Host "  Container: $container ($scanner) - WorkflowStage: $stage | Status: $status" -ForegroundColor $color
}

$reader.Close()
$connection.Close()

Write-Host ""
if ($count -eq 0) {
    Write-Host "❌ No records found for GroupIdentifier: $GroupIdentifier" -ForegroundColor Red
} elseif ($allAudit) {
    Write-Host "✅ All containers have WorkflowStage = 'Audit' - Group SHOULD appear in audit queue" -ForegroundColor Green
} else {
    Write-Host "⚠️ Not all containers have WorkflowStage = 'Audit' - Group will NOT appear in audit queue" -ForegroundColor Yellow
    Write-Host "   → Need to check why WorkflowStage is not 'Audit'" -ForegroundColor Yellow
}


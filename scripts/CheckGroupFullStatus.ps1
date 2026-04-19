# Check full status of a group
param(
    [Parameter(Mandatory=$true)]
    [string]$GroupIdentifier
)

$appsettingsPath = "src\NickScanCentralImagingPortal.API\appsettings.json"
$appsettings = Get-Content $appsettingsPath | ConvertFrom-Json
$connectionString = $appsettings.ConnectionStrings.NS_CIS_Connection

$connection = New-Object System.Data.SqlClient.SqlConnection($connectionString)
$connection.Open()

Write-Host "Full Status Check for Group: $GroupIdentifier" -ForegroundColor Cyan
Write-Host "==============================================" -ForegroundColor Cyan
Write-Host ""

# 1. AnalysisGroup Status
Write-Host "1. AnalysisGroup Status:" -ForegroundColor Yellow
$query1 = @"
SELECT Status, ScannerType, CreatedAtUtc, UpdatedAtUtc
FROM AnalysisGroups
WHERE GroupIdentifier = @GroupIdentifier
"@
$cmd1 = New-Object System.Data.SqlClient.SqlCommand($query1, $connection)
$cmd1.Parameters.AddWithValue("@GroupIdentifier", $GroupIdentifier)
$reader1 = $cmd1.ExecuteReader()
if ($reader1.Read()) {
    Write-Host "   Status: $($reader1['Status'])" -ForegroundColor White
    Write-Host "   ScannerType: $($reader1['ScannerType'])" -ForegroundColor White
} else {
    Write-Host "   ❌ No AnalysisGroup found" -ForegroundColor Red
}
$reader1.Close()

Write-Host ""

# 2. WorkflowStage
Write-Host "2. ContainerCompletenessStatus WorkflowStage:" -ForegroundColor Yellow
$query2 = @"
SELECT WorkflowStage, Status, ContainerNumber
FROM ContainerCompletenessStatuses
WHERE GroupIdentifier = @GroupIdentifier
"@
$cmd2 = New-Object System.Data.SqlClient.SqlCommand($query2, $connection)
$cmd2.Parameters.AddWithValue("@GroupIdentifier", $GroupIdentifier)
$reader2 = $cmd2.ExecuteReader()
while ($reader2.Read()) {
    $stage = $reader2["WorkflowStage"]
    $color = if ($stage -eq "Audit") { "Green" } elseif ($stage -eq "Completed") { "Red" } else { "Yellow" }
    Write-Host "   Container: $($reader2['ContainerNumber']) - WorkflowStage: $stage" -ForegroundColor $color
}
$reader2.Close()

Write-Host ""

# 3. ImageAnalysisDecisions
Write-Host "3. ImageAnalysisDecisions:" -ForegroundColor Yellow
$query3 = @"
SELECT ContainerNumber, Decision, ReviewedBy, ReviewedAt
FROM ImageAnalysisDecisions
WHERE GroupIdentifier = @GroupIdentifier
"@
$cmd3 = New-Object System.Data.SqlClient.SqlCommand($query3, $connection)
$cmd3.Parameters.AddWithValue("@GroupIdentifier", $GroupIdentifier)
$reader3 = $cmd3.ExecuteReader()
$hasDecisions = $false
while ($reader3.Read()) {
    $hasDecisions = $true
    Write-Host "   Container: $($reader3['ContainerNumber']) - Decision: $($reader3['Decision']) (by $($reader3['ReviewedBy']))" -ForegroundColor White
}
if (-not $hasDecisions) {
    Write-Host "   ⚠️ No analyst decisions found" -ForegroundColor Yellow
}
$reader3.Close()

Write-Host ""

# 4. AuditDecisions
Write-Host "4. AuditDecisions:" -ForegroundColor Yellow
$query4 = @"
SELECT ContainerNumber, Decision, AuditedBy, AuditedAt, IsCompleted
FROM AuditDecisions
WHERE GroupIdentifier = @GroupIdentifier
"@
$cmd4 = New-Object System.Data.SqlClient.SqlCommand($query4, $connection)
$cmd4.Parameters.AddWithValue("@GroupIdentifier", $GroupIdentifier)
$reader4 = $cmd4.ExecuteReader()
$hasAudit = $false
while ($reader4.Read()) {
    $hasAudit = $true
    Write-Host "   Container: $($reader4['ContainerNumber']) - Decision: $($reader4['Decision']) (by $($reader4['AuditedBy'])), Completed: $($reader4['IsCompleted'])" -ForegroundColor White
}
if (-not $hasAudit) {
    Write-Host "   ⚠️ No audit decisions found" -ForegroundColor Yellow
}
$reader4.Close()

$connection.Close()

Write-Host ""
Write-Host "==============================================" -ForegroundColor Cyan
Write-Host "DIAGNOSIS:" -ForegroundColor Cyan
Write-Host "==============================================" -ForegroundColor Cyan

if ($hasAudit) {
    Write-Host "✅ Group has been audited - WorkflowStage = 'Completed' is correct" -ForegroundColor Green
    Write-Host "   → Group should NOT appear in audit queue (already completed)" -ForegroundColor Green
} elseif ($hasDecisions) {
    Write-Host "⚠️ Group has analyst decisions but WorkflowStage = 'Completed'" -ForegroundColor Yellow
    Write-Host "   → WorkflowStage should be 'Audit' if not yet audited" -ForegroundColor Yellow
    Write-Host "   → May need to update WorkflowStage back to 'Audit'" -ForegroundColor Yellow
} else {
    Write-Host "❌ Group has no analyst decisions" -ForegroundColor Red
    Write-Host "   → Cannot move to audit until analyst decisions are saved" -ForegroundColor Red
}


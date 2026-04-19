# IntakeWorker Pipeline Diagnostic
# Confirms whether IntakeWorker is picking up ContainerCompletenessStatus and creating AnalysisGroups
# Usage: .\scripts\Run-IntakeWorker-Pipeline-Diagnostic.ps1

$ErrorActionPreference = "Stop"
$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$projectRoot = Split-Path -Parent $scriptDir
$outputPath = Join-Path $projectRoot "intake_worker_pipeline_result.txt"

$appsettingsPath = Join-Path $projectRoot "src\NickScanCentralImagingPortal.API\appsettings.json"
$appsettings = Get-Content $appsettingsPath -Raw | ConvertFrom-Json
$connString = $appsettings.ConnectionStrings.NS_CIS_Connection
if ($connString -match 'Server=([^;]+)') { $ServerInstance = $Matches[1].Trim() } else { $ServerInstance = '127.0.0.1,1433' }
if ($connString -match 'Database=([^;]+)') { $Database = $Matches[1].Trim() } else { $Database = 'NS_CIS' }

$output = @()
Write-Host "IntakeWorker Pipeline Diagnostic" -ForegroundColor Cyan
Write-Host "Output: $outputPath" -ForegroundColor Gray
Write-Host ""

try {
    # 1. Eligible completeness rows
    $q1 = @"
SELECT WorkflowStage, COUNT(*) AS ContainerCount, COUNT(DISTINCT GroupIdentifier) AS UniqueGroups
FROM ContainerCompletenessStatuses
WHERE Status LIKE 'Complete%'
    AND (WorkflowStage = 'Pending' OR WorkflowStage = 'ImageAnalysis' OR WorkflowStage IS NULL OR WorkflowStage = '')
GROUP BY WorkflowStage
ORDER BY ContainerCount DESC
"@
    Write-Host "=== 1. ContainerCompletenessStatus: Eligible for IntakeWorker ===" -ForegroundColor Yellow
    $r1 = Invoke-Sqlcmd -ServerInstance $ServerInstance -Database $Database -Query $q1
    $r1 | Format-Table -AutoSize
    $output += "=== 1. Eligible for IntakeWorker ===`n" + ($r1 | Format-Table -AutoSize | Out-String)

    # 2. Summary
    $q2 = @"
SELECT COUNT(*) AS EligibleContainers, COUNT(DISTINCT GroupIdentifier) AS EligibleGroups
FROM ContainerCompletenessStatuses
WHERE Status LIKE 'Complete%'
    AND (WorkflowStage = 'Pending' OR WorkflowStage = 'ImageAnalysis' OR WorkflowStage IS NULL OR WorkflowStage = '')
"@
    Write-Host "`n=== 2. Summary: Input for IntakeWorker ===" -ForegroundColor Yellow
    $r2 = Invoke-Sqlcmd -ServerInstance $ServerInstance -Database $Database -Query $q2
    $r2 | Format-Table -AutoSize
    $output += "`n=== 2. Summary ===`n" + ($r2 | Format-Table -AutoSize | Out-String)

    # 3. Groups created last 24h
    $q3 = "SELECT COUNT(*) AS GroupsCreatedLast24h FROM AnalysisGroups WHERE CreatedAtUtc >= DATEADD(HOUR, -24, GETUTCDATE())"
    Write-Host "`n=== 3. AnalysisGroups: Created in last 24h ===" -ForegroundColor Yellow
    $r3 = Invoke-Sqlcmd -ServerInstance $ServerInstance -Database $Database -Query $q3
    $r3 | Format-Table -AutoSize
    $output += "`n=== 3. Groups created last 24h ===`n" + ($r3 | Format-Table -AutoSize | Out-String)

    # 4. Groups by status
    $q4 = "SELECT Status, COUNT(*) AS GroupCount FROM AnalysisGroups GROUP BY Status ORDER BY GroupCount DESC"
    Write-Host "`n=== 4. AnalysisGroups: By Status ===" -ForegroundColor Yellow
    $r4 = Invoke-Sqlcmd -ServerInstance $ServerInstance -Database $Database -Query $q4
    $r4 | Format-Table -AutoSize
    $output += "`n=== 4. Groups by Status ===`n" + ($r4 | Format-Table -AutoSize | Out-String)

    # 5. Ready count
    $q5 = "SELECT COUNT(*) AS ReadyForAssignment FROM AnalysisGroups WHERE Status = 'Ready'"
    Write-Host "`n=== 5. Ready for AssignmentWorker ===" -ForegroundColor Yellow
    $r5 = Invoke-Sqlcmd -ServerInstance $ServerInstance -Database $Database -Query $q5
    $r5 | Format-Table -AutoSize
    $output += "`n=== 5. Ready for Assignment ===`n" + ($r5 | Format-Table -AutoSize | Out-String)

    # 6. Recent groups
    $q6 = "SELECT TOP 10 GroupIdentifier, Status, CreatedAtUtc FROM AnalysisGroups ORDER BY CreatedAtUtc DESC"
    Write-Host "`n=== 6. Recent AnalysisGroups (last 10) ===" -ForegroundColor Yellow
    $r6 = Invoke-Sqlcmd -ServerInstance $ServerInstance -Database $Database -Query $q6
    $r6 | Format-Table -AutoSize
    $output += "`n=== 6. Recent groups ===`n" + ($r6 | Format-Table -AutoSize | Out-String)

    $output | Set-Content $outputPath -Encoding UTF8
    Write-Host "`nResults saved to $outputPath" -ForegroundColor Green

    # Verdict
    $eligible = $r2.EligibleContainers
    $ready = $r5.ReadyForAssignment
    $last24h = $r3.GroupsCreatedLast24h
    Write-Host ""
    if ($eligible -eq 0) {
        Write-Host "VERDICT: No containers eligible for IntakeWorker. Check ContainerCompletenessService." -ForegroundColor Yellow
    } elseif ($last24h -eq 0) {
        Write-Host "VERDICT: No groups created in 24h. IntakeWorker may not be running or may be blocked." -ForegroundColor Yellow
    } else {
        Write-Host "VERDICT: IntakeWorker IS picking up records ($eligible eligible, $last24h groups created in 24h, $ready Ready)." -ForegroundColor Green
    }
} catch {
    Write-Host "Error: $_" -ForegroundColor Red
    throw
}

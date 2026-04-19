# IntakeWorker Blocker Diagnostic - Sequential next steps
# Usage: .\scripts\Run-IntakeWorker-Blocker-Diagnostic.ps1

$ErrorActionPreference = "Stop"
$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$projectRoot = Split-Path -Parent $scriptDir
$outputPath = Join-Path $projectRoot "intake_worker_blocker_result.txt"

$appsettingsPath = Join-Path $projectRoot "src\NickScanCentralImagingPortal.API\appsettings.json"
$appsettings = Get-Content $appsettingsPath -Raw | ConvertFrom-Json
$connString = $appsettings.ConnectionStrings.NS_CIS_Connection
if ($connString -match 'Server=([^;]+)') { $ServerInstance = $Matches[1].Trim() } else { $ServerInstance = '127.0.0.1,1433' }
if ($connString -match 'Database=([^;]+)') { $Database = $Matches[1].Trim() } else { $Database = 'NS_CIS' }

$output = @()
Write-Host "=== IntakeWorker Blocker Diagnostic (Sequential) ===" -ForegroundColor Cyan
Write-Host ""

# Step 1: IntakeWorker config
Write-Host "STEP 1: IntakeWorker enabled in config" -ForegroundColor Yellow
$iwEnabled = $appsettings.BackgroundServices.IntakeWorker.Enabled
$output += "=== STEP 1: Config ===`nIntakeWorker.Enabled = $iwEnabled`n"
if ($iwEnabled) {
    Write-Host "  OK: IntakeWorker.Enabled = true (ImageAnalysisOrchestratorService runs intake)" -ForegroundColor Green
} else {
    Write-Host "  BLOCKER: IntakeWorker.Enabled = false" -ForegroundColor Red
}
Write-Host ""

# Step 2: Log location (no app log files in project)
Write-Host "STEP 2: API logs" -ForegroundColor Yellow
$logPaths = @(
    (Join-Path $env:ProgramData "NickScan\logs"),
    (Join-Path $projectRoot "logs"),
    "C:\NickScan\logs"
)
$foundLogs = $false
foreach ($p in $logPaths) {
    if (Test-Path $p) {
        $recent = Get-ChildItem $p -Filter "*.log" -ErrorAction SilentlyContinue | Sort-Object LastWriteTime -Descending | Select-Object -First 3
        if ($recent) {
            Write-Host "  Logs at: $p" -ForegroundColor Cyan
            $recent | ForEach-Object { Write-Host "    - $($_.Name) ($($_.LastWriteTime))" }
            $foundLogs = $true
        }
    }
}
if (-not $foundLogs) {
    Write-Host "  No log files found in standard locations. Check API stdout/Event Viewer." -ForegroundColor Gray
}
$output += "`n=== STEP 2: Logs ===`nNo project log files. Check API stdout/Event Viewer.`n"
Write-Host ""

# Step 3: AnalysisSettings.Enabled in database
Write-Host "STEP 3: AnalysisSettings.Enabled in database" -ForegroundColor Yellow
$q3 = "SELECT Id, [Enabled], AssignmentMode FROM AnalysisSettings"
$r3 = Invoke-Sqlcmd -ServerInstance $ServerInstance -Database $Database -Query $q3
$r3 | Format-Table -AutoSize
$output += "`n=== STEP 3: AnalysisSettings ===`n" + ($r3 | Format-Table -AutoSize | Out-String)

if ($r3.Enabled -eq $true) {
    Write-Host "  OK: AnalysisSettings.Enabled = true" -ForegroundColor Green
} else {
    Write-Host "  BLOCKER: AnalysisSettings.Enabled = false - Intake exits immediately!" -ForegroundColor Red
}
Write-Host ""

# Step 4: Intake logic - new groups available?
Write-Host "STEP 4: Completeness rows eligible for NEW groups (Pending/null, GroupIdentifier not in AnalysisGroups)" -ForegroundColor Yellow
$q4 = @"
SELECT COUNT(*) AS NewGroupsAvailable
FROM ContainerCompletenessStatuses c
WHERE c.Status LIKE 'Complete%'
    AND (c.WorkflowStage = 'Pending' OR c.WorkflowStage IS NULL OR c.WorkflowStage = '')
    AND c.GroupIdentifier IS NOT NULL AND c.GroupIdentifier <> ''
    AND NOT EXISTS (SELECT 1 FROM AnalysisGroups g WHERE g.GroupIdentifier = c.GroupIdentifier)
"@
$r4 = Invoke-Sqlcmd -ServerInstance $ServerInstance -Database $Database -Query $q4
$r4 | Format-Table -AutoSize
$output += "`n=== STEP 4: New groups available ===`n" + ($r4 | Format-Table -AutoSize | Out-String)

if ($r4.NewGroupsAvailable -gt 0) {
    Write-Host "  OK: $($r4.NewGroupsAvailable) completeness rows would create NEW groups" -ForegroundColor Green
} else {
    Write-Host "  BLOCKER: 0 new groups available - all Pending containers already have AnalysisGroups!" -ForegroundColor Red
    Write-Host "  (Orchestrator excludes ImageAnalysis + existing GroupIdentifiers)" -ForegroundColor Gray
}
Write-Host ""

$output | Set-Content $outputPath -Encoding UTF8
Write-Host "Results saved to $outputPath" -ForegroundColor Green

# Check Record Decision Status
# Usage: .\Check-RecordDecisionStatus.ps1 -GroupIdentifier "11025667950"

param(
    [Parameter(Mandatory=$true)]
    [string]$GroupIdentifier
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

Write-Host "`n=== Checking Record Decision Status ===" -ForegroundColor Cyan
Write-Host "GroupIdentifier: $GroupIdentifier" -ForegroundColor Yellow
Write-Host "Server: $serverName`:$port" -ForegroundColor Gray
Write-Host "Database: $database`n" -ForegroundColor Gray

try {
    
    # 1. Check AnalysisGroup
    Write-Host "1. AnalysisGroup:" -ForegroundColor Green
    $groupSql = "SELECT Id, GroupIdentifier, ScannerType, Status, CreatedAtUtc, TotalContainerCount, SubmittedContainerCount, PendingContainerCount FROM AnalysisGroups WITH (NOLOCK) WHERE GroupIdentifier = '$($GroupIdentifier.Replace("'", "''"))';"
    $groupOutput = & $sqlcmdPath -S "$serverName,$port" -d $database -E -Q $groupSql -W -s "," -h -1 2>&1
    
    if ($groupOutput -match "rows affected" -or $groupOutput.Count -eq 0) {
        Write-Host "   ❌ No AnalysisGroup found with GroupIdentifier: $GroupIdentifier" -ForegroundColor Red
        $groupFound = $false
    } else {
        Write-Host "   ✅ Found AnalysisGroup:" -ForegroundColor Green
        $groupOutput | ForEach-Object { Write-Host "      $_" -ForegroundColor White }
        $groupFound = $true
    }
    
    # 2. Check AnalysisRecords
    Write-Host "`n2. AnalysisRecords:" -ForegroundColor Green
    $recordsSql = "SELECT Id, GroupId, ContainerNumber, ScannerType, Status, CreatedAtUtc FROM AnalysisRecords WITH (NOLOCK) WHERE GroupId IN (SELECT Id FROM AnalysisGroups WHERE GroupIdentifier = '$($GroupIdentifier.Replace("'", "''"))') ORDER BY ContainerNumber;"
    $recordsOutput = & $sqlcmdPath -S "$serverName,$port" -d $database -E -Q $recordsSql -W -s "," -h -1 2>&1
    
    if ($recordsOutput -match "rows affected" -or $recordsOutput.Count -eq 0) {
        Write-Host "   ❌ No AnalysisRecords found" -ForegroundColor Red
        $containers = @()
    } else {
        Write-Host "   ✅ Found AnalysisRecord(s):" -ForegroundColor Green
        $recordsOutput | ForEach-Object { Write-Host "      $_" -ForegroundColor White }
        # Extract container numbers from output
        $containers = $recordsOutput | Where-Object { $_ -match "^[^,]+," } | ForEach-Object { ($_ -split ",")[2].Trim() } | Where-Object { -not [string]::IsNullOrEmpty($_) }
    }
    
    # 3. Check ImageAnalysisDecisions
    Write-Host "`n3. ImageAnalysisDecisions:" -ForegroundColor Green
    $containerList = if ($containers.Count -gt 0) { "'" + ($containers -join "','") + "'" } else { "''" }
    $decisionsSql = "SELECT Id, ContainerNumber, ScannerType, Decision, GroupIdentifier, ReviewedBy, ReviewedAt, CreatedAt, UpdatedAt FROM ImageAnalysisDecisions WITH (NOLOCK) WHERE GroupIdentifier = '$($GroupIdentifier.Replace("'", "''"))' OR ContainerNumber IN ($containerList) ORDER BY ContainerNumber, ScannerType;"
    $decisionsOutput = & $sqlcmdPath -S "$serverName,$port" -d $database -E -Q $decisionsSql -W -s "," -h -1 2>&1
    
    if ($decisionsOutput -match "rows affected" -or $decisionsOutput.Count -eq 0) {
        Write-Host "   ❌ No ImageAnalysisDecisions found" -ForegroundColor Red
        $decidedContainers = @()
    } else {
        Write-Host "   ✅ Found Decision(s):" -ForegroundColor Green
        $decisionsOutput | ForEach-Object { Write-Host "      $_" -ForegroundColor White }
        # Extract decided containers
        $decidedContainers = $decisionsOutput | Where-Object { 
            $_ -match "^[^,]+," -and 
            ($_ -split ",")[3].Trim() -notmatch "^(NULL|Pending|)$" 
        } | ForEach-Object { ($_ -split ",")[1].Trim() } | Where-Object { -not [string]::IsNullOrEmpty($_) } | Select-Object -Unique
    }
    
    # 4. Check ContainerCompletenessStatus
    Write-Host "`n4. ContainerCompletenessStatus:" -ForegroundColor Green
    if ($containers.Count -eq 0) {
        Write-Host "   ⚠️ No containers to check (no AnalysisRecords found)" -ForegroundColor Yellow
    } else {
        $containerList = "'" + ($containers -join "','") + "'"
        $completenessSql = "SELECT ContainerNumber, GroupIdentifier, ScannerType, HasImageData, HasICUMSData, WorkflowStage, ScanDate FROM ContainerCompletenessStatuses WITH (NOLOCK) WHERE ContainerNumber IN ($containerList) ORDER BY ContainerNumber;"
        $completenessOutput = & $sqlcmdPath -S "$serverName,$port" -d $database -E -Q $completenessSql -W -s "," -h -1 2>&1
        
        if ($completenessOutput -match "rows affected" -or $completenessOutput.Count -eq 0) {
            Write-Host "   ❌ No ContainerCompletenessStatus found for containers: $($containers -join ', ')" -ForegroundColor Red
        } else {
            Write-Host "   ✅ Found ContainerCompletenessStatus record(s):" -ForegroundColor Green
            $completenessOutput | ForEach-Object { Write-Host "      $_" -ForegroundColor White }
        }
    }
    
    # 5. Summary Analysis
    Write-Host "`n5. Summary Analysis:" -ForegroundColor Green
    $containerCount = $containers.Count
    $decisionCount = if ($decisionsOutput -match "rows affected") { 0 } else { ($decisionsOutput | Where-Object { $_ -match "^[^,]+," }).Count }
    
    Write-Host "   - Total Containers: $containerCount" -ForegroundColor White
    Write-Host "   - Total Decisions: $decisionCount" -ForegroundColor White
    Write-Host "   - Decided Containers: $($decidedContainers.Count) - $($decidedContainers -join ', ')" -ForegroundColor White
    
    if ($containerCount -gt 0) {
        $pendingCount = $containerCount - $decidedContainers.Count
        Write-Host "   - Pending Containers: $pendingCount" -ForegroundColor $(if ($pendingCount -gt 0) { "Yellow" } else { "Green" })
        
        if ($pendingCount -gt 0) {
            $pendingContainers = $containers | Where-Object { $decidedContainers -notcontains $_ }
            Write-Host "   - Pending Container List: $($pendingContainers -join ', ')" -ForegroundColor Yellow
        }
    }
    
    # 6. Check for GroupIdentifier mismatch
    Write-Host "`n6. GroupIdentifier Consistency Check:" -ForegroundColor Green
    if ($decisionsOutput -notmatch "rows affected" -and $decisionsOutput.Count -gt 0) {
        $decisionGroupIds = $decisionsOutput | Where-Object { $_ -match "^[^,]+," } | ForEach-Object { ($_ -split ",")[4].Trim() } | Where-Object { -not [string]::IsNullOrEmpty($_) } | Select-Object -Unique
        
        Write-Host "   - AnalysisGroup.GroupIdentifier: $GroupIdentifier" -ForegroundColor White
        Write-Host "   - Decision GroupIdentifiers: $($decisionGroupIds -join ', ')" -ForegroundColor White
        
        if ($decisionGroupIds.Count -gt 0) {
            $mismatch = $decisionGroupIds | Where-Object { $_ -ne $GroupIdentifier }
            if ($mismatch.Count -gt 0) {
                Write-Host "   ⚠️ MISMATCH DETECTED: Decisions have different GroupIdentifier!" -ForegroundColor Red
                Write-Host "      This could cause the summary to show incorrect pending count." -ForegroundColor Red
            } else {
                Write-Host "   ✅ GroupIdentifiers match" -ForegroundColor Green
            }
        }
    }
    
    Write-Host "`n=== Check Complete ===" -ForegroundColor Cyan
    
} catch {
    Write-Host "`n❌ Error: $($_.Exception.Message)" -ForegroundColor Red
    Write-Host $_.ScriptStackTrace -ForegroundColor Red
}


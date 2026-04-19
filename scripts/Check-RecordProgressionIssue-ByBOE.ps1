# Check why records with missing images are not progressing
# Usage: .\scripts\Check-RecordProgressionIssue-ByBOE.ps1 -GroupIdentifier "40925581989"
# Note: GroupIdentifier is the BOE/Declaration Number for non-consolidated cargo

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

Write-Host "`n=== Checking Record Progression Issue (BOE/GroupIdentifier) ===" -ForegroundColor Cyan
Write-Host "GroupIdentifier (BOE Number): $GroupIdentifier" -ForegroundColor Yellow
Write-Host "Server: $serverName`:$port" -ForegroundColor Gray
Write-Host "Database: $database`n" -ForegroundColor Gray

try {
    
    # 1. Check AnalysisGroup (search for exact match and date-suffixed versions)
    Write-Host "1. AnalysisGroup Status:" -ForegroundColor Green
    $groupSql = "SELECT Id, GroupIdentifier, ScannerType, Status, CreatedAtUtc, TotalContainerCount, SubmittedContainerCount, PendingContainerCount, PartiallyCompletedDate, UpdatedAtUtc FROM AnalysisGroups WITH (NOLOCK) WHERE GroupIdentifier = '$($GroupIdentifier.Replace("'", "''"))' OR GroupIdentifier LIKE '$($GroupIdentifier.Replace("'", "''"))%' ORDER BY GroupIdentifier;"
    $groupOutput = & $sqlcmdPath -S "$serverName,$port" -d $database -E -Q $groupSql -W -s "," -h -1 2>&1
    
    if ($groupOutput -match "rows affected" -or $groupOutput.Count -eq 0) {
        Write-Host "   ❌ No AnalysisGroup found with GroupIdentifier: $GroupIdentifier" -ForegroundColor Red
        Write-Host "   ⚠️ Checking if it might be in ContainerCompletenessStatus only..." -ForegroundColor Yellow
    } else {
        Write-Host "   ✅ Found AnalysisGroup(s):" -ForegroundColor Green
        $groupOutput | ForEach-Object { 
            if ($_ -match "^[0-9A-Fa-f-]") {
                Write-Host "      $_" -ForegroundColor White
            }
        }
    }
    
    # 2. Check ContainerCompletenessStatus (this is where GroupIdentifier is stored for non-consolidated)
    Write-Host "`n2. ContainerCompletenessStatus (Image Availability):" -ForegroundColor Green
    $ccsSql = "SELECT ContainerNumber, GroupIdentifier, ScannerType, HasImageData, WorkflowStage, CreatedAt, UpdatedAt FROM ContainerCompletenessStatuses WITH (NOLOCK) WHERE GroupIdentifier = '$($GroupIdentifier.Replace("'", "''"))' OR GroupIdentifier LIKE '$($GroupIdentifier.Replace("'", "''"))%' ORDER BY ContainerNumber;"
    $ccsOutput = & $sqlcmdPath -S "$serverName,$port" -d $database -E -Q $ccsSql -W -s "," -h -1 2>&1
    
    if ($ccsOutput -match "rows affected" -or $ccsOutput.Count -eq 0) {
        Write-Host "   ❌ No ContainerCompletenessStatus found with GroupIdentifier: $GroupIdentifier" -ForegroundColor Red
        $containers = @()
        $containersWithImages = @()
        $containersWithoutImages = @()
    } else {
        Write-Host "   ✅ Found Container(s):" -ForegroundColor Green
        $ccsOutput | ForEach-Object { 
            if ($_ -match "^[A-Z]") {
                Write-Host "      $_" -ForegroundColor White
            }
        }
        $containers = $ccsOutput | Where-Object { $_ -match "^[A-Z]" } | ForEach-Object { 
            $parts = $_ -split ","
            if ($parts.Count -ge 1) { $parts[0].Trim() }
        } | Where-Object { -not [string]::IsNullOrEmpty($_) }
        
        $containersWithImages = $ccsOutput | Where-Object { $_ -match "^[A-Z]" } | ForEach-Object { 
            $parts = $_ -split ","
            if ($parts.Count -ge 4 -and $parts[3].Trim() -eq "True") { $parts[0].Trim() }
        } | Where-Object { -not [string]::IsNullOrEmpty($_) }
        
        $containersWithoutImages = $containers | Where-Object { $containersWithImages -notcontains $_ }
        
        Write-Host "`n   Summary:" -ForegroundColor Yellow
        Write-Host "      Total containers: $($containers.Count)" -ForegroundColor White
        Write-Host "      Containers WITH images: $($containersWithImages.Count)" -ForegroundColor $(if ($containersWithImages.Count -gt 0) { "Green" } else { "Red" })
        Write-Host "      Containers WITHOUT images: $($containersWithoutImages.Count)" -ForegroundColor $(if ($containersWithoutImages.Count -gt 0) { "Yellow" } else { "Green" })
    }
    
    # 3. Check AnalysisRecords (containers in group)
    Write-Host "`n3. AnalysisRecords (Containers in Group):" -ForegroundColor Green
    if ($containers.Count -gt 0) {
        $containerList = "'" + ($containers -join "','") + "'"
        $recordsSql = "SELECT ar.ContainerNumber, ag.GroupIdentifier, ag.Status FROM AnalysisRecords ar INNER JOIN AnalysisGroups ag ON ar.GroupId = ag.Id WHERE ar.ContainerNumber IN ($containerList) ORDER BY ar.ContainerNumber;"
        $recordsOutput = & $sqlcmdPath -S "$serverName,$port" -d $database -E -Q $recordsSql -W -s "," -h -1 2>&1
        
        if ($recordsOutput -match "rows affected" -or $recordsOutput.Count -eq 0) {
            Write-Host "   ⚠️ No AnalysisRecords found for these containers" -ForegroundColor Yellow
            Write-Host "   ⚠️ This means IntakeWorker hasn't created AnalysisGroup/AnalysisRecords yet" -ForegroundColor Yellow
        } else {
            Write-Host "   ✅ Found AnalysisRecord(s):" -ForegroundColor Green
            $recordsOutput | ForEach-Object { 
                if ($_ -match "^[A-Z]") {
                    Write-Host "      $_" -ForegroundColor White
                }
            }
        }
    } else {
        Write-Host "   ⚠️ No containers found to check" -ForegroundColor Yellow
    }
    
    # 4. Check ImageAnalysisDecisions
    Write-Host "`n4. Decisions Made (ImageAnalysisDecisions):" -ForegroundColor Green
    $decisionsSql = "SELECT ContainerNumber, ScannerType, Decision, GroupIdentifier, ReviewedBy, CreatedAt FROM ImageAnalysisDecisions WITH (NOLOCK) WHERE GroupIdentifier = '$($GroupIdentifier.Replace("'", "''"))' OR GroupIdentifier LIKE '$($GroupIdentifier.Replace("'", "''"))%' ORDER BY ContainerNumber, ScannerType;"
    $decisionsOutput = & $sqlcmdPath -S "$serverName,$port" -d $database -E -Q $decisionsSql -W -s "," -h -1 2>&1
    
    if ($decisionsOutput -match "rows affected" -or $decisionsOutput.Count -eq 0) {
        Write-Host "   ⚠️ No ImageAnalysisDecisions found" -ForegroundColor Yellow
        $decidedContainers = @()
    } else {
        Write-Host "   ✅ Found Decision(s):" -ForegroundColor Green
        $decisionsOutput | ForEach-Object { 
            if ($_ -match "^[A-Z]") {
                Write-Host "      $_" -ForegroundColor White
            }
        }
        $decidedContainers = $decisionsOutput | Where-Object { 
            $_ -match "^[A-Z]" 
        } | ForEach-Object { 
            $parts = $_ -split ","
            if ($parts.Count -ge 3 -and $parts[2].Trim() -notmatch "^(NULL|Pending|)$") { 
                $parts[0].Trim() 
            }
        } | Where-Object { -not [string]::IsNullOrEmpty($_) } | Select-Object -Unique
    }
    
    # 5. Check AnalysisAssignments
    Write-Host "`n5. Current Assignment:" -ForegroundColor Green
    $assignmentSql = "SELECT aa.AssignedTo, aa.LeaseUntilUtc, ag.Status FROM AnalysisAssignments aa INNER JOIN AnalysisGroups ag ON aa.GroupId = ag.Id WHERE ag.GroupIdentifier = '$($GroupIdentifier.Replace("'", "''"))' OR ag.GroupIdentifier LIKE '$($GroupIdentifier.Replace("'", "''"))%' AND aa.LeaseUntilUtc > GETUTCDATE();"
    $assignmentOutput = & $sqlcmdPath -S "$serverName,$port" -d $database -E -Q $assignmentSql -W -s "," -h -1 2>&1
    
    if ($assignmentOutput -match "rows affected" -or $assignmentOutput.Count -eq 0) {
        Write-Host "   ⚠️ No active assignment found" -ForegroundColor Yellow
    } else {
        Write-Host "   ✅ Found Assignment:" -ForegroundColor Green
        $assignmentOutput | ForEach-Object { 
            if ($_ -match "^[a-zA-Z]") {
                Write-Host "      $_" -ForegroundColor White
            }
        }
    }
    
    # 6. Analysis
    Write-Host "`n=== ANALYSIS ===" -ForegroundColor Cyan
    Write-Host "Summary:" -ForegroundColor Yellow
    Write-Host "  Total containers in group: $($containers.Count)" -ForegroundColor White
    Write-Host "  Containers WITH images: $($containersWithImages.Count)" -ForegroundColor $(if ($containersWithImages.Count -gt 0) { "Green" } else { "Red" })
    Write-Host "  Containers WITHOUT images: $($containersWithoutImages.Count)" -ForegroundColor $(if ($containersWithoutImages.Count -gt 0) { "Yellow" } else { "Green" })
    Write-Host "  Containers with decisions: $($decidedContainers.Count)" -ForegroundColor White
    
    Write-Host "`nIssue Analysis:" -ForegroundColor Yellow
    if ($containers.Count -eq 0) {
        Write-Host "  ❌ No containers found for this GroupIdentifier" -ForegroundColor Red
        Write-Host "  ⚠️ Record may not have been processed by ContainerCompletenessService" -ForegroundColor Yellow
    } elseif ($containersWithImages.Count -eq 0 -and $containersWithoutImages.Count -gt 0) {
        Write-Host "  ⚠️ ALL containers have NO images" -ForegroundColor Yellow
        Write-Host "  ✅ Should be marked as PartiallyCompleted automatically when decision is saved" -ForegroundColor Green
        Write-Host "  ❌ But record is stuck - need to check why auto-progression didn't work" -ForegroundColor Red
        Write-Host "`n  Possible causes:" -ForegroundColor Yellow
        Write-Host "    1. AnalysisGroup doesn't exist (IntakeWorker hasn't run)" -ForegroundColor White
        Write-Host "    2. Decision hasn't been saved yet (no trigger for auto-progression)" -ForegroundColor White
        Write-Host "    3. Auto-progression logic has a bug (GroupIdentifier mismatch, scanner type issue)" -ForegroundColor White
    } elseif ($containersWithImages.Count -gt 0 -and $decidedContainers.Count -ge $containersWithImages.Count) {
        Write-Host "  ✅ All containers WITH images have decisions" -ForegroundColor Green
        Write-Host "  ✅ Should progress to AnalystCompleted or PartiallyCompleted" -ForegroundColor Green
        Write-Host "  ❌ But record is stuck - need to check why status didn't update" -ForegroundColor Red
    } elseif ($containersWithImages.Count -gt 0 -and $decidedContainers.Count -lt $containersWithImages.Count) {
        Write-Host "  ⚠️ Not all containers with images have decisions yet" -ForegroundColor Yellow
        $undecided = $containersWithImages | Where-Object { $decidedContainers -notcontains $_ }
        Write-Host "  Undecided containers: $($undecided -join ', ')" -ForegroundColor Yellow
    } else {
        Write-Host "  ⚠️ Unclear state - need manual review" -ForegroundColor Yellow
    }
    
} catch {
    Write-Host "`n❌ Error: $_" -ForegroundColor Red
    Write-Host "Stack trace: $($_.ScriptStackTrace)" -ForegroundColor Red
}

Write-Host "`n=== DONE ===" -ForegroundColor Cyan


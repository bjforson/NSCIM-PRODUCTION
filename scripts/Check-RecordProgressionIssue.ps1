# Check why records with missing images are not progressing
# Usage: .\scripts\Check-RecordProgressionIssue.ps1 -GroupIdentifier "40925581989"

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

Write-Host "`n=== Checking Record Progression Issue ===" -ForegroundColor Cyan
Write-Host "GroupIdentifier: $GroupIdentifier" -ForegroundColor Yellow
Write-Host "Server: $serverName`:$port" -ForegroundColor Gray
Write-Host "Database: $database`n" -ForegroundColor Gray

try {
    
    # 1. Check AnalysisGroup
    Write-Host "1. AnalysisGroup Status:" -ForegroundColor Green
    $groupSql = "SELECT Id, GroupIdentifier, ScannerType, Status, CreatedAtUtc, TotalContainerCount, SubmittedContainerCount, PendingContainerCount, PartiallyCompletedDate FROM AnalysisGroups WITH (NOLOCK) WHERE GroupIdentifier = '$($GroupIdentifier.Replace("'", "''"))';"
    $groupOutput = & $sqlcmdPath -S "$serverName,$port" -d $database -E -Q $groupSql -W -s "," -h -1 2>&1
    
    if ($groupOutput -match "rows affected" -or $groupOutput.Count -eq 0) {
        Write-Host "   ❌ No AnalysisGroup found with GroupIdentifier: $GroupIdentifier" -ForegroundColor Red
        exit 1
    } else {
        Write-Host "   ✅ Found AnalysisGroup:" -ForegroundColor Green
        $groupOutput | ForEach-Object { Write-Host "      $_" -ForegroundColor White }
    }
    
    # 2. Check AnalysisRecords (containers in group)
    Write-Host "`n2. Containers in Group (AnalysisRecords):" -ForegroundColor Green
    $recordsSql = "SELECT ContainerNumber FROM AnalysisRecords WITH (NOLOCK) WHERE GroupId IN (SELECT Id FROM AnalysisGroups WHERE GroupIdentifier = '$($GroupIdentifier.Replace("'", "''"))') ORDER BY ContainerNumber;"
    $recordsOutput = & $sqlcmdPath -S "$serverName,$port" -d $database -E -Q $recordsSql -W -s "," -h -1 2>&1
    
    if ($recordsOutput -match "rows affected" -or $recordsOutput.Count -eq 0) {
        Write-Host "   ❌ No AnalysisRecords found" -ForegroundColor Red
        $containers = @()
    } else {
        Write-Host "   ✅ Found Container(s):" -ForegroundColor Green
        $recordsOutput | ForEach-Object { Write-Host "      $_" -ForegroundColor White }
        $containers = $recordsOutput | Where-Object { $_ -match "^[A-Z]" } | ForEach-Object { $_.Trim() } | Where-Object { -not [string]::IsNullOrEmpty($_) }
    }
    
    # 3. Check ContainerCompletenessStatus (image availability)
    Write-Host "`n3. Image Availability (ContainerCompletenessStatus):" -ForegroundColor Green
    if ($containers.Count -eq 0) {
        Write-Host "   ⚠️ No containers to check" -ForegroundColor Yellow
        $containersWithImages = @()
        $containersWithoutImages = @()
    } else {
        $containerList = "'" + ($containers -join "','") + "'"
        $completenessSql = "SELECT ContainerNumber, HasImageData, WorkflowStage FROM ContainerCompletenessStatuses WITH (NOLOCK) WHERE ContainerNumber IN ($containerList) ORDER BY ContainerNumber;"
        $completenessOutput = & $sqlcmdPath -S "$serverName,$port" -d $database -E -Q $completenessSql -W -s "," -h -1 2>&1
        
        if ($completenessOutput -match "rows affected" -or $completenessOutput.Count -eq 0) {
            Write-Host "   ❌ No ContainerCompletenessStatus found" -ForegroundColor Red
            $containersWithImages = @()
            $containersWithoutImages = $containers
        } else {
            Write-Host "   ✅ Image Status:" -ForegroundColor Green
            $completenessOutput | ForEach-Object { 
                if ($_ -match "^[A-Z]") {
                    $parts = $_ -split ","
                    if ($parts.Count -ge 2) {
                        $hasImage = $parts[1].Trim() -eq "True"
                        $stage = if ($parts.Count -ge 3) { $parts[2].Trim() } else { "Unknown" }
                        $icon = if ($hasImage) { "✅" } else { "❌" }
                        $color = if ($hasImage) { "Green" } else { "Red" }
                        Write-Host "      $icon $($parts[0].Trim()) - HasImage: $($parts[1].Trim()), Stage: $stage" -ForegroundColor $color
                    }
                }
            }
            $containersWithImages = $completenessOutput | Where-Object { $_ -match "^[A-Z]" } | ForEach-Object { 
                $parts = $_ -split ","
                if ($parts.Count -ge 2 -and $parts[1].Trim() -eq "True") { $parts[0].Trim() }
            } | Where-Object { -not [string]::IsNullOrEmpty($_) }
            $containersWithoutImages = $containers | Where-Object { $containersWithImages -notcontains $_ }
        }
    }
    
    # 4. Check ImageAnalysisDecisions
    Write-Host "`n4. Decisions Made (ImageAnalysisDecisions):" -ForegroundColor Green
    $decisionsSql = "SELECT ContainerNumber, ScannerType, Decision FROM ImageAnalysisDecisions WITH (NOLOCK) WHERE DecisionGroupIdentifier = '$($GroupIdentifier.Replace("'", "''"))' OR DecisionGroupIdentifier LIKE '$($GroupIdentifier.Replace("'", "''"))%' ORDER BY ContainerNumber, ScannerType;"
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
    
    # 5. Analysis
    Write-Host "`n=== ANALYSIS ===" -ForegroundColor Cyan
    Write-Host "Summary:" -ForegroundColor Yellow
    Write-Host "  Total containers in group: $($containers.Count)" -ForegroundColor White
    Write-Host "  Containers WITH images: $($containersWithImages.Count)" -ForegroundColor $(if ($containersWithImages.Count -gt 0) { "Green" } else { "Red" })
    Write-Host "  Containers WITHOUT images: $($containersWithoutImages.Count)" -ForegroundColor $(if ($containersWithoutImages.Count -gt 0) { "Yellow" } else { "Green" })
    Write-Host "  Containers with decisions: $($decidedContainers.Count)" -ForegroundColor White
    
    Write-Host "`nIssue Analysis:" -ForegroundColor Yellow
    if ($containersWithImages.Count -eq 0 -and $containersWithoutImages.Count -gt 0) {
        Write-Host "  ⚠️ ALL containers have NO images" -ForegroundColor Yellow
        Write-Host "  ✅ Should be marked as PartiallyCompleted automatically" -ForegroundColor Green
        Write-Host "  ❌ But record is stuck - need to check why auto-progression didn't work" -ForegroundColor Red
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

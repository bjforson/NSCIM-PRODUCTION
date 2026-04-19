# Find the actual GroupIdentifier for a container number
# Usage: .\scripts\Find-RecordGroupIdentifier.ps1 -ContainerNumber "40925581989"

param(
    [Parameter(Mandatory=$true)]
    [string]$ContainerNumber
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

Write-Host "`n=== Finding GroupIdentifier for Container ===" -ForegroundColor Cyan
Write-Host "ContainerNumber: $ContainerNumber" -ForegroundColor Yellow
Write-Host "Server: $serverName`:$port" -ForegroundColor Gray
Write-Host "Database: $database`n" -ForegroundColor Gray

try {
    
    # Check ContainerCompletenessStatus
    Write-Host "1. ContainerCompletenessStatus:" -ForegroundColor Green
    $ccsSql = "SELECT ContainerNumber, GroupIdentifier, ScannerType, HasImageData, WorkflowStage FROM ContainerCompletenessStatuses WITH (NOLOCK) WHERE ContainerNumber = '$($ContainerNumber.Replace("'", "''"))';"
    $ccsOutput = & $sqlcmdPath -S "$serverName,$port" -d $database -E -Q $ccsSql -W -s "," -h -1 2>&1
    
    if ($ccsOutput -match "rows affected" -or $ccsOutput.Count -eq 0) {
        Write-Host "   ❌ Container not found in ContainerCompletenessStatuses" -ForegroundColor Red
    } else {
        Write-Host "   ✅ Found:" -ForegroundColor Green
        $ccsOutput | ForEach-Object { 
            if ($_ -match "^[A-Z]") {
                Write-Host "      $_" -ForegroundColor White
                $parts = $_ -split ","
                if ($parts.Count -ge 2) {
                    $groupId = $parts[1].Trim()
                    Write-Host "`n   GroupIdentifier: $groupId" -ForegroundColor Cyan
                    
                    # Now check AnalysisGroup
                    Write-Host "`n2. AnalysisGroup:" -ForegroundColor Green
                    $groupSql = "SELECT Id, GroupIdentifier, ScannerType, Status, TotalContainerCount, SubmittedContainerCount, PendingContainerCount, PartiallyCompletedDate FROM AnalysisGroups WITH (NOLOCK) WHERE GroupIdentifier = '$($groupId.Replace("'", "''"))';"
                    $groupOutput = & $sqlcmdPath -S "$serverName,$port" -d $database -E -Q $groupSql -W -s "," -h -1 2>&1
                    
                    if ($groupOutput -match "rows affected" -or $groupOutput.Count -eq 0) {
                        Write-Host "   ❌ No AnalysisGroup found with GroupIdentifier: $groupId" -ForegroundColor Red
                        Write-Host "   ⚠️ Container exists but group not created yet" -ForegroundColor Yellow
                    } else {
                        Write-Host "   ✅ Found AnalysisGroup:" -ForegroundColor Green
                        $groupOutput | ForEach-Object { Write-Host "      $_" -ForegroundColor White }
                    }
                }
            }
        }
    }
    
    # Also check AnalysisRecords
    Write-Host "`n3. AnalysisRecords:" -ForegroundColor Green
    $recordsSql = "SELECT ar.ContainerNumber, ag.GroupIdentifier, ag.Status FROM AnalysisRecords ar INNER JOIN AnalysisGroups ag ON ar.GroupId = ag.Id WHERE ar.ContainerNumber = '$($ContainerNumber.Replace("'", "''"))';"
    $recordsOutput = & $sqlcmdPath -S "$serverName,$port" -d $database -E -Q $recordsSql -W -s "," -h -1 2>&1
    
    if ($recordsOutput -match "rows affected" -or $recordsOutput.Count -eq 0) {
        Write-Host "   ⚠️ No AnalysisRecord found" -ForegroundColor Yellow
    } else {
        Write-Host "   ✅ Found:" -ForegroundColor Green
        $recordsOutput | ForEach-Object { Write-Host "      $_" -ForegroundColor White }
    }
    
} catch {
    Write-Host "`n❌ Error: $_" -ForegroundColor Red
}

Write-Host "`n=== DONE ===" -ForegroundColor Cyan


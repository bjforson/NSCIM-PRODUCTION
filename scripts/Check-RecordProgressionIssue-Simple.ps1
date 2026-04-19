# Check why records with missing images are not progressing
# Usage: .\scripts\Check-RecordProgressionIssue-Simple.ps1 -GroupIdentifier "40925581989"

param(
    [Parameter(Mandatory=$true)]
    [string]$GroupIdentifier
)

$ErrorActionPreference = "Stop"

# Get connection string from appsettings.json
$appsettingsPath = "src\NickScanCentralImagingPortal.API\appsettings.json"
if (-not (Test-Path $appsettingsPath)) {
    Write-Host "❌ appsettings.json not found at: $appsettingsPath" -ForegroundColor Red
    exit 1
}

$appsettings = Get-Content $appsettingsPath | ConvertFrom-Json
$connectionString = $appsettings.ConnectionStrings.DefaultConnection

if ([string]::IsNullOrWhiteSpace($connectionString)) {
    Write-Host "❌ Connection string not found in appsettings.json" -ForegroundColor Red
    exit 1
}

Write-Host "`n=== CHECKING RECORD PROGRESSION ISSUE ===" -ForegroundColor Green
Write-Host "GroupIdentifier: $GroupIdentifier" -ForegroundColor Cyan

# Parse connection string to get server and database
if ($connectionString -match "Server=([^;]+)") {
    $server = $matches[1]
} else {
    Write-Host "❌ Could not parse server from connection string" -ForegroundColor Red
    exit 1
}

if ($connectionString -match "Database=([^;]+)") {
    $database = $matches[1]
} else {
    Write-Host "❌ Could not parse database from connection string" -ForegroundColor Red
    exit 1
}

# Build SQL queries using here-strings
$sqlFile = [System.IO.Path]::GetTempFileName() + ".sql"

$sqlContent = @"
-- Check AnalysisGroup status
SELECT 
    ag.Id,
    ag.GroupIdentifier,
    ag.ScannerType,
    ag.Status,
    ag.TotalContainerCount,
    ag.SubmittedContainerCount,
    ag.PendingContainerCount,
    ag.PartiallyCompletedDate,
    ag.CreatedAtUtc,
    ag.UpdatedAtUtc
FROM AnalysisGroups ag
WHERE ag.GroupIdentifier = '$GroupIdentifier';

-- Check AnalysisRecords (containers in the group)
SELECT 
    ar.ContainerNumber
FROM AnalysisRecords ar
INNER JOIN AnalysisGroups ag ON ar.GroupId = ag.Id
WHERE ag.GroupIdentifier = '$GroupIdentifier'
ORDER BY ar.ContainerNumber;

-- Check ContainerCompletenessStatus (image availability)
SELECT 
    ccs.ContainerNumber,
    ccs.ScannerType,
    ccs.HasImageData,
    ccs.WorkflowStage
FROM ContainerCompletenessStatuses ccs
WHERE ccs.GroupIdentifier = '$GroupIdentifier'
ORDER BY ccs.ContainerNumber;

-- Check ImageAnalysisDecisions (decisions made)
SELECT 
    iad.ContainerNumber,
    iad.ScannerType,
    iad.Decision
FROM ImageAnalysisDecisions iad
WHERE iad.DecisionGroupIdentifier = '$GroupIdentifier' OR iad.DecisionGroupIdentifier LIKE '$GroupIdentifier%'
ORDER BY iad.ContainerNumber, iad.ScannerType;
"@

$sqlContent | Out-File -FilePath $sqlFile -Encoding UTF8

try {
    Write-Host "`n📊 AnalysisGroup Status:" -ForegroundColor Yellow
    $result1 = sqlcmd.exe -S $server -d $database -i $sqlFile -h -1 -W -s "|" 2>&1
    $lines = $result1 | Where-Object { $_ -match "^\w+\|" -or $_ -match "^Id\|" }
    if ($lines) {
        $lines | ForEach-Object { Write-Host "  $_" -ForegroundColor White }
    } else {
        Write-Host "  ❌ No AnalysisGroup found" -ForegroundColor Red
    }
    
    Write-Host "`n📦 Containers in Group:" -ForegroundColor Yellow
    $containersQuery = "SELECT ar.ContainerNumber FROM AnalysisRecords ar INNER JOIN AnalysisGroups ag ON ar.GroupId = ag.Id WHERE ag.GroupIdentifier = '$GroupIdentifier' ORDER BY ar.ContainerNumber"
    $containersFile = [System.IO.Path]::GetTempFileName() + ".sql"
    $containersQuery | Out-File -FilePath $containersFile -Encoding UTF8
    $result2 = sqlcmd.exe -S $server -d $database -i $containersFile -h -1 -W 2>&1
    $containers = $result2 | Where-Object { $_ -match "^[A-Z]" } | Select-Object -Skip 1
    if ($containers) {
        Write-Host "  Total containers: $($containers.Count)" -ForegroundColor White
        $containers | ForEach-Object { Write-Host "    - $_" -ForegroundColor Gray }
    } else {
        Write-Host "  ❌ No containers found" -ForegroundColor Red
    }
    Remove-Item $containersFile -Force -ErrorAction SilentlyContinue
    
    Write-Host "`n🖼️ Image Availability:" -ForegroundColor Yellow
    $imageQuery = "SELECT ccs.ContainerNumber, ccs.HasImageData, ccs.WorkflowStage FROM ContainerCompletenessStatuses ccs WHERE ccs.GroupIdentifier = '$GroupIdentifier' ORDER BY ccs.ContainerNumber"
    $imageFile = [System.IO.Path]::GetTempFileName() + ".sql"
    $imageQuery | Out-File -FilePath $imageFile -Encoding UTF8
    $result3 = sqlcmd.exe -S $server -d $database -i $imageFile -h -1 -W -s "|" 2>&1
    $imageStatus = $result3 | Where-Object { $_ -match "^[A-Z]" } | Select-Object -Skip 1
    if ($imageStatus) {
        $withImages = ($imageStatus | Where-Object { $_ -match "True" }).Count
        $withoutImages = ($imageStatus | Where-Object { $_ -match "False" }).Count
        Write-Host "  Containers WITH images: $withImages" -ForegroundColor Green
        Write-Host "  Containers WITHOUT images: $withoutImages" -ForegroundColor Red
        $imageStatus | ForEach-Object { 
            $parts = $_ -split "\|"
            if ($parts.Count -ge 3) {
                $hasImage = if ($parts[1] -eq "True") { "✅" } else { "❌" }
                Write-Host "    $hasImage $($parts[0]) - Stage: $($parts[2])" -ForegroundColor $(if ($parts[1] -eq "True") { "Green" } else { "Red" })
            }
        }
    } else {
        Write-Host "  ❌ No ContainerCompletenessStatus found" -ForegroundColor Red
    }
    Remove-Item $imageFile -Force -ErrorAction SilentlyContinue
    
    Write-Host "`n✅ Decisions Made:" -ForegroundColor Yellow
    $decisionsQuery = "SELECT iad.ContainerNumber, iad.ScannerType, iad.Decision FROM ImageAnalysisDecisions iad WHERE iad.DecisionGroupIdentifier = '$GroupIdentifier' OR iad.DecisionGroupIdentifier LIKE '$GroupIdentifier%' ORDER BY iad.ContainerNumber"
    $decisionsFile = [System.IO.Path]::GetTempFileName() + ".sql"
    $decisionsQuery | Out-File -FilePath $decisionsFile -Encoding UTF8
    $result4 = sqlcmd.exe -S $server -d $database -i $decisionsFile -h -1 -W -s "|" 2>&1
    $decisions = $result4 | Where-Object { $_ -match "^[A-Z]" } | Select-Object -Skip 1
    if ($decisions) {
        Write-Host "  Total decisions: $($decisions.Count)" -ForegroundColor White
        $decisions | ForEach-Object { 
            $parts = $_ -split "\|"
            if ($parts.Count -ge 3) {
                Write-Host "    - $($parts[0]) ($($parts[1])): $($parts[2])" -ForegroundColor Gray
            }
        }
    } else {
        Write-Host "  ⚠️ No decisions found" -ForegroundColor Yellow
    }
    Remove-Item $decisionsFile -Force -ErrorAction SilentlyContinue
    
    Write-Host "`n=== ANALYSIS ===" -ForegroundColor Green
    Write-Host "Checking why record is not progressing..." -ForegroundColor Yellow
    
    # Check counts
    $countQuery1 = "SELECT COUNT(*) FROM ContainerCompletenessStatuses WHERE GroupIdentifier = '$GroupIdentifier' AND HasImageData = 1"
    $countFile1 = [System.IO.Path]::GetTempFileName() + ".sql"
    $countQuery1 | Out-File -FilePath $countFile1 -Encoding UTF8
    $containersWithImages = (sqlcmd.exe -S $server -d $database -i $countFile1 -h -1 -W 2>&1 | Select-Object -Last 1).Trim()
    Remove-Item $countFile1 -Force -ErrorAction SilentlyContinue
    
    $countQuery2 = "SELECT COUNT(DISTINCT ContainerNumber) FROM ImageAnalysisDecisions WHERE DecisionGroupIdentifier = '$GroupIdentifier' OR DecisionGroupIdentifier LIKE '$GroupIdentifier%'"
    $countFile2 = [System.IO.Path]::GetTempFileName() + ".sql"
    $countQuery2 | Out-File -FilePath $countFile2 -Encoding UTF8
    $decisionsCount = (sqlcmd.exe -S $server -d $database -i $countFile2 -h -1 -W 2>&1 | Select-Object -Last 1).Trim()
    Remove-Item $countFile2 -Force -ErrorAction SilentlyContinue
    
    Write-Host "  Containers with images: $containersWithImages" -ForegroundColor White
    Write-Host "  Containers with decisions: $decisionsCount" -ForegroundColor White
    
    if ([int]$containersWithImages -gt 0 -and [int]$decisionsCount -ge [int]$containersWithImages) {
        Write-Host "  ✅ All containers with images have decisions" -ForegroundColor Green
        Write-Host "  ⚠️ Record should progress to AnalystCompleted or PartiallyCompleted" -ForegroundColor Yellow
    } elseif ([int]$containersWithImages -eq 0) {
        Write-Host "  ⚠️ No containers have images - should be marked as PartiallyCompleted" -ForegroundColor Yellow
    } else {
        Write-Host "  ⚠️ Not all containers with images have decisions yet" -ForegroundColor Yellow
    }
    
} catch {
    Write-Host "`n❌ Error: $_" -ForegroundColor Red
} finally {
    if (Test-Path $sqlFile) {
        Remove-Item $sqlFile -Force -ErrorAction SilentlyContinue
    }
}

Write-Host "`n=== DONE ===" -ForegroundColor Green


# Get detailed information about stuck groups

param(
    [string]$ConnectionString = ""
)

# Load connection string
if ([string]::IsNullOrEmpty($ConnectionString)) {
    $apiPath = "src\NickScanCentralImagingPortal.API"
    $appsettingsPath = Join-Path $apiPath "appsettings.json"
    $appsettingsDevPath = Join-Path $apiPath "appsettings.Development.json"
    
    if (Test-Path $appsettingsPath) {
        $appsettings = Get-Content $appsettingsPath | ConvertFrom-Json
        $ConnectionString = $appsettings.ConnectionStrings.NS_CIS_Connection
        
        if (Test-Path $appsettingsDevPath) {
            $appsettingsDev = Get-Content $appsettingsDevPath | ConvertFrom-Json
            if ($appsettingsDev.ConnectionStrings.NS_CIS_Connection) {
                $ConnectionString = $appsettingsDev.ConnectionStrings.NS_CIS_Connection
            }
        }
    }
}

if ([string]::IsNullOrEmpty($ConnectionString)) {
    Write-Host "ERROR: Connection string not found" -ForegroundColor Red
    exit 1
}

try {
    $connection = New-Object System.Data.SqlClient.SqlConnection($ConnectionString)
    $connection.Open()
    
    Write-Host "================================================" -ForegroundColor Cyan
    Write-Host "Detailed Analysis of Stuck Groups" -ForegroundColor Cyan
    Write-Host "================================================" -ForegroundColor Cyan
    Write-Host ""
    
    # Get detailed info for each stuck group
    $query = @'
SELECT 
    g.Id AS GroupId,
    g.GroupIdentifier,
    g.Status AS GroupStatus,
    g.CreatedAtUtc,
    g.UpdatedAtUtc,
    COUNT(DISTINCT r.ContainerNumber) AS TotalContainers,
    COUNT(DISTINCT CASE WHEN d.Decision IN ('Normal', 'Abnormal') THEN d.ContainerNumber END) AS DecidedContainers,
    COUNT(DISTINCT CASE WHEN d.GroupIdentifier IS NULL OR d.GroupIdentifier = '' THEN d.ContainerNumber END) AS DecisionsWithoutGroupId,
    COUNT(DISTINCT CASE WHEN d.GroupIdentifier IS NOT NULL AND d.GroupIdentifier != g.GroupIdentifier THEN d.ContainerNumber END) AS DecisionsWithMismatchedGroupId
FROM AnalysisGroups g
INNER JOIN AnalysisRecords r ON g.Id = r.GroupId
LEFT JOIN ImageAnalysisDecisions d ON d.ContainerNumber = r.ContainerNumber
    AND d.ScannerType = COALESCE(r.ScannerType, g.ScannerType, '')
WHERE g.Status IN ('AnalystAssigned', 'Ready')
GROUP BY g.Id, g.GroupIdentifier, g.Status, g.CreatedAtUtc, g.UpdatedAtUtc
HAVING COUNT(DISTINCT r.ContainerNumber) > 0
    AND COUNT(DISTINCT r.ContainerNumber) = COUNT(DISTINCT CASE WHEN d.Decision IN ('Normal', 'Abnormal') THEN d.ContainerNumber END)
ORDER BY g.UpdatedAtUtc DESC;
'@
    
    $command = New-Object System.Data.SqlClient.SqlCommand($query, $connection)
    $adapter = New-Object System.Data.SqlClient.SqlDataAdapter($command)
    $dataset = New-Object System.Data.DataSet
    $adapter.Fill($dataset) | Out-Null
    
    $count = $dataset.Tables[0].Rows.Count
    if ($count -gt 0) {
        foreach ($row in $dataset.Tables[0].Rows) {
            Write-Host "Group Details" -ForegroundColor Yellow
            Write-Host "  GroupId: $($row['GroupId'])" -ForegroundColor White
            Write-Host "  GroupIdentifier: $($row['GroupIdentifier'])" -ForegroundColor White
            Write-Host "  Status: $($row['GroupStatus'])" -ForegroundColor $(if ($row['GroupStatus'] -eq 'Ready') { "Red" } else { "Yellow" })
            Write-Host "  Total Containers: $($row['TotalContainers'])" -ForegroundColor White
            Write-Host "  Decided Containers: $($row['DecidedContainers'])" -ForegroundColor White
            Write-Host "  Decisions Without GroupId: $($row['DecisionsWithoutGroupId'])" -ForegroundColor $(if ([int]$row['DecisionsWithoutGroupId'] -gt 0) { "Red" } else { "Green" })
            Write-Host "  Decisions With Mismatched GroupId: $($row['DecisionsWithMismatchedGroupId'])" -ForegroundColor $(if ([int]$row['DecisionsWithMismatchedGroupId'] -gt 0) { "Red" } else { "Green" })
            Write-Host ""
        }
    }
    
    Write-Host "================================================" -ForegroundColor Cyan
    Write-Host "Total stuck groups found: $count" -ForegroundColor $(if ($count -gt 0) { "Red" } else { "Green" })
    Write-Host "================================================" -ForegroundColor Cyan
    
    $connection.Close()
}
catch {
    $errorMsg = $_.Exception.Message
    Write-Host "ERROR: $errorMsg" -ForegroundColor Red
    exit 1
}


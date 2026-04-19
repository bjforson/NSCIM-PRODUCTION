# Find ALL groups that might be stuck (regardless of status)
# This checks all groups with decisions to find any that should have moved

param(
    [string]$ConnectionString = ""
)

Write-Host "================================================" -ForegroundColor Cyan
Write-Host "Finding ALL Potentially Stuck Groups" -ForegroundColor Cyan
Write-Host "================================================" -ForegroundColor Cyan
Write-Host ""

# Load connection string from appsettings if not provided
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
    
    Write-Host "OK: Connected to database" -ForegroundColor Green
    Write-Host ""
    
    # Find groups where all containers have decisions but status is NOT AnalystCompleted or higher
    $query = @'
SELECT 
    g.Id AS GroupId,
    g.GroupIdentifier,
    g.Status AS GroupStatus,
    COUNT(DISTINCT r.ContainerNumber) AS TotalContainers,
    COUNT(DISTINCT CASE WHEN d.Decision IN ('Normal', 'Abnormal') THEN d.ContainerNumber END) AS DecidedContainers,
    COUNT(DISTINCT CASE WHEN d.GroupIdentifier IS NULL OR d.GroupIdentifier = '' THEN d.ContainerNumber END) AS DecisionsWithoutGroupId,
    COUNT(DISTINCT CASE WHEN d.GroupIdentifier IS NOT NULL AND d.GroupIdentifier != g.GroupIdentifier THEN d.ContainerNumber END) AS DecisionsWithMismatchedGroupId
FROM AnalysisGroups g
INNER JOIN AnalysisRecords r ON g.Id = r.GroupId
LEFT JOIN ImageAnalysisDecisions d ON d.ContainerNumber = r.ContainerNumber
    AND d.ScannerType = COALESCE(r.ScannerType, g.ScannerType, '')
WHERE g.Status IN ('AnalystAssigned', 'Ready')
GROUP BY g.Id, g.GroupIdentifier, g.Status
HAVING COUNT(DISTINCT r.ContainerNumber) > 0
    AND COUNT(DISTINCT r.ContainerNumber) = COUNT(DISTINCT CASE WHEN d.Decision IN ('Normal', 'Abnormal') THEN d.ContainerNumber END)
ORDER BY g.Id DESC;
'@
    
    $command = New-Object System.Data.SqlClient.SqlCommand($query, $connection)
    $adapter = New-Object System.Data.SqlClient.SqlDataAdapter($command)
    $dataset = New-Object System.Data.DataSet
    $adapter.Fill($dataset) | Out-Null
    
    if ($dataset.Tables[0].Rows.Count -eq 0) {
        Write-Host "OK: No stuck groups found" -ForegroundColor Green
    } else {
        Write-Host "WARNING: Found $($dataset.Tables[0].Rows.Count) potentially stuck groups:" -ForegroundColor Red
        $dataset.Tables[0] | Format-Table -AutoSize
    }
    
    $connection.Close()
}
catch {
    $errorMsg = $_.Exception.Message
    Write-Host "ERROR: $errorMsg" -ForegroundColor Red
    exit 1
}


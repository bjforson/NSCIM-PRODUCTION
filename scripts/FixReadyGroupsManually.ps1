# Manually fix Ready groups that have all containers decided

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
    Write-Host "Fixing Ready Groups with All Containers Decided" -ForegroundColor Cyan
    Write-Host "================================================" -ForegroundColor Cyan
    Write-Host ""
    
    # Find Ready groups where all containers have decisions
    $query = @'
UPDATE g
SET g.Status = 'AnalystCompleted',
    g.UpdatedAtUtc = SYSUTCDATETIME()
FROM AnalysisGroups g
INNER JOIN (
    SELECT 
        g.Id AS GroupId,
        COUNT(DISTINCT r.ContainerNumber) AS TotalContainers,
        COUNT(DISTINCT CASE WHEN d.Decision IN ('Normal', 'Abnormal') THEN d.ContainerNumber END) AS DecidedContainers
    FROM AnalysisGroups g
    INNER JOIN AnalysisRecords r ON g.Id = r.GroupId
    LEFT JOIN ImageAnalysisDecisions d ON d.ContainerNumber = r.ContainerNumber
        AND d.ScannerType = COALESCE(r.ScannerType, g.ScannerType, '')
    WHERE g.Status = 'Ready'
    GROUP BY g.Id
    HAVING COUNT(DISTINCT r.ContainerNumber) > 0
        AND COUNT(DISTINCT r.ContainerNumber) = COUNT(DISTINCT CASE WHEN d.Decision IN ('Normal', 'Abnormal') THEN d.ContainerNumber END)
) AS matched ON g.Id = matched.GroupId;
'@
    
    $command = New-Object System.Data.SqlClient.SqlCommand($query, $connection)
    $affected = $command.ExecuteNonQuery()
    
    Write-Host "Groups updated: $affected" -ForegroundColor $(if ($affected -gt 0) { "Green" } else { "Yellow" })
    Write-Host ""
    
    # Also update WorkflowStage
    $query2 = @'
UPDATE ContainerCompletenessStatuses
SET WorkflowStage = 'Audit',
    UpdatedAt = SYSUTCDATETIME()
WHERE GroupIdentifier IN (
    SELECT GroupIdentifier
    FROM AnalysisGroups
    WHERE Status = 'AnalystCompleted'
        AND GroupIdentifier IS NOT NULL
)
AND WorkflowStage <> 'Audit';
'@
    
    $command2 = New-Object System.Data.SqlClient.SqlCommand($query2, $connection)
    $affected2 = $command2.ExecuteNonQuery()
    
    Write-Host "WorkflowStage records updated: $affected2" -ForegroundColor $(if ($affected2 -gt 0) { "Green" } else { "Yellow" })
    Write-Host ""
    
    $connection.Close()
    Write-Host "OK: Fix complete" -ForegroundColor Green
}
catch {
    $errorMsg = $_.Exception.Message
    Write-Host "ERROR: $errorMsg" -ForegroundColor Red
    exit 1
}


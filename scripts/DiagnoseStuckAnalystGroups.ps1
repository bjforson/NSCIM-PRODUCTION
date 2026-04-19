# Diagnostic Script: Find groups stuck in AnalystAssigned with all containers decided
# This helps identify why groups aren't moving to AnalystCompleted

param(
    [string]$ConnectionString = ""
)

Write-Host "================================================" -ForegroundColor Cyan
Write-Host "Diagnosing Stuck Analyst Groups" -ForegroundColor Cyan
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
    Write-Host "ERROR: Connection string not found. Please provide it as a parameter:" -ForegroundColor Red
    Write-Host "   .\DiagnoseStuckAnalystGroups.ps1 -ConnectionString 'Server=...;Database=...;...'" -ForegroundColor Yellow
    exit 1
}

Write-Host "Connecting to database..." -ForegroundColor Yellow
Write-Host ""

try {
    # Create SQL connection
    $connection = New-Object System.Data.SqlClient.SqlConnection($ConnectionString)
    $connection.Open()
    
    Write-Host "OK: Connected to database" -ForegroundColor Green
    Write-Host ""
    
    # Step 1: Find groups with Status = AnalystAssigned
    Write-Host "Step 1: Finding all groups with Status = AnalystAssigned" -ForegroundColor Cyan
    Write-Host "------------------------------------------------------------" -ForegroundColor Cyan
    
    $query1 = @'
SELECT 
    g.Id AS GroupId,
    g.GroupIdentifier,
    g.Status AS GroupStatus,
    g.CreatedAtUtc,
    g.UpdatedAtUtc,
    COUNT(DISTINCT r.ContainerNumber) AS TotalContainers,
    COUNT(DISTINCT d.ContainerNumber) AS DecidedContainers,
    COUNT(DISTINCT CASE WHEN d.Decision IN ('Normal', 'Abnormal') THEN d.ContainerNumber END) AS ValidDecisions
FROM AnalysisGroups g
LEFT JOIN AnalysisRecords r ON g.Id = r.GroupId
LEFT JOIN ImageAnalysisDecisions d ON d.GroupIdentifier = g.GroupIdentifier 
    AND d.ContainerNumber = r.ContainerNumber
    AND d.ScannerType = COALESCE(r.ScannerType, g.ScannerType)
WHERE g.Status = 'AnalystAssigned'
GROUP BY g.Id, g.GroupIdentifier, g.Status, g.CreatedAtUtc, g.UpdatedAtUtc
HAVING COUNT(DISTINCT r.ContainerNumber) > 0
ORDER BY g.UpdatedAtUtc DESC;
'@
    
    $command1 = New-Object System.Data.SqlClient.SqlCommand($query1, $connection)
    $adapter1 = New-Object System.Data.SqlClient.SqlDataAdapter($command1)
    $dataset1 = New-Object System.Data.DataSet
    $adapter1.Fill($dataset1) | Out-Null
    
    if ($dataset1.Tables[0].Rows.Count -eq 0) {
        Write-Host "OK: No groups found with Status = AnalystAssigned" -ForegroundColor Green
    } else {
        Write-Host "Found $($dataset1.Tables[0].Rows.Count) groups with Status = AnalystAssigned" -ForegroundColor Yellow
        $dataset1.Tables[0] | Format-Table -AutoSize
    }
    Write-Host ""
    
    # Step 2: Find groups where all containers have decisions but status is still AnalystAssigned
    Write-Host "Step 2: Finding groups where ALL containers are decided but status is still AnalystAssigned" -ForegroundColor Cyan
    Write-Host "------------------------------------------------------------------------------------------------" -ForegroundColor Cyan
    
    $query2 = @'
SELECT 
    g.Id AS GroupId,
    g.GroupIdentifier,
    g.Status AS GroupStatus,
    COUNT(DISTINCT r.ContainerNumber) AS TotalContainers,
    COUNT(DISTINCT d.ContainerNumber) AS DecidedContainers,
    CASE 
        WHEN COUNT(DISTINCT r.ContainerNumber) = COUNT(DISTINCT CASE WHEN d.Decision IN ('Normal', 'Abnormal') THEN d.ContainerNumber END)
        THEN 'ALL_DECIDED'
        ELSE 'MISSING_DECISIONS'
    END AS CompletionStatus,
    COUNT(DISTINCT CASE WHEN d.GroupIdentifier IS NULL OR d.GroupIdentifier = '' THEN d.ContainerNumber END) AS DecisionsWithoutGroupId,
    COUNT(DISTINCT CASE WHEN d.GroupIdentifier IS NOT NULL AND d.GroupIdentifier != g.GroupIdentifier THEN d.ContainerNumber END) AS DecisionsWithMismatchedGroupId
FROM AnalysisGroups g
INNER JOIN AnalysisRecords r ON g.Id = r.GroupId
LEFT JOIN ImageAnalysisDecisions d ON d.ContainerNumber = r.ContainerNumber
    AND d.ScannerType = COALESCE(r.ScannerType, g.ScannerType)
WHERE g.Status = 'AnalystAssigned'
GROUP BY g.Id, g.GroupIdentifier, g.Status
HAVING COUNT(DISTINCT r.ContainerNumber) = COUNT(DISTINCT CASE WHEN d.Decision IN ('Normal', 'Abnormal') THEN d.ContainerNumber END)
ORDER BY g.Id DESC;
'@
    
    $command2 = New-Object System.Data.SqlClient.SqlCommand($query2, $connection)
    $adapter2 = New-Object System.Data.SqlClient.SqlDataAdapter($command2)
    $dataset2 = New-Object System.Data.DataSet
    $adapter2.Fill($dataset2) | Out-Null
    
    if ($dataset2.Tables[0].Rows.Count -eq 0) {
        Write-Host "OK: No stuck groups found (all groups are correctly processed)" -ForegroundColor Green
    } else {
        Write-Host "WARNING: Found $($dataset2.Tables[0].Rows.Count) STUCK GROUPS:" -ForegroundColor Red
        $dataset2.Tables[0] | Format-Table -AutoSize
    }
    Write-Host ""
    
    # Step 3: Check for decisions with missing or mismatched GroupIdentifier
    Write-Host "Step 3: Checking for decisions with missing or mismatched GroupIdentifier" -ForegroundColor Cyan
    Write-Host "---------------------------------------------------------------------------" -ForegroundColor Cyan
    
    $query3 = @'
SELECT 
    d.Id AS DecisionId,
    d.ContainerNumber,
    d.ScannerType,
    d.Decision,
    d.GroupIdentifier AS DecisionGroupId,
    g.GroupIdentifier AS ActualGroupId,
    g.Status AS GroupStatus,
    CASE 
        WHEN d.GroupIdentifier IS NULL OR d.GroupIdentifier = '' THEN 'MISSING_GROUP_ID'
        WHEN d.GroupIdentifier != g.GroupIdentifier THEN 'MISMATCHED_GROUP_ID'
        ELSE 'OK'
    END AS Issue
FROM ImageAnalysisDecisions d
INNER JOIN AnalysisRecords r ON d.ContainerNumber = r.ContainerNumber 
    AND d.ScannerType = COALESCE(r.ScannerType, '')
INNER JOIN AnalysisGroups g ON r.GroupId = g.Id
WHERE g.Status = 'AnalystAssigned'
    AND d.Decision IN ('Normal', 'Abnormal')
    AND (d.GroupIdentifier IS NULL OR d.GroupIdentifier = '' OR d.GroupIdentifier != g.GroupIdentifier);
'@
    
    $command3 = New-Object System.Data.SqlClient.SqlCommand($query3, $connection)
    $adapter3 = New-Object System.Data.SqlClient.SqlDataAdapter($command3)
    $dataset3 = New-Object System.Data.DataSet
    $adapter3.Fill($dataset3) | Out-Null
    
    if ($dataset3.Tables[0].Rows.Count -eq 0) {
        Write-Host "OK: No decisions with missing or mismatched GroupIdentifier found" -ForegroundColor Green
    } else {
        Write-Host "WARNING: Found $($dataset3.Tables[0].Rows.Count) decisions with issues:" -ForegroundColor Yellow
        $dataset3.Tables[0] | Format-Table -AutoSize
    }
    Write-Host ""
    
    # Step 4: Check AnalysisAssignments for stuck groups
    Write-Host "Step 4: Checking AnalysisAssignments for stuck groups" -ForegroundColor Cyan
    Write-Host "------------------------------------------------------" -ForegroundColor Cyan
    
    $query4 = @'
SELECT 
    a.Id AS AssignmentId,
    a.GroupId,
    g.GroupIdentifier,
    a.AssignedTo,
    a.Role,
    a.State,
    a.LeaseUntilUtc,
    g.Status AS GroupStatus
FROM AnalysisAssignments a
INNER JOIN AnalysisGroups g ON a.GroupId = g.Id
WHERE g.Status = 'AnalystAssigned'
    AND a.Role = 'Analyst'
    AND a.State = 'Active'
ORDER BY a.LeaseUntilUtc DESC;
'@
    
    $command4 = New-Object System.Data.SqlClient.SqlCommand($query4, $connection)
    $adapter4 = New-Object System.Data.SqlClient.SqlDataAdapter($command4)
    $dataset4 = New-Object System.Data.DataSet
    $adapter4.Fill($dataset4) | Out-Null
    
    if ($dataset4.Tables[0].Rows.Count -eq 0) {
        Write-Host "OK: No active Analyst assignments found for stuck groups" -ForegroundColor Green
    } else {
        Write-Host "Found $($dataset4.Tables[0].Rows.Count) active Analyst assignments:" -ForegroundColor Yellow
        $dataset4.Tables[0] | Format-Table -AutoSize
    }
    Write-Host ""
    
    # Summary
    Write-Host "================================================" -ForegroundColor Cyan
    Write-Host "Summary" -ForegroundColor Cyan
    Write-Host "================================================" -ForegroundColor Cyan
    Write-Host "Total AnalystAssigned groups: $($dataset1.Tables[0].Rows.Count)" -ForegroundColor White
    Write-Host "Stuck groups (all decided): $($dataset2.Tables[0].Rows.Count)" -ForegroundColor $(if ($dataset2.Tables[0].Rows.Count -gt 0) { "Red" } else { "Green" })
    Write-Host "Decisions with GroupIdentifier issues: $($dataset3.Tables[0].Rows.Count)" -ForegroundColor $(if ($dataset3.Tables[0].Rows.Count -gt 0) { "Yellow" } else { "Green" })
    Write-Host "Active Analyst assignments: $($dataset4.Tables[0].Rows.Count)" -ForegroundColor White
    Write-Host ""
    
    if ($dataset2.Tables[0].Rows.Count -gt 0) {
        Write-Host "ACTION: To fix stuck groups, run:" -ForegroundColor Yellow
        Write-Host "   POST /api/image-analysis-management/fix-stuck-groups" -ForegroundColor Cyan
        Write-Host "   OR run: scripts\FixStuckAnalystGroups.sql" -ForegroundColor Cyan
    }
    
    $connection.Close()
    Write-Host ""
    Write-Host "OK: Diagnostic complete" -ForegroundColor Green
}
catch {
    $errorMsg = $_.Exception.Message
    Write-Host "ERROR: $errorMsg" -ForegroundColor Red
    if ($_.Exception.StackTrace) {
        Write-Host $_.Exception.StackTrace -ForegroundColor Red
    }
    exit 1
}


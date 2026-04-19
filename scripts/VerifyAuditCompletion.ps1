# Verify that audit decisions properly move records to Completed stage
param(
    [string]$ConnectionString = "",
    [string]$GroupIdentifier = ""
)

Write-Host "================================================" -ForegroundColor Cyan
Write-Host "Verifying Audit Completion Workflow" -ForegroundColor Cyan
Write-Host "================================================" -ForegroundColor Cyan
Write-Host ""

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
    Write-Host "   .\VerifyAuditCompletion.ps1 -ConnectionString 'Server=...;Database=...;...' [-GroupIdentifier 'GROUP123']" -ForegroundColor Yellow
    exit 1
}

try {
    $connection = New-Object System.Data.SqlClient.SqlConnection($ConnectionString)
    $connection.Open()

    Write-Host "1. Checking database trigger exists..." -ForegroundColor Yellow
    $triggerCheck = @"
    SELECT name, is_disabled 
    FROM sys.triggers 
    WHERE name = 'trg_AuditDecision_AdvanceStage'
"@
    $commandTrigger = New-Object System.Data.SqlClient.SqlCommand($triggerCheck, $connection)
    $adapterTrigger = New-Object System.Data.SqlClient.SqlDataAdapter($commandTrigger)
    $datasetTrigger = New-Object System.Data.DataSet
    $adapterTrigger.Fill($datasetTrigger) | Out-Null
    
    if ($datasetTrigger.Tables[0].Rows.Count -eq 0) {
        Write-Host "   WARNING: Trigger trg_AuditDecision_AdvanceStage does not exist!" -ForegroundColor Red
    } else {
        $isDisabled = $datasetTrigger.Tables[0].Rows[0]["is_disabled"]
        if ($isDisabled) {
            Write-Host "   WARNING: Trigger trg_AuditDecision_AdvanceStage is DISABLED!" -ForegroundColor Red
        } else {
            Write-Host "   OK: Trigger exists and is enabled" -ForegroundColor Green
        }
    }
    Write-Host ""

    Write-Host "2. Checking WorkflowStage distribution..." -ForegroundColor Yellow
    $stageQuery = @"
    SELECT 
        WorkflowStage,
        COUNT(*) AS RecordCount
    FROM ContainerCompletenessStatuses
    GROUP BY WorkflowStage
    ORDER BY 
        CASE WorkflowStage
            WHEN 'ImageAnalysis' THEN 1
            WHEN 'Audit' THEN 2
            WHEN 'Completed' THEN 3
            WHEN 'Pending' THEN 4
            ELSE 5
        END
"@
    $commandStage = New-Object System.Data.SqlClient.SqlCommand($stageQuery, $connection)
    $adapterStage = New-Object System.Data.SqlClient.SqlDataAdapter($commandStage)
    $datasetStage = New-Object System.Data.DataSet
    $adapterStage.Fill($datasetStage) | Out-Null
    
    foreach ($row in $datasetStage.Tables[0].Rows) {
        $stage = $row["WorkflowStage"]
        $count = $row["RecordCount"]
        Write-Host "   $stage : $count records" -ForegroundColor Cyan
    }
    Write-Host ""

    if (![string]::IsNullOrEmpty($GroupIdentifier)) {
        Write-Host "3. Checking specific group: $GroupIdentifier" -ForegroundColor Yellow
        
        # Check containers in this group
        $groupQuery = @"
        SELECT 
            c.ContainerNumber,
            c.ScannerType,
            c.WorkflowStage,
            a.Decision AS AuditDecision,
            a.AuditedBy,
            a.AuditedAt,
            a.IsCompleted,
            ag.Status AS AnalysisGroupStatus
        FROM ContainerCompletenessStatuses c
        LEFT JOIN AuditDecisions a ON a.ContainerNumber = c.ContainerNumber 
            AND a.ScannerType = c.ScannerType 
            AND a.GroupIdentifier = c.GroupIdentifier
        LEFT JOIN AnalysisGroups ag ON ag.GroupIdentifier = c.GroupIdentifier
        WHERE c.GroupIdentifier = @GroupIdentifier
        ORDER BY c.ContainerNumber
"@
        $commandGroup = New-Object System.Data.SqlClient.SqlCommand($groupQuery, $connection)
        $commandGroup.Parameters.AddWithValue("@GroupIdentifier", $GroupIdentifier)
        $adapterGroup = New-Object System.Data.SqlClient.SqlDataAdapter($commandGroup)
        $datasetGroup = New-Object System.Data.DataSet
        $adapterGroup.Fill($datasetGroup) | Out-Null
        
        if ($datasetGroup.Tables[0].Rows.Count -eq 0) {
            Write-Host "   WARNING: No records found for group $GroupIdentifier" -ForegroundColor Red
        } else {
            Write-Host "   Found $($datasetGroup.Tables[0].Rows.Count) container(s) in group:" -ForegroundColor Cyan
            Write-Host ""
            
            $allCompleted = $true
            $allHaveAuditDecisions = $true
            $analysisGroupStatus = ""
            
            foreach ($row in $datasetGroup.Tables[0].Rows) {
                $container = $row["ContainerNumber"]
                $workflowStage = $row["WorkflowStage"]
                $auditDecision = $row["AuditDecision"]
                $isCompleted = $row["IsCompleted"]
                $analysisGroupStatus = $row["AnalysisGroupStatus"]
                
                if ($workflowStage -ne "Completed") {
                    $allCompleted = $false
                }
                
                if ([string]::IsNullOrEmpty($auditDecision)) {
                    $allHaveAuditDecisions = $false
                }
                
                $statusIcon = if ($workflowStage -eq "Completed") { "OK" } else { "WARNING" }
                $statusColor = if ($workflowStage -eq "Completed") { "Green" } else { "Yellow" }
                
                Write-Host "   Container: $container" -ForegroundColor White
                Write-Host "     WorkflowStage: $workflowStage" -ForegroundColor $statusColor
                Write-Host "     AuditDecision: $(if ([string]::IsNullOrEmpty($auditDecision)) { 'None' } else { $auditDecision })" -ForegroundColor $(if ([string]::IsNullOrEmpty($auditDecision)) { "Yellow" } else { "Cyan" })
                Write-Host "     IsCompleted: $isCompleted" -ForegroundColor $(if ($isCompleted) { "Green" } else { "Yellow" })
                Write-Host ""
            }
            
            Write-Host "   AnalysisGroup.Status: $analysisGroupStatus" -ForegroundColor Cyan
            Write-Host ""
            
            if ($allCompleted -and $allHaveAuditDecisions) {
                Write-Host "   RESULT: All containers are properly completed!" -ForegroundColor Green
            } else {
                Write-Host "   RESULT: Issues found:" -ForegroundColor Red
                if (!$allCompleted) {
                    Write-Host "     - Some containers are not in 'Completed' stage" -ForegroundColor Yellow
                }
                if (!$allHaveAuditDecisions) {
                    Write-Host "     - Some containers are missing audit decisions" -ForegroundColor Yellow
                }
            }
        }
        Write-Host ""
    }

    Write-Host "4. Checking recent audit decisions..." -ForegroundColor Yellow
    $recentAuditQuery = @"
    SELECT TOP 10
        a.ContainerNumber,
        a.GroupIdentifier,
        a.Decision,
        a.AuditedAt,
        a.IsCompleted,
        c.WorkflowStage
    FROM AuditDecisions a
    INNER JOIN ContainerCompletenessStatuses c ON c.ContainerNumber = a.ContainerNumber 
        AND c.ScannerType = a.ScannerType
    ORDER BY a.AuditedAt DESC
"@
    $commandRecent = New-Object System.Data.SqlClient.SqlCommand($recentAuditQuery, $connection)
    $adapterRecent = New-Object System.Data.SqlClient.SqlDataAdapter($commandRecent)
    $datasetRecent = New-Object System.Data.DataSet
    $adapterRecent.Fill($datasetRecent) | Out-Null
    
    if ($datasetRecent.Tables[0].Rows.Count -eq 0) {
        Write-Host "   No audit decisions found" -ForegroundColor Yellow
    } else {
        Write-Host "   Recent audit decisions:" -ForegroundColor Cyan
        $mismatchCount = 0
        foreach ($row in $datasetRecent.Tables[0].Rows) {
            $container = $row["ContainerNumber"]
            $group = $row["GroupIdentifier"]
            $decision = $row["Decision"]
            $auditedAt = $row["AuditedAt"]
            $isCompleted = $row["IsCompleted"]
            $workflowStage = $row["WorkflowStage"]
            
            $status = if ($workflowStage -eq "Completed") { "OK" } else { "MISMATCH" }
            if ($status -eq "MISMATCH") {
                $mismatchCount++
            }
            
            Write-Host "     $container ($group): Decision=$decision, Stage=$workflowStage, Completed=$isCompleted [$status]" -ForegroundColor $(if ($status -eq "OK") { "Green" } else { "Red" })
        }
        
        if ($mismatchCount -gt 0) {
            Write-Host ""
            Write-Host "   WARNING: $mismatchCount record(s) have audit decisions but WorkflowStage is not 'Completed'" -ForegroundColor Red
            Write-Host "   This suggests the trigger may not be firing correctly!" -ForegroundColor Red
        } else {
            Write-Host ""
            Write-Host "   OK: All recent audit decisions have correct WorkflowStage" -ForegroundColor Green
        }
    }

    Write-Host ""
    Write-Host "Verification complete!" -ForegroundColor Green

} catch {
    Write-Host "ERROR: $($_.Exception.Message)" -ForegroundColor Red
    Write-Host $_.Exception.StackTrace -ForegroundColor Red
    exit 1
} finally {
    if ($connection.State -eq 'Open') {
        $connection.Close()
    }
}


# Check if audit decision was saved for a specific container
param(
    [Parameter(Mandatory=$true)]
    [string]$ContainerNumber
)

Write-Host "==================================================" -ForegroundColor Cyan
Write-Host "Checking Audit Decision for Container: $ContainerNumber" -ForegroundColor Cyan
Write-Host "==================================================" -ForegroundColor Cyan
Write-Host ""

# Load connection string from appsettings.json
$appsettingsPath = "src\NickScanCentralImagingPortal.API\appsettings.json"
$appsettingsDevPath = "src\NickScanCentralImagingPortal.API\appsettings.Development.json"

if (-not (Test-Path $appsettingsPath)) {
    Write-Host "❌ Error: appsettings.json not found at $appsettingsPath" -ForegroundColor Red
    exit 1
}

$appsettings = Get-Content $appsettingsPath | ConvertFrom-Json
$connectionString = $appsettings.ConnectionStrings.NS_CIS_Connection

if (Test-Path $appsettingsDevPath) {
    $appsettingsDev = Get-Content $appsettingsDevPath | ConvertFrom-Json
    if ($appsettingsDev.ConnectionStrings.NS_CIS_Connection) {
        $connectionString = $appsettingsDev.ConnectionStrings.NS_CIS_Connection
    }
}

if ([string]::IsNullOrEmpty($connectionString)) {
    Write-Host "❌ Error: Connection string not found in appsettings.json" -ForegroundColor Red
    exit 1
}

try {
    $connection = New-Object System.Data.SqlClient.SqlConnection($connectionString)
    $connection.Open()
    
    Write-Host "✅ Connected to database" -ForegroundColor Green
    Write-Host ""
    
    # 1. Check AuditDecisions
    Write-Host "1. Checking AuditDecisions table..." -ForegroundColor Yellow
    $auditQuery = @"
    SELECT 
        Id,
        ContainerNumber,
        GroupIdentifier,
        ScannerType,
        Decision,
        AuditNotes,
        AuditedBy,
        AuditedAt,
        IsCompleted,
        CompletedAt,
        OverallGroupDecision,
        CreatedAt,
        UpdatedAt
    FROM AuditDecisions
    WHERE ContainerNumber = @ContainerNumber
    ORDER BY AuditedAt DESC
"@
    
    $commandAudit = New-Object System.Data.SqlClient.SqlCommand($auditQuery, $connection)
    $commandAudit.Parameters.AddWithValue("@ContainerNumber", $ContainerNumber)
    $adapterAudit = New-Object System.Data.SqlClient.SqlDataAdapter($commandAudit)
    $datasetAudit = New-Object System.Data.DataSet
    $adapterAudit.Fill($datasetAudit) | Out-Null
    
    if ($datasetAudit.Tables[0].Rows.Count -eq 0) {
        Write-Host "   ❌ NO AUDIT DECISION FOUND for container $ContainerNumber" -ForegroundColor Red
        Write-Host "   → The audit decision was NOT saved successfully" -ForegroundColor Red
    } else {
        Write-Host "   ✅ Found $($datasetAudit.Tables[0].Rows.Count) audit decision(s):" -ForegroundColor Green
        foreach ($row in $datasetAudit.Tables[0].Rows) {
            Write-Host "      Decision: $($row['Decision'])" -ForegroundColor Cyan
            Write-Host "      GroupIdentifier: $($row['GroupIdentifier'])" -ForegroundColor White
            Write-Host "      ScannerType: $($row['ScannerType'])" -ForegroundColor White
            Write-Host "      AuditedBy: $($row['AuditedBy'])" -ForegroundColor White
            Write-Host "      AuditedAt: $($row['AuditedAt'])" -ForegroundColor White
            Write-Host "      IsCompleted: $($row['IsCompleted'])" -ForegroundColor $(if ($row['IsCompleted']) { "Green" } else { "Yellow" })
            Write-Host "      OverallGroupDecision: $($row['OverallGroupDecision'])" -ForegroundColor White
            if (-not [string]::IsNullOrEmpty($row['AuditNotes'])) {
                Write-Host "      AuditNotes: $($row['AuditNotes'])" -ForegroundColor White
            }
            Write-Host ""
        }
    }
    Write-Host ""
    
    # 2. Check ContainerCompletenessStatus
    Write-Host "2. Checking ContainerCompletenessStatus..." -ForegroundColor Yellow
    $completenessQuery = @"
    SELECT 
        Id,
        ContainerNumber,
        ScannerType,
        GroupIdentifier,
        WorkflowStage,
        Status,
        IsConsolidated,
        CreatedAt,
        UpdatedAt
    FROM ContainerCompletenessStatuses
    WHERE ContainerNumber = @ContainerNumber
    ORDER BY UpdatedAt DESC
"@
    
    $commandCompleteness = New-Object System.Data.SqlClient.SqlCommand($completenessQuery, $connection)
    $commandCompleteness.Parameters.AddWithValue("@ContainerNumber", $ContainerNumber)
    $adapterCompleteness = New-Object System.Data.SqlClient.SqlDataAdapter($commandCompleteness)
    $datasetCompleteness = New-Object System.Data.DataSet
    $adapterCompleteness.Fill($datasetCompleteness) | Out-Null
    
    if ($datasetCompleteness.Tables[0].Rows.Count -eq 0) {
        Write-Host "   ⚠️ No ContainerCompletenessStatus found" -ForegroundColor Yellow
    } else {
        Write-Host "   ✅ Found $($datasetCompleteness.Tables[0].Rows.Count) record(s):" -ForegroundColor Green
        foreach ($row in $datasetCompleteness.Tables[0].Rows) {
            $workflowStage = $row["WorkflowStage"]
            $stageColor = if ($workflowStage -eq "Completed") { "Green" } elseif ($workflowStage -eq "Audit") { "Yellow" } else { "Red" }
            Write-Host "      WorkflowStage: $workflowStage" -ForegroundColor $stageColor
            Write-Host "      Status: $($row['Status'])" -ForegroundColor White
            Write-Host "      GroupIdentifier: $($row['GroupIdentifier'])" -ForegroundColor White
            Write-Host "      ScannerType: $($row['ScannerType'])" -ForegroundColor White
            Write-Host ""
        }
    }
    Write-Host ""
    
    # 3. Check ImageAnalysisDecision
    Write-Host "3. Checking ImageAnalysisDecision (original decision)..." -ForegroundColor Yellow
    $imageQuery = @"
    SELECT 
        Id,
        ContainerNumber,
        ScannerType,
        GroupIdentifier,
        Decision,
        ReviewedBy,
        ReviewedAt,
        Comments,
        CreatedAt,
        UpdatedAt
    FROM ImageAnalysisDecisions
    WHERE ContainerNumber = @ContainerNumber
    ORDER BY ReviewedAt DESC
"@
    
    $commandImage = New-Object System.Data.SqlClient.SqlCommand($imageQuery, $connection)
    $commandImage.Parameters.AddWithValue("@ContainerNumber", $ContainerNumber)
    $adapterImage = New-Object System.Data.SqlClient.SqlDataAdapter($commandImage)
    $datasetImage = New-Object System.Data.DataSet
    $adapterImage.Fill($datasetImage) | Out-Null
    
    if ($datasetImage.Tables[0].Rows.Count -eq 0) {
        Write-Host "   ⚠️ No ImageAnalysisDecision found" -ForegroundColor Yellow
    } else {
        Write-Host "   ✅ Found $($datasetImage.Tables[0].Rows.Count) decision(s):" -ForegroundColor Green
        foreach ($row in $datasetImage.Tables[0].Rows) {
            Write-Host "      Decision: $($row['Decision'])" -ForegroundColor Cyan
            Write-Host "      ReviewedBy: $($row['ReviewedBy'])" -ForegroundColor White
            Write-Host "      ReviewedAt: $($row['ReviewedAt'])" -ForegroundColor White
            Write-Host ""
        }
    }
    Write-Host ""
    
    # 4. Summary
    Write-Host "==================================================" -ForegroundColor Cyan
    Write-Host "SUMMARY" -ForegroundColor Cyan
    Write-Host "==================================================" -ForegroundColor Cyan
    Write-Host ""
    
    $hasAuditDecision = $datasetAudit.Tables[0].Rows.Count -gt 0
    $workflowStage = if ($datasetCompleteness.Tables[0].Rows.Count -gt 0) { 
        $datasetCompleteness.Tables[0].Rows[0]["WorkflowStage"] 
    } else { 
        "Not Found" 
    }
    
    if ($hasAuditDecision) {
        $latestDecision = $datasetAudit.Tables[0].Rows[0]
        Write-Host "✅ Audit Decision Status: SAVED" -ForegroundColor Green
        Write-Host "   Decision: $($latestDecision['Decision'])" -ForegroundColor Cyan
        Write-Host "   AuditedBy: $($latestDecision['AuditedBy'])" -ForegroundColor White
        Write-Host "   AuditedAt: $($latestDecision['AuditedAt'])" -ForegroundColor White
        Write-Host ""
        
        if ($workflowStage -eq "Completed") {
            Write-Host "✅ WorkflowStage: Completed (Expected)" -ForegroundColor Green
        } elseif ($workflowStage -eq "Audit") {
            Write-Host "⚠️ WorkflowStage: Audit (Should be 'Completed' after audit decision)" -ForegroundColor Yellow
            Write-Host "   → The trigger may not have fired, or there was an issue updating the stage" -ForegroundColor Yellow
        } else {
            Write-Host "⚠️ WorkflowStage: $workflowStage (Unexpected)" -ForegroundColor Yellow
        }
    } else {
        Write-Host "❌ Audit Decision Status: NOT SAVED" -ForegroundColor Red
        Write-Host "   → The audit decision was NOT found in the database" -ForegroundColor Red
        Write-Host "   → Check application logs for errors during submission" -ForegroundColor Yellow
    }
    
    Write-Host ""
    Write-Host "✅ Check complete" -ForegroundColor Green
    
} catch {
    Write-Host "❌ Error: $($_.Exception.Message)" -ForegroundColor Red
    Write-Host $_.Exception.StackTrace -ForegroundColor Red
    exit 1
} finally {
    if ($connection.State -eq 'Open') {
        $connection.Close()
    }
}


# Diagnose why a specific group is not in audit queue
param(
    [Parameter(Mandatory=$true)]
    [string]$GroupIdentifier
)

Write-Host "==================================================" -ForegroundColor Cyan
Write-Host "Audit Queue Diagnosis for Group: $GroupIdentifier" -ForegroundColor Cyan
Write-Host "==================================================" -ForegroundColor Cyan
Write-Host ""

# Load connection string from appsettings.json
$appsettingsPath = "src\NickScanCentralImagingPortal.API\appsettings.json"
if (-not (Test-Path $appsettingsPath)) {
    Write-Host "❌ Error: appsettings.json not found at $appsettingsPath" -ForegroundColor Red
    exit 1
}

$appsettings = Get-Content $appsettingsPath | ConvertFrom-Json
$connectionString = $appsettings.ConnectionStrings.NS_CIS_Connection

if ([string]::IsNullOrEmpty($connectionString)) {
    Write-Host "❌ Error: Connection string not found in appsettings.json" -ForegroundColor Red
    exit 1
}

try {
    $connection = New-Object System.Data.SqlClient.SqlConnection($connectionString)
    $connection.Open()
    
    Write-Host "✅ Connected to database" -ForegroundColor Green
    Write-Host ""
    
    # 1. Check ContainerCompletenessStatuses
    Write-Host "1. Checking ContainerCompletenessStatuses..." -ForegroundColor Yellow
    $query1 = @"
    SELECT 
        ContainerNumber,
        ScannerType,
        GroupIdentifier,
        WorkflowStage,
        Status,
        IsConsolidated,
        CreatedAt,
        UpdatedAt
    FROM ContainerCompletenessStatuses
    WHERE GroupIdentifier = @GroupIdentifier
    ORDER BY ContainerNumber, ScannerType
"@
    
    $command1 = New-Object System.Data.SqlClient.SqlCommand($query1, $connection)
    $command1.Parameters.AddWithValue("@GroupIdentifier", $GroupIdentifier)
    $adapter1 = New-Object System.Data.SqlClient.SqlDataAdapter($command1)
    $dataset1 = New-Object System.Data.DataSet
    $adapter1.Fill($dataset1) | Out-Null
    
    if ($dataset1.Tables[0].Rows.Count -eq 0) {
        Write-Host "   ❌ No records found in ContainerCompletenessStatuses with GroupIdentifier = '$GroupIdentifier'" -ForegroundColor Red
    } else {
        Write-Host "   ✅ Found $($dataset1.Tables[0].Rows.Count) container(s) for this group:" -ForegroundColor Green
        foreach ($row in $dataset1.Tables[0].Rows) {
            $workflowStage = $row["WorkflowStage"]
            $status = $row["Status"]
            $container = $row["ContainerNumber"]
            $scanner = $row["ScannerType"]
            
            $stageColor = if ($workflowStage -eq "Audit") { "Green" } else { "Red" }
            Write-Host "      Container: $container ($scanner)" -ForegroundColor White
            Write-Host "        WorkflowStage: $workflowStage" -ForegroundColor $stageColor
            Write-Host "        Status: $status" -ForegroundColor White
            Write-Host ""
        }
    }
    
    Write-Host ""
    
    # 2. Check AnalysisGroups
    Write-Host "2. Checking AnalysisGroups..." -ForegroundColor Yellow
    $query2 = @"
    SELECT 
        Id,
        GroupIdentifier,
        ScannerType,
        Status,
        CreatedAtUtc,
        UpdatedAtUtc
    FROM AnalysisGroups
    WHERE GroupIdentifier = @GroupIdentifier
"@
    
    $command2 = New-Object System.Data.SqlClient.SqlCommand($query2, $connection)
    $command2.Parameters.AddWithValue("@GroupIdentifier", $GroupIdentifier)
    $adapter2 = New-Object System.Data.SqlClient.SqlDataAdapter($command2)
    $dataset2 = New-Object System.Data.DataSet
    $adapter2.Fill($dataset2) | Out-Null
    
    if ($dataset2.Tables[0].Rows.Count -eq 0) {
        Write-Host "   ❌ No AnalysisGroup found with GroupIdentifier = '$GroupIdentifier'" -ForegroundColor Red
    } else {
        $groupRow = $dataset2.Tables[0].Rows[0]
        $groupStatus = $groupRow["Status"]
        Write-Host "   ✅ Found AnalysisGroup:" -ForegroundColor Green
        Write-Host "      Status: $groupStatus" -ForegroundColor White
        Write-Host "      ScannerType: $($groupRow['ScannerType'])" -ForegroundColor White
        Write-Host "      CreatedAt: $($groupRow['CreatedAtUtc'])" -ForegroundColor White
        Write-Host "      UpdatedAt: $($groupRow['UpdatedAtUtc'])" -ForegroundColor White
    }
    
    Write-Host ""
    
    # 3. Check ImageAnalysisDecisions
    Write-Host "3. Checking ImageAnalysisDecisions..." -ForegroundColor Yellow
    $query3 = @"
    SELECT 
        ContainerNumber,
        ScannerType,
        GroupIdentifier,
        Decision,
        ReviewedBy,
        ReviewedAt
    FROM ImageAnalysisDecisions
    WHERE GroupIdentifier = @GroupIdentifier
    ORDER BY ContainerNumber, ScannerType
"@
    
    $command3 = New-Object System.Data.SqlClient.SqlCommand($query3, $connection)
    $command3.Parameters.AddWithValue("@GroupIdentifier", $GroupIdentifier)
    $adapter3 = New-Object System.Data.SqlClient.SqlDataAdapter($command3)
    $dataset3 = New-Object System.Data.DataSet
    $adapter3.Fill($dataset3) | Out-Null
    
    if ($dataset3.Tables[0].Rows.Count -eq 0) {
        Write-Host "   ⚠️ No ImageAnalysisDecisions found for this group" -ForegroundColor Yellow
        Write-Host "      This means analyst decisions have not been saved yet" -ForegroundColor Yellow
    } else {
        Write-Host "   ✅ Found $($dataset3.Tables[0].Rows.Count) decision(s):" -ForegroundColor Green
        foreach ($row in $dataset3.Tables[0].Rows) {
            $decision = $row["Decision"]
            $container = $row["ContainerNumber"]
            $scanner = $row["ScannerType"]
            $decisionColor = if ($decision -in @("Normal", "Abnormal")) { "Green" } else { "Yellow" }
            Write-Host "      Container: $container ($scanner) - Decision: $decision" -ForegroundColor $decisionColor
        }
    }
    
    Write-Host ""
    
    # 4. Check AnalysisAssignments
    Write-Host "4. Checking AnalysisAssignments..." -ForegroundColor Yellow
    $query4 = @"
    SELECT 
        a.Id,
        a.GroupId,
        a.AssignedTo,
        a.Role,
        a.State,
        a.LeaseUntilUtc,
        a.CreatedAtUtc,
        g.GroupIdentifier
    FROM AnalysisAssignments a
    INNER JOIN AnalysisGroups g ON a.GroupId = g.Id
    WHERE g.GroupIdentifier = @GroupIdentifier
    ORDER BY a.CreatedAtUtc DESC
"@
    
    $command4 = New-Object System.Data.SqlClient.SqlCommand($query4, $connection)
    $command4.Parameters.AddWithValue("@GroupIdentifier", $GroupIdentifier)
    $adapter4 = New-Object System.Data.SqlClient.SqlDataAdapter($command4)
    $dataset4 = New-Object System.Data.DataSet
    $adapter4.Fill($dataset4) | Out-Null
    
    if ($dataset4.Tables[0].Rows.Count -eq 0) {
        Write-Host "   ⚠️ No AnalysisAssignments found for this group" -ForegroundColor Yellow
    } else {
        Write-Host "   ✅ Found $($dataset4.Tables[0].Rows.Count) assignment(s):" -ForegroundColor Green
        foreach ($row in $dataset4.Tables[0].Rows) {
            Write-Host "      AssignedTo: $($row['AssignedTo']) | Role: $($row['Role']) | State: $($row['State'])" -ForegroundColor White
        }
    }
    
    Write-Host ""
    
    # 5. Summary and recommendations
    Write-Host "==================================================" -ForegroundColor Cyan
    Write-Host "SUMMARY & RECOMMENDATIONS" -ForegroundColor Cyan
    Write-Host "==================================================" -ForegroundColor Cyan
    Write-Host ""
    
    $hasCompletenessRecords = $dataset1.Tables[0].Rows.Count -gt 0
    $hasAnalysisGroup = $dataset2.Tables[0].Rows.Count -gt 0
    $hasDecisions = $dataset3.Tables[0].Rows.Count -gt 0
    
    if (-not $hasCompletenessRecords) {
        Write-Host "❌ ISSUE: No ContainerCompletenessStatuses records found" -ForegroundColor Red
        Write-Host "   → Group may not exist or GroupIdentifier is incorrect" -ForegroundColor Yellow
    } elseif ($hasCompletenessRecords) {
        $allAuditStage = $true
        foreach ($row in $dataset1.Tables[0].Rows) {
            if ($row["WorkflowStage"] -ne "Audit") {
                $allAuditStage = $false
                break
            }
        }
        
        if (-not $allAuditStage) {
            Write-Host "❌ ISSUE: Not all containers have WorkflowStage = 'Audit'" -ForegroundColor Red
            Write-Host "   → Some containers may still be in 'ImageAnalysis' stage" -ForegroundColor Yellow
            Write-Host "   → Analyst decisions may not have been saved yet" -ForegroundColor Yellow
        } else {
            Write-Host "✅ All containers have WorkflowStage = 'Audit'" -ForegroundColor Green
            Write-Host "   → Group should appear in audit queue" -ForegroundColor Green
        }
    }
    
    if ($hasAnalysisGroup) {
        $groupStatus = $dataset2.Tables[0].Rows[0]["Status"]
        if ($groupStatus -ne "AnalystCompleted" -and $groupStatus -ne "AuditAssigned") {
            Write-Host "⚠️ AnalysisGroup.Status = '$groupStatus' (expected: AnalystCompleted or AuditAssigned)" -ForegroundColor Yellow
        }
    }
    
    if (-not $hasDecisions) {
        Write-Host "❌ ISSUE: No ImageAnalysisDecisions found" -ForegroundColor Red
        Write-Host "   → Analyst decisions must be saved before group can move to audit" -ForegroundColor Yellow
    }
    
    $connection.Close()
    Write-Host ""
    Write-Host "✅ Diagnosis complete" -ForegroundColor Green
}
catch {
    Write-Host "❌ Error: $($_.Exception.Message)" -ForegroundColor Red
    if ($connection.State -eq 'Open') {
        $connection.Close()
    }
    exit 1
}


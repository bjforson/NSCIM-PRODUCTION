# Check WorkflowStage of containers in Ready groups
# This is a critical filter that AssignmentWorker applies

param(
    [string]$Server = "127.0.0.1,1433",
    [string]$Database = "NS_CIS"
)

# Continues past errors intentionally: read-only diagnostic that aggregates WorkflowStage stats across many groups; per-stage query failures must not abort the report.
$ErrorActionPreference = "Continue"

Write-Host "Check WorkflowStage Filter" -ForegroundColor Cyan
Write-Host "=========================" -ForegroundColor Cyan
Write-Host ""

# Check WorkflowStage distribution for Ready groups
$workflowQuery = @"
SELECT 
    ag.GroupIdentifier,
    ag.Status AS GroupStatus,
    ccs.WorkflowStage,
    COUNT(DISTINCT ar.ContainerNumber) AS ContainerCount
FROM AnalysisGroups ag
INNER JOIN AnalysisRecords ar ON ar.GroupId = ag.Id
INNER JOIN ContainerCompletenessStatuses ccs ON ccs.ContainerNumber = ar.ContainerNumber
WHERE ag.Status = 'Ready'
GROUP BY ag.GroupIdentifier, ag.Status, ccs.WorkflowStage
ORDER BY ag.GroupIdentifier, ccs.WorkflowStage
"@

$workflows = Invoke-Sqlcmd -ServerInstance $Server -Database $Database -Query $workflowQuery

if ($workflows) {
    Write-Host "WorkflowStage Distribution for Ready Groups:" -ForegroundColor Yellow
    Write-Host "------------------------------------------" -ForegroundColor Yellow
    
    # Group by WorkflowStage
    $stageSummary = $workflows | Group-Object -Property WorkflowStage | Select-Object Name, Count, @{Name='TotalContainers';Expression={($_.Group | Measure-Object -Property ContainerCount -Sum).Sum}}
    
    $stageSummary | Format-Table -AutoSize
    Write-Host ""
    
    # Check for groups with ImageAnalysis or Pending stages (eligible for Analyst)
    $eligibleStages = @("ImageAnalysis", "Pending")
    $eligibleGroups = $workflows | Where-Object { $eligibleStages -contains $_.WorkflowStage } | Select-Object -ExpandProperty GroupIdentifier -Unique
    
    Write-Host "Groups with ImageAnalysis or Pending stages (ELIGIBLE for Analyst):" -ForegroundColor Cyan
    Write-Host "  Count: $($eligibleGroups.Count)" -ForegroundColor White
    Write-Host ""
    
    # Check for groups with only Audit or Completed stages (NOT eligible)
    $ineligibleGroups = $workflows | 
        Group-Object -Property GroupIdentifier | 
        Where-Object { 
            $stages = $_.Group.WorkflowStage | Select-Object -Unique
            $stages.Count -eq 1 -and ($stages -contains "Audit" -or $stages -contains "Completed")
        } | 
        Select-Object -ExpandProperty Name
    
    if ($ineligibleGroups) {
        Write-Host "Groups with ONLY Audit or Completed stages (NOT eligible for Analyst):" -ForegroundColor Yellow
        Write-Host "  Count: $($ineligibleGroups.Count)" -ForegroundColor White
        Write-Host "  These groups will be filtered out by AssignmentWorker" -ForegroundColor Yellow
    }
    
    # Sample groups
    Write-Host ""
    Write-Host "Sample Ready Groups (first 10):" -ForegroundColor Cyan
    $workflows | Select-Object -First 10 | Format-Table -AutoSize
} else {
    Write-Host "WARNING: No WorkflowStage data found" -ForegroundColor Yellow
}

Write-Host ""

# Summary
Write-Host "========================================" -ForegroundColor Cyan
Write-Host "CRITICAL FILTER" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""
Write-Host "AssignmentWorker filters Ready groups by WorkflowStage:" -ForegroundColor Yellow
Write-Host "  For Analyst role:" -ForegroundColor White
Write-Host "    - Groups with ImageAnalysisContainers > 0 → ELIGIBLE" -ForegroundColor Green
Write-Host "    - Groups with ONLY Audit/Completed → FILTERED OUT" -ForegroundColor Red
Write-Host ""
Write-Host "If all containers in Ready groups have WorkflowStage='Audit' or 'Completed'," -ForegroundColor Yellow
Write-Host "AssignmentWorker will filter them all out and find 0 eligible groups." -ForegroundColor Yellow
Write-Host ""


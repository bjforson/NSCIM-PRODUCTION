# Diagnostic script to check WorkflowStage distribution and identify assignment blockers
# This script checks:
# 1. WorkflowStage distribution for complete containers
# 2. Ready groups and their WorkflowStage distribution
# 3. Why groups might be excluded from assignment

param(
    [string]$ServerInstance = "localhost",
    [string]$Database = "NS_CIS"
)

Write-Host "========================================" -ForegroundColor Cyan
Write-Host "WorkflowStage Diagnostic Analysis" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""

# Check 1: WorkflowStage distribution for complete containers
Write-Host "1. WorkflowStage Distribution for Complete Containers" -ForegroundColor Yellow
Write-Host "---------------------------------------------------" -ForegroundColor Yellow

$query1 = @"
SELECT 
    ISNULL(WorkflowStage, 'NULL') as WorkflowStage,
    COUNT(*) as Count,
    CAST(COUNT(*) * 100.0 / SUM(COUNT(*)) OVER() AS DECIMAL(5,2)) as Percentage
FROM ContainerCompletenessStatuses
WHERE Status LIKE 'Complete%'
GROUP BY WorkflowStage
ORDER BY Count DESC;
"@

try {
    $results1 = Invoke-Sqlcmd -ServerInstance $ServerInstance -Database $Database -Query $query1
    
    if ($results1) {
        $results1 | Format-Table -AutoSize
        Write-Host ""
        
        $nullCount = ($results1 | Where-Object { $_.WorkflowStage -eq 'NULL' }).Count
        $pendingCount = ($results1 | Where-Object { $_.WorkflowStage -eq 'Pending' }).Count
        $imageAnalysisCount = ($results1 | Where-Object { $_.WorkflowStage -eq 'ImageAnalysis' }).Count
        
        if ($nullCount -gt 0 -or $pendingCount -gt 0) {
            Write-Host "WARNING: Found containers with NULL or Pending WorkflowStage!" -ForegroundColor Red
            Write-Host "  - NULL: $nullCount containers" -ForegroundColor Red
            Write-Host "  - Pending: $pendingCount containers" -ForegroundColor Red
            Write-Host "  - ImageAnalysis: $imageAnalysisCount containers" -ForegroundColor Green
            Write-Host ""
            Write-Host "These containers may not be eligible for assignment if WorkflowStage is not set to 'ImageAnalysis'." -ForegroundColor Yellow
        } else {
            Write-Host "OK: All complete containers have WorkflowStage set (not NULL or Pending)" -ForegroundColor Green
        }
    } else {
        Write-Host "No results found" -ForegroundColor Yellow
    }
} catch {
    Write-Host "Error executing query: $_" -ForegroundColor Red
}

Write-Host ""
Write-Host ""

# Check 2: Ready groups and their WorkflowStage distribution
Write-Host "2. Ready Groups and WorkflowStage Distribution" -ForegroundColor Yellow
Write-Host "---------------------------------------------------" -ForegroundColor Yellow

$query2 = @"
SELECT 
    ag.GroupIdentifier,
    ag.Status as GroupStatus,
    ag.Priority,
    COUNT(DISTINCT ccs.ContainerNumber) as TotalContainers,
    SUM(CASE WHEN ccs.WorkflowStage = 'ImageAnalysis' THEN 1 ELSE 0 END) as ImageAnalysisCount,
    SUM(CASE WHEN ccs.WorkflowStage = 'Pending' OR ccs.WorkflowStage IS NULL THEN 1 ELSE 0 END) as PendingOrNullCount,
    SUM(CASE WHEN ccs.WorkflowStage = 'Audit' THEN 1 ELSE 0 END) as AuditCount,
    SUM(CASE WHEN ccs.WorkflowStage = 'Completed' THEN 1 ELSE 0 END) as CompletedCount,
    -- Check if group would pass ReadyGroupsCacheService filter
    CASE 
        WHEN SUM(CASE WHEN ccs.WorkflowStage = 'ImageAnalysis' THEN 1 ELSE 0 END) > 0 THEN 'PASS (ImageAnalysis > 0)'
        WHEN SUM(CASE WHEN ccs.WorkflowStage = 'ImageAnalysis' THEN 1 ELSE 0 END) = 0 
             AND SUM(CASE WHEN ccs.WorkflowStage = 'Audit' THEN 1 ELSE 0 END) < COUNT(DISTINCT ccs.ContainerNumber)
             AND SUM(CASE WHEN ccs.WorkflowStage = 'Completed' THEN 1 ELSE 0 END) < COUNT(DISTINCT ccs.ContainerNumber)
        THEN 'PASS (Second condition)'
        ELSE 'FAIL (Filter excludes)'
    END as FilterResult
FROM AnalysisGroups ag
LEFT JOIN ContainerCompletenessStatuses ccs ON ccs.GroupIdentifier = ag.GroupIdentifier
WHERE ag.Status = 'Ready'
GROUP BY ag.GroupIdentifier, ag.Status, ag.Priority
ORDER BY ag.Priority DESC, TotalContainers DESC;
"@

try {
    $results2 = Invoke-Sqlcmd -ServerInstance $ServerInstance -Database $Database -Query $query2
    
    if ($results2) {
        Write-Host "Found $($results2.Count) Ready groups" -ForegroundColor Cyan
        Write-Host ""
        
        $results2 | Format-Table -AutoSize
        Write-Host ""
        
        $passCount = ($results2 | Where-Object { $_.FilterResult -like 'PASS*' }).Count
        $failCount = ($results2 | Where-Object { $_.FilterResult -like 'FAIL*' }).Count
        
        Write-Host "Filter Results:" -ForegroundColor Cyan
        Write-Host "  - PASS: $passCount groups" -ForegroundColor Green
        Write-Host "  - FAIL: $failCount groups" -ForegroundColor Red
        Write-Host ""
        
        if ($failCount -gt 0) {
            Write-Host "WARNING: $failCount Ready groups are being EXCLUDED by ReadyGroupsCacheService filter!" -ForegroundColor Red
            Write-Host ""
            Write-Host "Failed groups:" -ForegroundColor Yellow
            $results2 | Where-Object { $_.FilterResult -like 'FAIL*' } | Format-Table GroupIdentifier, TotalContainers, ImageAnalysisCount, PendingOrNullCount, AuditCount, CompletedCount, FilterResult -AutoSize
        }
        
        # Check for groups with Pending/Null WorkflowStages
        $pendingGroups = $results2 | Where-Object { $_.PendingOrNullCount -gt 0 }
        if ($pendingGroups) {
            Write-Host ""
            Write-Host "Groups with Pending/Null WorkflowStages:" -ForegroundColor Yellow
            $pendingGroups | Format-Table GroupIdentifier, TotalContainers, ImageAnalysisCount, PendingOrNullCount, FilterResult -AutoSize
        }
    } else {
        Write-Host "No Ready groups found" -ForegroundColor Yellow
    }
} catch {
    Write-Host "Error executing query: $_" -ForegroundColor Red
}

Write-Host ""
Write-Host ""

# Check 3: Check for groups that should be Ready but aren't
Write-Host "3. Groups That Should Be Ready But Aren't" -ForegroundColor Yellow
Write-Host "---------------------------------------------------" -ForegroundColor Yellow

$query3 = @"
SELECT 
    ag.GroupIdentifier,
    ag.Status as CurrentStatus,
    COUNT(DISTINCT ccs.ContainerNumber) as TotalContainers,
    SUM(CASE WHEN ccs.WorkflowStage = 'ImageAnalysis' THEN 1 ELSE 0 END) as ImageAnalysisCount,
    SUM(CASE WHEN ccs.WorkflowStage = 'Pending' OR ccs.WorkflowStage IS NULL THEN 1 ELSE 0 END) as PendingOrNullCount,
    SUM(CASE WHEN ccs.WorkflowStage = 'Audit' THEN 1 ELSE 0 END) as AuditCount,
    SUM(CASE WHEN ccs.WorkflowStage = 'Completed' THEN 1 ELSE 0 END) as CompletedCount
FROM AnalysisGroups ag
LEFT JOIN ContainerCompletenessStatuses ccs ON ccs.GroupIdentifier = ag.GroupIdentifier
WHERE ag.Status != 'Ready'
  AND ag.Status != 'Completed'
  AND (
      SUM(CASE WHEN ccs.WorkflowStage = 'ImageAnalysis' THEN 1 ELSE 0 END) > 0
      OR (SUM(CASE WHEN ccs.WorkflowStage = 'ImageAnalysis' THEN 1 ELSE 0 END) = 0 
          AND SUM(CASE WHEN ccs.WorkflowStage = 'Audit' THEN 1 ELSE 0 END) < COUNT(DISTINCT ccs.ContainerNumber)
          AND SUM(CASE WHEN ccs.WorkflowStage = 'Completed' THEN 1 ELSE 0 END) < COUNT(DISTINCT ccs.ContainerNumber))
  )
GROUP BY ag.GroupIdentifier, ag.Status
HAVING COUNT(DISTINCT ccs.ContainerNumber) > 0
ORDER BY TotalContainers DESC;
"@

try {
    $results3 = Invoke-Sqlcmd -ServerInstance $ServerInstance -Database $Database -Query $query3
    
    if ($results3) {
        Write-Host "Found $($results3.Count) groups that should be Ready but have different status" -ForegroundColor Yellow
        $results3 | Format-Table -AutoSize
    } else {
        Write-Host "No groups found that should be Ready but aren't" -ForegroundColor Green
    }
} catch {
    Write-Host "Error executing query: $_" -ForegroundColor Red
}

Write-Host ""
Write-Host ""

# Check 4: Summary and recommendations
Write-Host "4. Summary and Recommendations" -ForegroundColor Yellow
Write-Host "---------------------------------------------------" -ForegroundColor Yellow

Write-Host ""
Write-Host "Key Findings:" -ForegroundColor Cyan
Write-Host "1. Check if WorkflowStage is being set to 'ImageAnalysis' when containers become complete" -ForegroundColor White
Write-Host "2. Check if IntakeWorkflow updates WorkflowStage from 'Pending' to 'ImageAnalysis'" -ForegroundColor White
Write-Host "3. Verify ReadyGroupsCacheService filter logic matches actual WorkflowStage values" -ForegroundColor White
Write-Host ""
Write-Host "Next Steps:" -ForegroundColor Cyan
Write-Host "1. Run this script to see actual WorkflowStage distribution" -ForegroundColor White
Write-Host "2. Check ContainerCompletenessService logs for WorkflowStage updates" -ForegroundColor White
Write-Host "3. Check IntakeWorkflow logs for WorkflowStage updates" -ForegroundColor White
Write-Host "4. If WorkflowStage is not being set, fix ContainerCompletenessService or IntakeWorkflow" -ForegroundColor White
Write-Host ""


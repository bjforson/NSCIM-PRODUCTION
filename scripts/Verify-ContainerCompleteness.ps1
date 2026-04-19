# Verify ContainerCompletenessService
# Step 3 of Image Analyst Assignment Fix

param(
    [string]$Server = "127.0.0.1,1433",
    [string]$Database = "NS_CIS"
)

$ErrorActionPreference = "Continue"

Write-Host "Fix 3: Verify ContainerCompletenessService" -ForegroundColor Cyan
Write-Host "===========================================" -ForegroundColor Cyan
Write-Host ""

# Check 1: Container Completeness Status Summary
Write-Host "Check 1: Container Completeness Status" -ForegroundColor Yellow
Write-Host "-------------------------------------" -ForegroundColor Yellow

$statusQuery = "SELECT Status, COUNT(*) AS Count FROM ContainerCompletenessStatuseses GROUP BY Status ORDER BY Count DESC"
$statuses = Invoke-Sqlcmd -ServerInstance $Server -Database $Database -Query $statusQuery

if ($statuses) {
    Write-Host "Container Completeness Status Summary:" -ForegroundColor Cyan
    $statuses | Format-Table -AutoSize
    Write-Host ""
    
    $completeCount = ($statuses | Where-Object { $_.Status -eq "Complete" }).Count
    if ($completeCount -gt 0) {
        Write-Host "SUCCESS: Found $completeCount containers with Status='Complete'" -ForegroundColor Green
    } else {
        Write-Host "WARNING: No containers with Status='Complete'" -ForegroundColor Yellow
        Write-Host "   This means IntakeWorker has no input to create groups" -ForegroundColor Yellow
    }
} else {
    Write-Host "WARNING: No ContainerCompletenessStatus records found" -ForegroundColor Yellow
    Write-Host "   ContainerCompletenessService may not be running or no containers scanned" -ForegroundColor Yellow
}
Write-Host ""

# Check 2: Data Completeness Breakdown
Write-Host "Check 2: Data Completeness Breakdown" -ForegroundColor Yellow
Write-Host "-----------------------------------" -ForegroundColor Yellow

$completenessQuery = @"
SELECT 
    COUNT(*) AS TotalContainers,
    SUM(CASE WHEN HasScannerData = 1 THEN 1 ELSE 0 END) AS HasScannerData,
    SUM(CASE WHEN HasICUMSData = 1 THEN 1 ELSE 0 END) AS HasICUMSData,
    SUM(CASE WHEN HasImageData = 1 THEN 1 ELSE 0 END) AS HasImageData,
    SUM(CASE WHEN HasScannerData = 1 AND HasICUMSData = 1 AND HasImageData = 1 THEN 1 ELSE 0 END) AS AllDataAvailable
FROM ContainerCompletenessStatuses
"@

$completeness = Invoke-Sqlcmd -ServerInstance $Server -Database $Database -Query $completenessQuery

if ($completeness) {
    Write-Host "Data Completeness:" -ForegroundColor Cyan
    Write-Host "  - Total Containers: $($completeness.TotalContainers)" -ForegroundColor White
    Write-Host "  - Has Scanner Data: $($completeness.HasScannerData)" -ForegroundColor White
    Write-Host "  - Has ICUMS Data: $($completeness.HasICUMSData)" -ForegroundColor White
    Write-Host "  - Has Image Data: $($completeness.HasImageData)" -ForegroundColor White
    Write-Host "  - All Data Available: $($completeness.AllDataAvailable)" -ForegroundColor $(if ($completeness.AllDataAvailable -gt 0) { "Green" } else { "Yellow" })
    Write-Host ""
    
    if ($completeness.AllDataAvailable -eq 0) {
        Write-Host "ISSUE: No containers have all three data types (Scanner + ICUMS + Images)" -ForegroundColor Red
        Write-Host "   Containers need all three to be marked as 'Complete'" -ForegroundColor Yellow
    }
} else {
    Write-Host "WARNING: Could not retrieve completeness data" -ForegroundColor Yellow
}
Write-Host ""

# Check 3: Recent Activity
Write-Host "Check 3: Recent Activity" -ForegroundColor Yellow
Write-Host "----------------------" -ForegroundColor Yellow

$recentQuery = @"
SELECT TOP 10
    ContainerNumber,
    Status,
    HasScannerData,
    HasICUMSData,
    HasImageData,
    UpdatedAtUtc,
    DATEDIFF(minute, UpdatedAtUtc, GETUTCDATE()) AS MinutesAgo
FROM ContainerCompletenessStatuses
ORDER BY UpdatedAtUtc DESC
"@

$recent = Invoke-Sqlcmd -ServerInstance $Server -Database $Database -Query $recentQuery

if ($recent) {
    Write-Host "Most recently updated containers:" -ForegroundColor Cyan
    $recent | Format-Table -AutoSize
    Write-Host ""
    
    $mostRecent = ($recent | Select-Object -First 1).MinutesAgo
    if ($mostRecent -lt 10) {
        Write-Host "SUCCESS: Service appears active (last update $mostRecent minutes ago)" -ForegroundColor Green
    } elseif ($mostRecent -lt 60) {
        Write-Host "WARNING: Service may be slow (last update $mostRecent minutes ago)" -ForegroundColor Yellow
    } else {
        Write-Host "ISSUE: Service may not be running (last update $mostRecent minutes ago)" -ForegroundColor Red
    }
} else {
    Write-Host "WARNING: No recent activity found" -ForegroundColor Yellow
}
Write-Host ""

# Check 4: Complete Containers Ready for IntakeWorker
Write-Host "Check 4: Complete Containers Ready for IntakeWorker" -ForegroundColor Yellow
Write-Host "----------------------------------------------------" -ForegroundColor Yellow

$readyQuery = @"
SELECT 
    COUNT(*) AS CompleteCount,
    COUNT(DISTINCT GroupIdentifier) AS UniqueGroups,
    MIN(UpdatedAtUtc) AS OldestComplete,
    MAX(UpdatedAtUtc) AS NewestComplete
FROM ContainerCompletenessStatuses
WHERE Status = 'Complete'
"@

$ready = Invoke-Sqlcmd -ServerInstance $Server -Database $Database -Query $readyQuery

if ($ready -and $ready.CompleteCount -gt 0) {
    Write-Host "SUCCESS: Found $($ready.CompleteCount) complete containers" -ForegroundColor Green
    Write-Host "  - Unique Groups: $($ready.UniqueGroups)" -ForegroundColor White
    Write-Host "  - Oldest: $($ready.OldestComplete)" -ForegroundColor White
    Write-Host "  - Newest: $($ready.NewestComplete)" -ForegroundColor White
    Write-Host ""
    Write-Host "These containers should be picked up by IntakeWorker to create AnalysisGroups" -ForegroundColor Cyan
} else {
    Write-Host "ISSUE: No complete containers found" -ForegroundColor Red
    Write-Host "   IntakeWorker needs complete containers to create groups" -ForegroundColor Yellow
    Write-Host "   Check if containers have:" -ForegroundColor Yellow
    Write-Host "     1. Scanner data (FS6000Scans or AseScans)" -ForegroundColor White
    Write-Host "     2. ICUMS data (BOE documents)" -ForegroundColor White
    Write-Host "     3. Image data (HasImage = true or ScanImage != null)" -ForegroundColor White
}
Write-Host ""

# Summary
Write-Host "SUMMARY" -ForegroundColor Cyan
Write-Host "=======" -ForegroundColor Cyan
Write-Host ""

$issues = @()

if (-not $statuses -or ($statuses | Measure-Object).Count -eq 0) {
    $issues += "No ContainerCompletenessStatus records found - service may not be running"
}

if ($completeness -and $completeness.AllDataAvailable -eq 0) {
    $issues += "No containers have all three data types (Scanner + ICUMS + Images)"
}

if ($ready -and $ready.CompleteCount -eq 0) {
    $issues += "No containers with Status='Complete' - IntakeWorker has no input"
}

if ($recent) {
    $mostRecent = ($recent | Select-Object -First 1).MinutesAgo
    if ($mostRecent -gt 60) {
        $issues += "Service appears inactive (last update $mostRecent minutes ago)"
    }
}

if ($issues.Count -eq 0) {
    Write-Host "SUCCESS: ContainerCompletenessService appears to be working" -ForegroundColor Green
    Write-Host ""
    Write-Host "Next Steps:" -ForegroundColor Cyan
    Write-Host "  1. Verify IntakeWorker is running and creating AnalysisGroups" -ForegroundColor White
    Write-Host "  2. Check that AnalysisGroups have Status='Ready'" -ForegroundColor White
    Write-Host "  3. Verify AssignmentWorker is assigning groups to analysts" -ForegroundColor White
} else {
    Write-Host "ISSUES FOUND:" -ForegroundColor Red
    foreach ($issue in $issues) {
        Write-Host "  - $issue" -ForegroundColor Red
    }
    Write-Host ""
    Write-Host "Recommended Actions:" -ForegroundColor Yellow
    Write-Host "  1. Check application logs for ContainerCompletenessService errors" -ForegroundColor White
    Write-Host "  2. Verify service is registered in Program.cs" -ForegroundColor White
    Write-Host "  3. Check database connections (NS_CIS and ICUMS_Downloads)" -ForegroundColor White
    Write-Host "  4. Verify scanner data exists (FS6000Scans, AseScans)" -ForegroundColor White
    Write-Host "  5. Verify ICUMS data exists (BOEDocuments)" -ForegroundColor White
}

Write-Host ""


# ========================================
# Find Practical Example: Consolidated Cargo with Multiple BOE Records
# ========================================
# This script finds containers that have multiple BOE documents
# and shows how ContainerCompletenessStatus handles them

param(
    [string]$ContainerNumber = ""
)

# Load connection string from appsettings.json
$appsettingsPath = "src\NickScanCentralImagingPortal.API\appsettings.json"
if (Test-Path $appsettingsPath) {
    $appsettings = Get-Content $appsettingsPath | ConvertFrom-Json
    $connectionString = $appsettings.ConnectionStrings.NS_CIS_Connection
} else {
    Write-Host "❌ appsettings.json not found. Using default connection string." -ForegroundColor Red
    $connectionString = "Server=localhost;Database=NS_CIS;Trusted_Connection=true;MultipleActiveResultSets=true;Encrypt=true;TrustServerCertificate=true"
}

Write-Host "========================================" -ForegroundColor Cyan
Write-Host "Finding Consolidated Cargo Examples" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""

# Step 1: Find containers with multiple BOE records
Write-Host "STEP 1: Finding containers with multiple BOE records..." -ForegroundColor Yellow
Write-Host ""

$query1 = @"
SELECT TOP 10
    b.ContainerNumber,
    COUNT(DISTINCT b.Id) AS BOE_Count,
    COUNT(DISTINCT b.DeclarationNumber) AS Declaration_Count,
    COUNT(DISTINCT b.HouseBl) AS HouseBL_Count,
    MIN(b.CreatedAt) AS First_BOE_Downloaded,
    MAX(b.CreatedAt) AS Last_BOE_Downloaded
FROM ICUMS_Downloads.dbo.BOEDocuments b
WHERE b.IsConsolidated = 1
    AND b.ContainerNumber IS NOT NULL
    AND b.ContainerNumber != ''
GROUP BY b.ContainerNumber
HAVING COUNT(DISTINCT b.Id) > 1
ORDER BY BOE_Count DESC, Last_BOE_Downloaded DESC
"@

try {
    $results1 = Invoke-Sqlcmd -ConnectionString $connectionString -Query $query1 -ErrorAction Stop
    
    if ($results1.Count -eq 0) {
        Write-Host "⚠️  No containers found with multiple BOE records." -ForegroundColor Yellow
        Write-Host "   This might mean:" -ForegroundColor Yellow
        Write-Host "   - No consolidated cargo has been downloaded yet" -ForegroundColor Yellow
        Write-Host "   - All consolidated cargo has only one BOE per container" -ForegroundColor Yellow
        exit
    }
    
    Write-Host "Found $($results1.Count) container(s) with multiple BOE records:" -ForegroundColor Green
    Write-Host ""
    
    $results1 | Format-Table -AutoSize
    
    # Use first container if not specified
    if ([string]::IsNullOrEmpty($ContainerNumber)) {
        $ContainerNumber = $results1[0].ContainerNumber
        Write-Host ""
        Write-Host "Using first container: $ContainerNumber" -ForegroundColor Cyan
    }
} catch {
    Write-Host "❌ Error finding containers: $_" -ForegroundColor Red
    exit
}

Write-Host ""
Write-Host "========================================" -ForegroundColor Cyan
Write-Host "STEP 2: Detailed BOE Records for $ContainerNumber" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""

$query2 = @"
SELECT 
    b.Id AS BOE_Id,
    b.ContainerNumber,
    b.DeclarationNumber,
    b.HouseBl AS HouseBL,
    b.BlNumber AS MasterBL,
    b.IsConsolidated,
    b.ConsigneeName,
    b.CrmsLevel,
    b.ClearanceType,
    b.CreatedAt AS BOE_CreatedAt
FROM ICUMS_Downloads.dbo.BOEDocuments b
WHERE b.ContainerNumber = '$ContainerNumber'
ORDER BY b.CreatedAt ASC
"@

try {
    $results2 = Invoke-Sqlcmd -ConnectionString $connectionString -Query $query2 -ErrorAction Stop
    
    Write-Host "Found $($results2.Count) BOE record(s) for container ${ContainerNumber}:" -ForegroundColor Green
    Write-Host ""
    
    $results2 | Format-Table -AutoSize
    
    Write-Host ""
    Write-Host "📊 Summary:" -ForegroundColor Cyan
    Write-Host "   - First BOE downloaded: $($results2[0].BOE_CreatedAt)" -ForegroundColor White
    Write-Host "   - Last BOE downloaded: $($results2[-1].BOE_CreatedAt)" -ForegroundColor White
    Write-Host "   - Total House BLs: $($results2.Count)" -ForegroundColor White
} catch {
    Write-Host "❌ Error getting BOE records: $_" -ForegroundColor Red
    exit
}

Write-Host ""
Write-Host "========================================" -ForegroundColor Cyan
Write-Host "STEP 3: ContainerCompletenessStatus Record" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""

$query3 = @"
SELECT 
    c.Id,
    c.ContainerNumber,
    c.ScannerType,
    c.GroupIdentifier,
    c.IsConsolidated,
    c.TotalHouseBLs,
    c.CompleteHouseBLs,
    c.BOEDocumentId AS Primary_BOE_Id,
    c.HasICUMSData,
    c.HasImageData,
    c.HasScannerData,
    c.Status,
    c.WorkflowStage,
    c.CreatedAt AS Status_CreatedAt,
    c.UpdatedAt AS Status_UpdatedAt
FROM ContainerCompletenessStatuses c
WHERE c.ContainerNumber = '${ContainerNumber}'
ORDER BY c.ScannerType
"@

try {
    $results3 = Invoke-Sqlcmd -ConnectionString $connectionString -Query $query3 -ErrorAction Stop
    
    if ($results3.Count -eq 0) {
        Write-Host "WARNING: No ContainerCompletenessStatus record found for ${ContainerNumber}" -ForegroundColor Yellow
        Write-Host "   This might mean the container has not been processed yet." -ForegroundColor Yellow
    } else {
        Write-Host "Found $($results3.Count) ContainerCompletenessStatus record(s):" -ForegroundColor Green
        Write-Host ""
        
        $results3 | Format-Table -AutoSize
        
        Write-Host ""
        Write-Host "✅ Key Verification Points:" -ForegroundColor Cyan
        foreach ($status in $results3) {
            Write-Host "   - GroupIdentifier: $($status.GroupIdentifier)" -ForegroundColor White
            Write-Host "   - IsConsolidated: $($status.IsConsolidated)" -ForegroundColor White
            Write-Host "   - TotalHouseBLs: $($status.TotalHouseBLs)" -ForegroundColor White
            Write-Host "   - Primary BOE Id: $($status.Primary_BOE_Id)" -ForegroundColor White
            Write-Host "   - Status: $($status.Status)" -ForegroundColor White
            Write-Host "   - WorkflowStage: $($status.WorkflowStage)" -ForegroundColor White
        }
    }
} catch {
    Write-Host "❌ Error getting ContainerCompletenessStatus: $_" -ForegroundColor Red
}

Write-Host ""
Write-Host "========================================" -ForegroundColor Cyan
Write-Host "STEP 4: Verify Primary BOE is Most Recent" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""

$query4 = @"
SELECT 
    'ContainerCompletenessStatus Primary BOE' AS Source,
    c.BOEDocumentId AS BOE_Id,
    b.DeclarationNumber,
    b.HouseBl AS HouseBL,
    b.CreatedAt AS BOE_CreatedAt
FROM ContainerCompletenessStatuses c
INNER JOIN ICUMS_Downloads.dbo.BOEDocuments b ON c.BOEDocumentId = b.Id
WHERE c.ContainerNumber = '${ContainerNumber}'
    AND c.ScannerType = (SELECT TOP 1 ScannerType FROM ContainerCompletenessStatuses WHERE ContainerNumber = '${ContainerNumber}' ORDER BY CreatedAt DESC)

UNION ALL

SELECT 
    'Most Recent BOE (by CreatedAt)' AS Source,
    b.Id AS BOE_Id,
    b.DeclarationNumber,
    b.HouseBl AS HouseBL,
    b.CreatedAt AS BOE_CreatedAt
FROM ICUMS_Downloads.dbo.BOEDocuments b
WHERE b.ContainerNumber = '${ContainerNumber}'
    AND b.CreatedAt = (
        SELECT MAX(CreatedAt) 
        FROM ICUMS_Downloads.dbo.BOEDocuments 
        WHERE ContainerNumber = '${ContainerNumber}'
    )
"@

try {
    $results4 = Invoke-Sqlcmd -ConnectionString $connectionString -Query $query4 -ErrorAction Stop
    
    Write-Host "BOE Comparison:" -ForegroundColor Green
    Write-Host ""
    
    $results4 | Format-Table -AutoSize
    
    if ($results4.Count -eq 2) {
        $primary = $results4 | Where-Object { $_.Source -like "*ContainerCompletenessStatus*" }
        $mostRecent = $results4 | Where-Object { $_.Source -like "*Most Recent*" }
        
        if ($primary.BOE_Id -eq $mostRecent.BOE_Id) {
            Write-Host ""
            Write-Host "✅ VERIFIED: Primary BOE matches most recent BOE!" -ForegroundColor Green
        } else {
            Write-Host ""
            Write-Host "WARNING: Primary BOE does NOT match most recent BOE!" -ForegroundColor Yellow
            Write-Host "   Primary BOE Id: $($primary.BOE_Id)" -ForegroundColor Yellow
            Write-Host "   Most Recent BOE Id: $($mostRecent.BOE_Id)" -ForegroundColor Yellow
        }
    }
} catch {
    Write-Host "❌ Error comparing BOE records: $_" -ForegroundColor Red
}

Write-Host ""
Write-Host "========================================" -ForegroundColor Cyan
Write-Host "INVESTIGATION COMPLETE" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""
Write-Host "Summary:" -ForegroundColor Yellow
Write-Host "  Container: ${ContainerNumber}" -ForegroundColor White
Write-Host "  BOE Records: $($results2.Count)" -ForegroundColor White
Write-Host "  ContainerCompletenessStatus Records: $($results3.Count)" -ForegroundColor White
Write-Host ""
Write-Host "Expected Behavior:" -ForegroundColor Yellow
Write-Host "  ✅ ONE ContainerCompletenessStatus record exists" -ForegroundColor Green
Write-Host "  ✅ GroupIdentifier = ContainerNumber (for consolidated)" -ForegroundColor Green
Write-Host "  ✅ Primary BOE = Most Recent BOE" -ForegroundColor Green
Write-Host "  ✅ All House BLs included in ConsolidationDetails" -ForegroundColor Green
Write-Host ""


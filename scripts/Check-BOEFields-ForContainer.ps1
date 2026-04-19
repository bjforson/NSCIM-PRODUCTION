# Check BOE fields for a specific container to identify potential fallback identifiers
# Usage: .\Check-BOEFields-ForContainer.ps1 -ContainerNumber "HLXU8234732"

param(
    [Parameter(Mandatory=$true)]
    [string]$ContainerNumber
)

$ErrorActionPreference = "Stop"

# Database connection string (adjust as needed)
$connectionString = "Server=127.0.0.1,1433;Database=ICUMS_Downloads;Integrated Security=True;TrustServerCertificate=True;"

Write-Host "Checking BOE fields for container: $ContainerNumber" -ForegroundColor Cyan
Write-Host ""

try {
    $connection = New-Object System.Data.SqlClient.SqlConnection($connectionString)
    $connection.Open()
    
    # Query to get all fields from BOEDocuments for the container
    $query = @"
SELECT 
    Id,
    ContainerNumber,
    DeclarationNumber,
    BlNumber,
    HouseBl,
    RotationNumber,
    ClearanceType,
    IsConsolidated,
    ConsigneeName,
    ShipperName,
    ImpName,
    ExpName,
    DeclarantName,
    ContainerISO,
    ContainerQuantity,
    ContainerWeight,
    TotalDutyPaid,
    CountryOfOrigin,
    GoodsDescription,
    DeliveryPlace,
    MarksNumbers,
    DeclarationDate,
    RegimeCode,
    -- Check if RawJsonData exists and contains grouping fields
    CASE 
        WHEN RawJsonData IS NOT NULL AND LEN(RawJsonData) > 0 THEN 1 
        ELSE 0 
    END AS HasRawJsonData,
    CASE 
        WHEN RawJsonData LIKE '%"BL Number"%' OR RawJsonData LIKE '%"BlNumber"%' OR RawJsonData LIKE '%"Master BL"%' THEN 1 
        ELSE 0 
    END AS RawJsonHasBlNumber,
    CASE 
        WHEN RawJsonData LIKE '%"Declaration Number"%' OR RawJsonData LIKE '%"DeclarationNumber"%' THEN 1 
        ELSE 0 
    END AS RawJsonHasDeclarationNumber,
    CreatedAt,
    UpdatedAt
FROM BOEDocuments
WHERE ContainerNumber = @ContainerNumber
ORDER BY CreatedAt DESC
"@
    
    $command = New-Object System.Data.SqlClient.SqlCommand($query, $connection)
    $command.Parameters.AddWithValue("@ContainerNumber", $ContainerNumber.ToUpper()) | Out-Null
    
    $adapter = New-Object System.Data.SqlClient.SqlDataAdapter($command)
    $dataset = New-Object System.Data.DataSet
    $adapter.Fill($dataset) | Out-Null
    
    $results = $dataset.Tables[0]
    
    if ($results.Rows.Count -eq 0) {
        Write-Host "❌ No BOE document found for container: $ContainerNumber" -ForegroundColor Red
        Write-Host ""
        Write-Host "Checking if container exists in other tables..." -ForegroundColor Yellow
        
        # Check if container exists in AnalysisRecords
        $checkQuery = @"
SELECT TOP 1 ContainerNumber, GroupIdentifier, Status
FROM AnalysisRecords
WHERE ContainerNumber = @ContainerNumber
"@
        $checkCommand = New-Object System.Data.SqlClient.SqlCommand($checkQuery, $connection)
        $checkCommand.Parameters.AddWithValue("@ContainerNumber", $ContainerNumber.ToUpper()) | Out-Null
        $checkAdapter = New-Object System.Data.SqlClient.SqlDataAdapter($checkCommand)
        $checkDataset = New-Object System.Data.DataSet
        $checkAdapter.Fill($checkDataset) | Out-Null
        
        if ($checkDataset.Tables[0].Rows.Count -gt 0) {
            $row = $checkDataset.Tables[0].Rows[0]
            Write-Host "✅ Container found in AnalysisRecords:" -ForegroundColor Green
            Write-Host "   GroupIdentifier: $($row['GroupIdentifier'])" -ForegroundColor White
            Write-Host "   Status: $($row['Status'])" -ForegroundColor White
        }
    } else {
        Write-Host "✅ Found $($results.Rows.Count) BOE document(s) for container: $ContainerNumber" -ForegroundColor Green
        Write-Host ""
        
        foreach ($row in $results.Rows) {
            Write-Host "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━" -ForegroundColor DarkGray
            Write-Host "BOE Document ID: $($row['Id'])" -ForegroundColor Cyan
            Write-Host "Created: $($row['CreatedAt'])" -ForegroundColor Gray
            Write-Host ""
            
            # Primary grouping fields
            Write-Host "PRIMARY GROUPING FIELDS:" -ForegroundColor Yellow
            $blNumber = if ([DBNull]::Value.Equals($row['BlNumber'])) { $null } else { $row['BlNumber'] }
            $declarationNumber = if ([DBNull]::Value.Equals($row['DeclarationNumber'])) { $null } else { $row['DeclarationNumber'] }
            
            if ([string]::IsNullOrWhiteSpace($blNumber)) {
                Write-Host "  ❌ BlNumber: NULL/Empty" -ForegroundColor Red
            } else {
                Write-Host "  ✅ BlNumber: $blNumber" -ForegroundColor Green
            }
            
            if ([string]::IsNullOrWhiteSpace($declarationNumber)) {
                Write-Host "  ❌ DeclarationNumber: NULL/Empty" -ForegroundColor Red
            } else {
                Write-Host "  ✅ DeclarationNumber: $declarationNumber" -ForegroundColor Green
            }
            
            Write-Host ""
            Write-Host "CONSOLIDATION STATUS:" -ForegroundColor Yellow
            $isConsolidated = if ([DBNull]::Value.Equals($row['IsConsolidated'])) { $false } else { $row['IsConsolidated'] }
            Write-Host "  IsConsolidated: $isConsolidated" -ForegroundColor $(if ($isConsolidated) { "Cyan" } else { "Gray" })
            
            $houseBl = if ([DBNull]::Value.Equals($row['HouseBl'])) { $null } else { $row['HouseBl'] }
            if (![string]::IsNullOrWhiteSpace($houseBl)) {
                Write-Host "  HouseBl: $houseBl" -ForegroundColor Gray
            }
            
            Write-Host ""
            Write-Host "POTENTIAL FALLBACK FIELDS:" -ForegroundColor Yellow
            
            # Check RotationNumber
            $rotationNumber = if ([DBNull]::Value.Equals($row['RotationNumber'])) { $null } else { $row['RotationNumber'] }
            if (![string]::IsNullOrWhiteSpace($rotationNumber)) {
                Write-Host "  ✅ RotationNumber: $rotationNumber" -ForegroundColor Green
            } else {
                Write-Host "  ❌ RotationNumber: NULL/Empty" -ForegroundColor Gray
            }
            
            # Check other potentially useful fields
            $clearanceType = if ([DBNull]::Value.Equals($row['ClearanceType'])) { $null } else { $row['ClearanceType'] }
            if (![string]::IsNullOrWhiteSpace($clearanceType)) {
                Write-Host "  ✅ ClearanceType: $clearanceType" -ForegroundColor Green
            }
            
            $consigneeName = if ([DBNull]::Value.Equals($row['ConsigneeName'])) { $null } else { $row['ConsigneeName'] }
            if (![string]::IsNullOrWhiteSpace($consigneeName)) {
                Write-Host "  ✅ ConsigneeName: $consigneeName" -ForegroundColor Green
            }
            
            $shipperName = if ([DBNull]::Value.Equals($row['ShipperName'])) { $null } else { $row['ShipperName'] }
            if (![string]::IsNullOrWhiteSpace($shipperName)) {
                Write-Host "  ✅ ShipperName: $shipperName" -ForegroundColor Green
            }
            
            Write-Host ""
            Write-Host "RAW JSON DATA CHECK:" -ForegroundColor Yellow
            $hasRawJson = $row['HasRawJsonData'] -eq 1
            $rawJsonHasBl = $row['RawJsonHasBlNumber'] -eq 1
            $rawJsonHasDecl = $row['RawJsonHasDeclarationNumber'] -eq 1
            
            if ($hasRawJson) {
                Write-Host "  ✅ RawJsonData exists" -ForegroundColor Green
                if ($rawJsonHasBl) {
                    Write-Host "  ✅ RawJsonData contains BL Number field" -ForegroundColor Green
                } else {
                    Write-Host "  ❌ RawJsonData does NOT contain BL Number field" -ForegroundColor Red
                }
                if ($rawJsonHasDecl) {
                    Write-Host "  ✅ RawJsonData contains Declaration Number field" -ForegroundColor Green
                } else {
                    Write-Host "  ❌ RawJsonData does NOT contain Declaration Number field" -ForegroundColor Red
                }
            } else {
                Write-Host "  ❌ RawJsonData is NULL/Empty" -ForegroundColor Red
            }
            
            Write-Host ""
            Write-Host "RECOMMENDATION:" -ForegroundColor Magenta
            if (![string]::IsNullOrWhiteSpace($blNumber) -or ![string]::IsNullOrWhiteSpace($declarationNumber)) {
                Write-Host "  ✅ Container has valid grouping identifier" -ForegroundColor Green
            } elseif ($rawJsonHasBl -or $rawJsonHasDecl) {
                Write-Host "  ⚠️  Grouping identifier exists in RawJsonData but not in columns" -ForegroundColor Yellow
                Write-Host "     → Consider extracting from RawJsonData as fallback" -ForegroundColor Yellow
            } elseif (![string]::IsNullOrWhiteSpace($rotationNumber)) {
                Write-Host "  ⚠️  Could use RotationNumber as fallback, but this is NOT standard" -ForegroundColor Yellow
                Write-Host "     → RotationNumber may group containers by rotation, not by cargo" -ForegroundColor Yellow
            } else {
                Write-Host "  ❌ No suitable grouping identifier found" -ForegroundColor Red
                Write-Host "     → This is a data quality issue - BOE document is missing required fields" -ForegroundColor Red
            }
            
            Write-Host ""
        }
    }
    
    $connection.Close()
    
} catch {
    Write-Host "❌ Error: $($_.Exception.Message)" -ForegroundColor Red
    Write-Host $_.ScriptStackTrace -ForegroundColor Red
    exit 1
}

Write-Host ""
Write-Host "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━" -ForegroundColor DarkGray
Write-Host "Analysis complete!" -ForegroundColor Green


# Find containers that are missing both BlNumber and DeclarationNumber
# This helps identify data quality issues

$ErrorActionPreference = "Stop"

# Database connection string
$connectionString = "Server=127.0.0.1,1433;Database=ICUMS_Downloads;Integrated Security=True;TrustServerCertificate=True;"

Write-Host "Finding containers missing both BlNumber and DeclarationNumber..." -ForegroundColor Cyan
Write-Host ""

try {
    $connection = New-Object System.Data.SqlClient.SqlConnection($connectionString)
    $connection.Open()
    
    # Query to find containers missing both grouping fields
    $query = @"
SELECT TOP 100
    ContainerNumber,
    DeclarationNumber,
    BlNumber,
    HouseBl,
    RotationNumber,
    ClearanceType,
    IsConsolidated,
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
    CreatedAt
FROM BOEDocuments
WHERE (BlNumber IS NULL OR BlNumber = '')
  AND (DeclarationNumber IS NULL OR DeclarationNumber = '')
ORDER BY CreatedAt DESC
"@
    
    $command = New-Object System.Data.SqlClient.SqlCommand($query, $connection)
    $adapter = New-Object System.Data.SqlClient.SqlDataAdapter($command)
    $dataset = New-Object System.Data.DataSet
    $adapter.Fill($dataset) | Out-Null
    
    $results = $dataset.Tables[0]
    
    if ($results.Rows.Count -eq 0) {
        Write-Host "✅ No containers found missing both BlNumber and DeclarationNumber" -ForegroundColor Green
    } else {
        Write-Host "⚠️  Found $($results.Rows.Count) containers missing both grouping fields:" -ForegroundColor Yellow
        Write-Host ""
        
        $count = 0
        foreach ($row in $results.Rows) {
            $count++
            Write-Host "[$count] Container: $($row['ContainerNumber'])" -ForegroundColor Cyan
            Write-Host "    ClearanceType: $($row['ClearanceType'])" -ForegroundColor Gray
            Write-Host "    IsConsolidated: $($row['IsConsolidated'])" -ForegroundColor Gray
            Write-Host "    RotationNumber: $($row['RotationNumber'])" -ForegroundColor Gray
            Write-Host "    HouseBl: $($row['HouseBl'])" -ForegroundColor Gray
            
            $hasRawJson = $row['HasRawJsonData'] -eq 1
            $rawJsonHasBl = $row['RawJsonHasBlNumber'] -eq 1
            $rawJsonHasDecl = $row['RawJsonHasDeclarationNumber'] -eq 1
            
            if ($hasRawJson) {
                Write-Host "    RawJsonData: EXISTS" -ForegroundColor Green
                if ($rawJsonHasBl) {
                    Write-Host "      → Contains BL Number field" -ForegroundColor Green
                }
                if ($rawJsonHasDecl) {
                    Write-Host "      → Contains Declaration Number field" -ForegroundColor Green
                }
            } else {
                Write-Host "    RawJsonData: NULL/Empty" -ForegroundColor Red
            }
            
            Write-Host "    Created: $($row['CreatedAt'])" -ForegroundColor DarkGray
            Write-Host ""
        }
        
        Write-Host "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━" -ForegroundColor DarkGray
        Write-Host ""
        Write-Host "RECOMMENDATIONS:" -ForegroundColor Magenta
        Write-Host "1. Check if RawJsonData contains grouping fields that can be extracted" -ForegroundColor Yellow
        Write-Host "2. Consider using RotationNumber as a fallback (NOT standard, may group incorrectly)" -ForegroundColor Yellow
        Write-Host "3. This is a data quality issue - BOE documents should have BlNumber or DeclarationNumber" -ForegroundColor Red
    }
    
    $connection.Close()
    
} catch {
    Write-Host "❌ Error: $($_.Exception.Message)" -ForegroundColor Red
    exit 1
}

Write-Host "Analysis complete!" -ForegroundColor Green


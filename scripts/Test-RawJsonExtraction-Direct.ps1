# Test RawJsonData extraction directly from database
# This simulates what the API code does

param(
    [Parameter(Mandatory=$true)]
    [string]$ContainerNumber
)

$ErrorActionPreference = "Stop"

# Database connection string
$connectionString = "Server=127.0.0.1,1433;Database=ICUMS_Downloads;Integrated Security=True;TrustServerCertificate=True;"

Write-Host "Testing RawJsonData extraction for container: $ContainerNumber" -ForegroundColor Cyan
Write-Host ""

try {
    $connection = New-Object System.Data.SqlClient.SqlConnection($connectionString)
    $connection.Open()
    
    # Get BOE document with RawJsonData
    $query = @"
SELECT TOP 1
    Id,
    ContainerNumber,
    BlNumber,
    DeclarationNumber,
    IsConsolidated,
    RawJsonData
FROM BOEDocuments
WHERE ContainerNumber = @ContainerNumber
  AND RawJsonData IS NOT NULL
  AND LEN(RawJsonData) > 0
ORDER BY CreatedAt DESC
"@
    
    $command = New-Object System.Data.SqlClient.SqlCommand($query, $connection)
    $command.Parameters.AddWithValue("@ContainerNumber", $ContainerNumber.ToUpper()) | Out-Null
    
    $reader = $command.ExecuteReader()
    
    if ($reader.Read()) {
        $boeId = $reader["Id"]
        $dbBlNumber = if ([DBNull]::Value.Equals($reader["BlNumber"])) { $null } else { $reader["BlNumber"] }
        $dbDeclarationNumber = if ([DBNull]::Value.Equals($reader["DeclarationNumber"])) { $null } else { $reader["DeclarationNumber"] }
        $isConsolidated = $reader["IsConsolidated"]
        $rawJsonData = $reader["RawJsonData"].ToString()
        
        Write-Host "✅ Found BOE Document ID: $boeId" -ForegroundColor Green
        Write-Host "   Database BlNumber: $(if ($dbBlNumber) { $dbBlNumber } else { 'NULL' })" -ForegroundColor $(if ($dbBlNumber) { "Green" } else { "Red" })
        Write-Host "   Database DeclarationNumber: $(if ($dbDeclarationNumber) { $dbDeclarationNumber } else { 'NULL' })" -ForegroundColor $(if ($dbDeclarationNumber) { "Green" } else { "Red" })
        Write-Host "   IsConsolidated: $isConsolidated" -ForegroundColor White
        Write-Host "   RawJsonData Length: $($rawJsonData.Length) characters" -ForegroundColor White
        Write-Host ""
        
        # Test extraction (simulate the C# code logic)
        Write-Host "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━" -ForegroundColor DarkGray
        Write-Host "EXTRACTING FROM RAWJSONDATA:" -ForegroundColor Yellow
        Write-Host ""
        
        try {
            # Parse JSON
            $jsonObj = $rawJsonData | ConvertFrom-Json
            
            $extractedBlNumber = $null
            $extractedDeclarationNumber = $null
            
            # Try to find BL Number - check root level first
            $blFieldNames = @("BL Number", "BlNumber", "blNumber", "Master BL", "masterBL", "BLNumber")
            foreach ($fieldName in $blFieldNames) {
                if ($jsonObj.PSObject.Properties.Name -contains $fieldName) {
                    $extractedBlNumber = $jsonObj.$fieldName
                    if ($extractedBlNumber) {
                        Write-Host "✅ Found BlNumber in root field '$fieldName': $extractedBlNumber" -ForegroundColor Green
                        break
                    }
                }
            }
            
            # Check nested ManifestDetails section
            if (-not $extractedBlNumber -and $jsonObj.PSObject.Properties.Name -contains "ManifestDetails") {
                $manifestDetails = $jsonObj.ManifestDetails
                foreach ($fieldName in $blFieldNames) {
                    if ($manifestDetails.PSObject.Properties.Name -contains $fieldName) {
                        $extractedBlNumber = $manifestDetails.$fieldName
                        if ($extractedBlNumber) {
                            Write-Host "✅ Found BlNumber in ManifestDetails.${fieldName}: $extractedBlNumber" -ForegroundColor Green
                            break
                        }
                    }
                }
            }
            
            if (-not $extractedBlNumber) {
                Write-Host "❌ BlNumber not found in RawJsonData" -ForegroundColor Red
            }
            
            # Try to find Declaration Number - check root level first
            $declFieldNames = @("Declaration Number", "DeclarationNumber", "declarationNumber", "Declaration")
            foreach ($fieldName in $declFieldNames) {
                if ($jsonObj.PSObject.Properties.Name -contains $fieldName) {
                    $extractedDeclarationNumber = $jsonObj.$fieldName
                    if ($extractedDeclarationNumber) {
                        Write-Host "✅ Found DeclarationNumber in root field '$fieldName': $extractedDeclarationNumber" -ForegroundColor Green
                        break
                    }
                }
            }
            
            # Check nested Header section
            if (-not $extractedDeclarationNumber -and $jsonObj.PSObject.Properties.Name -contains "Header") {
                $header = $jsonObj.Header
                foreach ($fieldName in $declFieldNames) {
                    if ($header.PSObject.Properties.Name -contains $fieldName) {
                        $extractedDeclarationNumber = $header.$fieldName
                        if ($extractedDeclarationNumber) {
                            Write-Host "✅ Found DeclarationNumber in Header.${fieldName}: $extractedDeclarationNumber" -ForegroundColor Green
                            break
                        }
                    }
                }
            }
            
            if (-not $extractedDeclarationNumber) {
                Write-Host "❌ DeclarationNumber not found in RawJsonData" -ForegroundColor Red
            }
            
            Write-Host ""
            Write-Host "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━" -ForegroundColor DarkGray
            Write-Host "EXTRACTION RESULTS:" -ForegroundColor Yellow
            Write-Host ""
            
            if ($extractedBlNumber -or $extractedDeclarationNumber) {
                Write-Host "✅ SUCCESS: Extraction worked!" -ForegroundColor Green
                Write-Host ""
                
                if ($extractedBlNumber -and -not $dbBlNumber) {
                    Write-Host "   ✅ BlNumber extracted from RawJsonData: $extractedBlNumber" -ForegroundColor Green
                    Write-Host "      (was NULL in database)" -ForegroundColor Gray
                }
                
                if ($extractedDeclarationNumber -and -not $dbDeclarationNumber) {
                    Write-Host "   ✅ DeclarationNumber extracted from RawJsonData: $extractedDeclarationNumber" -ForegroundColor Green
                    Write-Host "      (was NULL in database)" -ForegroundColor Gray
                }
                
                Write-Host ""
                Write-Host "✅ The API fallback logic should work for this container!" -ForegroundColor Green
            }
            else {
                Write-Host "❌ FAILED: Could not extract grouping fields from RawJsonData" -ForegroundColor Red
                Write-Host "   The JSON structure may be different than expected" -ForegroundColor Yellow
            }
            
            # Show sample of JSON structure
            Write-Host ""
            Write-Host "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━" -ForegroundColor DarkGray
            Write-Host "JSON STRUCTURE SAMPLE (first 10 properties):" -ForegroundColor Yellow
            Write-Host ""
            $propertyCount = 0
            foreach ($prop in $jsonObj.PSObject.Properties) {
                if ($propertyCount -lt 10) {
                    Write-Host "   $($prop.Name): $($prop.Value)" -ForegroundColor Gray
                    $propertyCount++
                }
            }
            if ($jsonObj.PSObject.Properties.Count -gt 10) {
                Write-Host "   ... ($($jsonObj.PSObject.Properties.Count - 10) more properties)" -ForegroundColor DarkGray
            }
            
        }
        catch {
            Write-Host "❌ ERROR parsing JSON: $($_.Exception.Message)" -ForegroundColor Red
            Write-Host "   RawJsonData may be malformed" -ForegroundColor Yellow
        }
    }
    else {
        Write-Host "❌ No BOE document found with RawJsonData for container: $ContainerNumber" -ForegroundColor Red
    }
    
    $reader.Close()
    $connection.Close()
    
} catch {
    Write-Host "❌ Error: $($_.Exception.Message)" -ForegroundColor Red
    exit 1
}

Write-Host ""
Write-Host "Test complete!" -ForegroundColor Green


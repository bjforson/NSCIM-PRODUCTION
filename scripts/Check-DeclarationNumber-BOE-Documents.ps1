# Check if there are other BOE documents with the same Declaration Number
# Container: MRSU5186846, Declaration: 80925573482

$declarationNumber = "80925573482"
$containerNumber = "MRSU5186846"
$connectionString = "Server=localhost;Database=ICUMS_Downloads;Integrated Security=true;TrustServerCertificate=true;"

Write-Host "========================================" -ForegroundColor Cyan
Write-Host "Checking BOE Documents by Declaration Number" -ForegroundColor Cyan
Write-Host "Declaration: $declarationNumber" -ForegroundColor Cyan
Write-Host "Container: $containerNumber" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""

try {
    $connection = New-Object System.Data.SqlClient.SqlConnection($connectionString)
    $connection.Open()
    Write-Host "Connected to database" -ForegroundColor Green
    Write-Host ""
    
    # Get all BOE documents with this declaration number
    $query = @"
        SELECT 
            Id,
            ContainerNumber,
            DeclarationNumber,
            BlNumber,
            HouseBl,
            GoodsDescription,
            IsConsolidated,
            ClearanceType,
            ProcessingStatus,
            CreatedAt,
            CASE 
                WHEN RawJsonData IS NULL OR RawJsonData = '' THEN 'No'
                ELSE 'Yes'
            END AS HasRawJsonData
        FROM BOEDocuments
        WHERE DeclarationNumber = '$declarationNumber'
        ORDER BY CreatedAt DESC
"@
    
    Write-Host "Step 1: Finding all BOE documents with Declaration Number $declarationNumber..." -ForegroundColor Yellow
    $cmd = New-Object System.Data.SqlClient.SqlCommand($query, $connection)
    $adapter = New-Object System.Data.SqlClient.SqlDataAdapter($cmd)
    $dataset = New-Object System.Data.DataSet
    $adapter.Fill($dataset) | Out-Null
    $boeDocuments = $dataset.Tables[0]
    
    Write-Host "   Found $($boeDocuments.Rows.Count) BOE document(s) with Declaration Number ${declarationNumber}:" -ForegroundColor Green
    Write-Host ""
    
    foreach ($row in $boeDocuments.Rows) {
        $boeId = $row["Id"]
        $goodsDesc = if ([string]::IsNullOrWhiteSpace($row["GoodsDescription"])) { "NULL/EMPTY" } else { $row["GoodsDescription"] }
        $goodsDescPreview = if ($goodsDesc.Length -gt 80) { $goodsDesc.Substring(0, 80) + "..." } else { $goodsDesc }
        
        $isTargetContainer = if ($row["ContainerNumber"] -eq $containerNumber) { " <<< TARGET CONTAINER" } else { "" }
        
        Write-Host "   BOE ID: $boeId$isTargetContainer" -ForegroundColor $(if ($isTargetContainer) { "Yellow" } else { "White" })
        Write-Host "      Container: $($row['ContainerNumber'])"
        Write-Host "      Declaration: $($row['DeclarationNumber'])"
        Write-Host "      BL Number: $($row['BlNumber'])"
        Write-Host "      House BL: $($row['HouseBl'])"
        Write-Host "      Goods Description: $goodsDescPreview"
        Write-Host "      Is Consolidated: $($row['IsConsolidated'])"
        Write-Host "      Clearance Type: $($row['ClearanceType'])"
        Write-Host "      Created At: $($row['CreatedAt'])"
        Write-Host "      Has RawJsonData: $($row['HasRawJsonData'])"
        Write-Host ""
    }
    
    # Check ManifestItems for each BOE
    Write-Host "Step 2: Checking ManifestItems for each BOE document..." -ForegroundColor Yellow
    Write-Host ""
    
    foreach ($row in $boeDocuments.Rows) {
        $boeId = $row["Id"]
        $container = $row["ContainerNumber"]
        
        $queryItems = @"
            SELECT 
                COUNT(*) as ItemCount,
                COUNT(CASE WHEN Description IS NOT NULL AND Description != '' THEN 1 END) as ItemsWithDescription
            FROM ManifestItems
            WHERE BOEDocumentId = $boeId
"@
        
        $cmd = New-Object System.Data.SqlClient.SqlCommand($queryItems, $connection)
        $adapter = New-Object System.Data.SqlClient.SqlDataAdapter($cmd)
        $dataset = New-Object System.Data.DataSet
        $adapter.Fill($dataset) | Out-Null
        $itemStats = $dataset.Tables[0].Rows[0]
        
        $isTargetContainer = if ($container -eq $containerNumber) { " <<< TARGET" } else { "" }
        
        Write-Host "   BOE ID: $boeId (Container: $container)$isTargetContainer" -ForegroundColor $(if ($isTargetContainer) { "Yellow" } else { "White" })
        Write-Host "      Total ManifestItems: $($itemStats['ItemCount'])"
        Write-Host "      Items with Description: $($itemStats['ItemsWithDescription'])"
        Write-Host ""
    }
    
    # Analysis
    Write-Host "Step 3: Analysis..." -ForegroundColor Yellow
    Write-Host ""
    
    $targetBoe = $boeDocuments.Rows | Where-Object { $_["ContainerNumber"] -eq $containerNumber } | Select-Object -First 1
    $otherBoes = $boeDocuments.Rows | Where-Object { $_["ContainerNumber"] -ne $containerNumber }
    
    if ($targetBoe) {
        $targetGoodsDesc = if ([string]::IsNullOrWhiteSpace($targetBoe["GoodsDescription"])) { "" } else { $targetBoe["GoodsDescription"] }
        
        Write-Host "   Target Container ($containerNumber) BOE:" -ForegroundColor Cyan
        Write-Host "      BOE ID: $($targetBoe['Id'])"
        Write-Host "      Goods Description: $(if ([string]::IsNullOrWhiteSpace($targetGoodsDesc)) { 'NULL/EMPTY' } else { $targetGoodsDesc })"
        Write-Host ""
        
        if ($otherBoes.Count -gt 0) {
            Write-Host "   Other Containers with Same Declaration Number:" -ForegroundColor Cyan
            foreach ($otherBoe in $otherBoes) {
                $otherGoodsDesc = if ([string]::IsNullOrWhiteSpace($otherBoe["GoodsDescription"])) { "" } else { $otherBoe["GoodsDescription"] }
                
                Write-Host "      Container: $($otherBoe['ContainerNumber']), BOE ID: $($otherBoe['Id'])"
                Write-Host "      Goods Description: $(if ([string]::IsNullOrWhiteSpace($otherGoodsDesc)) { 'NULL/EMPTY' } else { $otherGoodsDesc })"
                
                if (![string]::IsNullOrWhiteSpace($otherGoodsDesc) -and [string]::IsNullOrWhiteSpace($targetGoodsDesc)) {
                    Write-Host "      WARNING: This BOE has Goods Description but target container does not!" -ForegroundColor Yellow
                } elseif (![string]::IsNullOrWhiteSpace($otherGoodsDesc) -and ![string]::IsNullOrWhiteSpace($targetGoodsDesc) -and $otherGoodsDesc -ne $targetGoodsDesc) {
                    Write-Host "      WARNING: This BOE has DIFFERENT Goods Description than target container!" -ForegroundColor Yellow
                }
                Write-Host ""
            }
            
            Write-Host "   CONCLUSION:" -ForegroundColor Cyan
            Write-Host "      If Goods Description is shown for $containerNumber but is NULL in its BOE," -ForegroundColor White
            Write-Host "      it might be coming from another BOE document with the same Declaration Number." -ForegroundColor White
            Write-Host ""
        }
    }
    
} catch {
    Write-Host "Error: $($_.Exception.Message)" -ForegroundColor Red
    Write-Host $_.ScriptStackTrace
} finally {
    if ($connection.State -eq 'Open') {
        $connection.Close()
        Write-Host "Connection closed." -ForegroundColor Gray
    }
}


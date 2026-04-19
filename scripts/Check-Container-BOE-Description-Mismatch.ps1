# Diagnostic script to check if Goods Description and Item Descriptions come from different BOE documents
# Container: MRSU5186846

$containerNumber = "MRSU5186846"
$connectionString = "Server=localhost;Database=ICUMS_Downloads;Integrated Security=true;TrustServerCertificate=true;"

Write-Host "========================================" -ForegroundColor Cyan
Write-Host "BOE Document Description Mismatch Check" -ForegroundColor Cyan
Write-Host "Container: $containerNumber" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""

try {
    $connection = New-Object System.Data.SqlClient.SqlConnection($connectionString)
    $connection.Open()
    Write-Host "✅ Connected to database" -ForegroundColor Green
    Write-Host ""
    
    # Step 1: Get all BOE documents for this container
    $queryBOEs = @"
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
        WHERE ContainerNumber = '$containerNumber'
        ORDER BY CreatedAt DESC
"@
    
    Write-Host "Step 1: Checking BOE documents for container $containerNumber..." -ForegroundColor Yellow
    $cmd = New-Object System.Data.SqlClient.SqlCommand($queryBOEs, $connection)
    $adapter = New-Object System.Data.SqlClient.SqlDataAdapter($cmd)
    $dataset = New-Object System.Data.DataSet
    $adapter.Fill($dataset) | Out-Null
    $boeDocuments = $dataset.Tables[0]
    
    if ($boeDocuments.Rows.Count -eq 0) {
        Write-Host "❌ No BOE documents found for container $containerNumber" -ForegroundColor Red
        exit
    }
    
    Write-Host "   Found $($boeDocuments.Rows.Count) BOE document(s):" -ForegroundColor Green
    Write-Host ""
    
    $boeIds = @()
    foreach ($row in $boeDocuments.Rows) {
        $boeId = $row["Id"]
        $boeIds += $boeId
        $goodsDesc = if ([string]::IsNullOrWhiteSpace($row["GoodsDescription"])) { "NULL/EMPTY" } else { $row["GoodsDescription"] }
        $goodsDescPreview = if ($goodsDesc.Length -gt 80) { $goodsDesc.Substring(0, 80) + "..." } else { $goodsDesc }
        
        Write-Host "   BOE ID: $boeId" -ForegroundColor White
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
    
    # Step 2: For each BOE document, check its ManifestItems
    Write-Host "Step 2: Checking ManifestItems for each BOE document..." -ForegroundColor Yellow
    Write-Host ""
    
    $allItemDescriptions = @{}
    
    foreach ($boeId in $boeIds) {
        $queryItems = @"
            SELECT 
                BOEDocumentId,
                ItemIndex,
                ItemNo,
                Description,
                HsCode,
                Quantity,
                ProcessingStatus
            FROM ManifestItems
            WHERE BOEDocumentId = $boeId
            ORDER BY ItemIndex, ItemNo
"@
        
        $cmd = New-Object System.Data.SqlClient.SqlCommand($queryItems, $connection)
        $adapter = New-Object System.Data.SqlClient.SqlDataAdapter($cmd)
        $dataset = New-Object System.Data.DataSet
        $adapter.Fill($dataset) | Out-Null
        $items = $dataset.Tables[0]
        
        $itemDescriptions = @()
        foreach ($item in $items.Rows) {
            if (![string]::IsNullOrWhiteSpace($item["Description"])) {
                $desc = $item["Description"].ToString().Trim()
                $itemDescriptions += $desc
            }
        }
        
        $allItemDescriptions[$boeId] = $itemDescriptions
        
        Write-Host "   BOE ID: $boeId" -ForegroundColor White
        Write-Host "      Total ManifestItems: $($items.Rows.Count)"
        Write-Host "      Items with Description: $($itemDescriptions.Count)"
        
        if ($itemDescriptions.Count -gt 0) {
            Write-Host "      Sample Item Descriptions:" -ForegroundColor Cyan
            $sampleCount = [Math]::Min(3, $itemDescriptions.Count)
            for ($i = 0; $i -lt $sampleCount; $i++) {
                $descPreview = if ($itemDescriptions[$i].Length -gt 80) { 
                    $itemDescriptions[$i].Substring(0, 80) + "..." 
                } else { 
                    $itemDescriptions[$i] 
                }
                Write-Host "         - $descPreview"
            }
            if ($itemDescriptions.Count -gt 3) {
                Write-Host "         ... and $($itemDescriptions.Count - 3) more"
            }
        }
        Write-Host ""
    }
    
    # Step 3: Compare Goods Description with Item Descriptions
    Write-Host "Step 3: Analyzing potential mismatches..." -ForegroundColor Yellow
    Write-Host ""
    
    $foundMismatch = $false
    
    foreach ($row in $boeDocuments.Rows) {
        $boeId = $row["Id"]
        $goodsDesc = if ([string]::IsNullOrWhiteSpace($row["GoodsDescription"])) { "" } else { $row["GoodsDescription"].ToString().Trim() }
        $itemDescs = $allItemDescriptions[$boeId]
        
        Write-Host "   BOE ID: $boeId" -ForegroundColor White
        Write-Host "      Goods Description: $(if ([string]::IsNullOrWhiteSpace($goodsDesc)) { 'NULL/EMPTY' } else { $goodsDesc })"
        Write-Host "      Item Descriptions from this BOE: $($itemDescs.Count) unique description(s)"
        
        if ([string]::IsNullOrWhiteSpace($goodsDesc) -and $itemDescs.Count -gt 0) {
            Write-Host "      WARNING: Goods Description is NULL but this BOE has Item Descriptions!" -ForegroundColor Yellow
            $foundMismatch = $true
        }
        elseif (![string]::IsNullOrWhiteSpace($goodsDesc) -and $itemDescs.Count -gt 0) {
            # Check if Goods Description matches any Item Description (fuzzy match)
            $matches = $false
            foreach ($itemDesc in $itemDescs) {
                if ($itemDesc -eq $goodsDesc -or $itemDesc.Contains($goodsDesc) -or $goodsDesc.Contains($itemDesc)) {
                    $matches = $true
                    break
                }
            }
            
            if (-not $matches) {
                Write-Host "      WARNING: Goods Description does not match any Item Description from this BOE!" -ForegroundColor Yellow
                $foundMismatch = $true
            } else {
                Write-Host "      OK: Goods Description matches Item Descriptions" -ForegroundColor Green
            }
        }
        Write-Host ""
    }
    
    # Step 4: Check if multiple BOE documents could cause confusion
    if ($boeDocuments.Rows.Count -gt 1) {
        Write-Host "Step 4: Multiple BOE documents detected - checking for cross-contamination..." -ForegroundColor Yellow
        Write-Host ""
        
        $allUniqueItemDescs = @()
        foreach ($boeId in $boeIds) {
            $allUniqueItemDescs += $allItemDescriptions[$boeId]
        }
        $allUniqueItemDescs = $allUniqueItemDescs | Select-Object -Unique
        
        $lastBoe = $boeDocuments.Rows[$boeDocuments.Rows.Count - 1]
        $lastBoeId = $lastBoe["Id"]
        $lastBoeGoodsDesc = if ([string]::IsNullOrWhiteSpace($lastBoe["GoodsDescription"])) { "" } else { $lastBoe["GoodsDescription"].ToString().Trim() }
        
        Write-Host "   Total unique Item Descriptions across ALL BOE documents: $($allUniqueItemDescs.Count)"
        Write-Host "   Last BOE (ID: $lastBoeId) Goods Description: $(if ([string]::IsNullOrWhiteSpace($lastBoeGoodsDesc)) { 'NULL/EMPTY' } else { $lastBoeGoodsDesc })"
        Write-Host ""
        
        if ([string]::IsNullOrWhiteSpace($lastBoeGoodsDesc)) {
            Write-Host "   WARNING: Last BOE has NULL Goods Description - it might use aggregated Item Descriptions!" -ForegroundColor Yellow
            $foundMismatch = $true
        }
        elseif ($allUniqueItemDescs.Count -gt 0) {
            # Check if last BOE's Goods Description matches any Item Description from ANY BOE
            $matchesAny = $false
            foreach ($itemDesc in $allUniqueItemDescs) {
                if ($itemDesc -eq $lastBoeGoodsDesc -or $itemDesc.Contains($lastBoeGoodsDesc) -or $lastBoeGoodsDesc.Contains($itemDesc)) {
                    $matchesAny = $true
                    break
                }
            }
            
            if (-not $matchesAny) {
                Write-Host "   WARNING: Last BOE's Goods Description does not match any Item Description from ANY BOE!" -ForegroundColor Yellow
                Write-Host "      This suggests Goods Description and Item Descriptions come from different BOE documents!" -ForegroundColor Red
                $foundMismatch = $true
            } else {
                Write-Host "   INFO: Last BOE's Goods Description matches some Item Description(s) (but could be from different BOE)" -ForegroundColor Cyan
            }
        }
        Write-Host ""
    }
    
    # Summary
    Write-Host "========================================" -ForegroundColor Cyan
    Write-Host "Summary" -ForegroundColor Cyan
    Write-Host "========================================" -ForegroundColor Cyan
    
    if ($foundMismatch) {
        Write-Host "❌ POTENTIAL MISMATCH DETECTED!" -ForegroundColor Red
        Write-Host "   The Goods Description and Item Descriptions may come from different BOE documents." -ForegroundColor Yellow
        Write-Host "   This can cause confusion in the ICUMS data display." -ForegroundColor Yellow
    } else {
        Write-Host "✅ No obvious mismatch detected." -ForegroundColor Green
        Write-Host "   Goods Description and Item Descriptions appear to be consistent." -ForegroundColor Green
    }
    
    Write-Host ""
    Write-Host "Recommendation:" -ForegroundColor Cyan
    Write-Host "   If multiple BOE documents exist, the system should:" -ForegroundColor White
    Write-Host "   1. Group Items by BOE document (using HouseBL)" -ForegroundColor White
    Write-Host "   2. Show Goods Description per BOE document (not aggregated)" -ForegroundColor White
    Write-Host "   3. Or ensure Goods Description matches the BOE document(s) from which Items are shown" -ForegroundColor White
    Write-Host ""
    
} catch {
    Write-Host "❌ Error: $($_.Exception.Message)" -ForegroundColor Red
    Write-Host $_.ScriptStackTrace
} finally {
    if ($connection.State -eq 'Open') {
        $connection.Close()
        Write-Host "Connection closed." -ForegroundColor Gray
    }
}


# Check if Goods Description comes from UnmappedFields
# Container: MRSU5186846, BOE ID: 43028

$containerNumber = "MRSU5186846"
$boeId = 43028
$connectionString = "Server=localhost;Database=ICUMS_Downloads;Integrated Security=true;TrustServerCertificate=true;"

Write-Host "========================================" -ForegroundColor Cyan
Write-Host "Checking UnmappedFields for Goods Description" -ForegroundColor Cyan
Write-Host "Container: $containerNumber, BOE ID: $boeId" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""

try {
    $connection = New-Object System.Data.SqlClient.SqlConnection($connectionString)
    $connection.Open()
    Write-Host "Connected to database" -ForegroundColor Green
    Write-Host ""
    
    # Check UnmappedFields 1-20
    $query = @"
        SELECT 
            UnmappedField1Label, UnmappedField1Value,
            UnmappedField2Label, UnmappedField2Value,
            UnmappedField3Label, UnmappedField3Value,
            UnmappedField4Label, UnmappedField4Value,
            UnmappedField5Label, UnmappedField5Value,
            UnmappedField6Label, UnmappedField6Value,
            UnmappedField7Label, UnmappedField7Value,
            UnmappedField8Label, UnmappedField8Value,
            UnmappedField9Label, UnmappedField9Value,
            UnmappedField10Label, UnmappedField10Value,
            UnmappedField11Label, UnmappedField11Value,
            UnmappedField12Label, UnmappedField12Value,
            UnmappedField13Label, UnmappedField13Value,
            UnmappedField14Label, UnmappedField14Value,
            UnmappedField15Label, UnmappedField15Value,
            UnmappedField16Label, UnmappedField16Value,
            UnmappedField17Label, UnmappedField17Value,
            UnmappedField18Label, UnmappedField18Value,
            UnmappedField19Label, UnmappedField19Value,
            UnmappedField20Label, UnmappedField20Value,
            UnmappedFieldsCount
        FROM BOEDocuments
        WHERE Id = $boeId
"@
    
    Write-Host "Checking UnmappedFields for BOE ID $boeId..." -ForegroundColor Yellow
    $cmd = New-Object System.Data.SqlClient.SqlCommand($query, $connection)
    $adapter = New-Object System.Data.SqlClient.SqlDataAdapter($cmd)
    $dataset = New-Object System.Data.DataSet
    $adapter.Fill($dataset) | Out-Null
    $row = $dataset.Tables[0].Rows[0]
    
    Write-Host "   Total Unmapped Fields: $($row['UnmappedFieldsCount'])" -ForegroundColor Cyan
    Write-Host ""
    
    $goodsDescFound = $false
    $goodsDescSources = @()
    
    for ($i = 1; $i -le 20; $i++) {
        $labelCol = "UnmappedField${i}Label"
        $valueCol = "UnmappedField${i}Value"
        
        $label = $row[$labelCol]
        $value = $row[$valueCol]
        
        if (![string]::IsNullOrWhiteSpace($label)) {
            $labelLower = $label.ToLower()
            
            # Check if this could be a Goods Description field
            if ($labelLower -like "*goods*description*" -or $labelLower -like "*description*" -or $labelLower -like "*cargo*description*") {
                $goodsDescFound = $true
                $goodsDescSources += @{
                    Field = "UnmappedField$i"
                    Label = $label
                    Value = if ([string]::IsNullOrWhiteSpace($value)) { "EMPTY" } else { $value }
                }
                Write-Host "   FOUND: UnmappedField$i" -ForegroundColor Yellow
                Write-Host "      Label: $label"
                Write-Host "      Value: $(if ([string]::IsNullOrWhiteSpace($value)) { 'EMPTY' } else { if ($value.Length -gt 100) { $value.Substring(0, 100) + '...' } else { $value } })"
                Write-Host ""
            }
        }
    }
    
    if (-not $goodsDescFound) {
        Write-Host "   No Goods Description found in UnmappedFields 1-20" -ForegroundColor Gray
        Write-Host ""
    }
    
    # Summary
    Write-Host "========================================" -ForegroundColor Cyan
    Write-Host "Summary" -ForegroundColor Cyan
    Write-Host "========================================" -ForegroundColor Cyan
    
    if ($goodsDescFound) {
        Write-Host "GOODS DESCRIPTION FOUND in UnmappedFields!" -ForegroundColor Yellow
        Write-Host "   This could be the source of the Goods Description shown in the UI." -ForegroundColor White
        Write-Host "   However, unmapped fields are added with prefix (e.g., 'ManifestDetails:GoodsDescription')" -ForegroundColor White
        Write-Host "   while the standard 'Goods Description' field is set from BOEDocument.GoodsDescription column." -ForegroundColor White
        Write-Host ""
        Write-Host "   ISSUE: If the UI shows 'Goods Description' from UnmappedFields," -ForegroundColor Yellow
        Write-Host "   it might overwrite or conflict with the standard 'Goods Description' field." -ForegroundColor Yellow
    } else {
        Write-Host "No Goods Description found in UnmappedFields." -ForegroundColor Green
        Write-Host "   The Goods Description must come from:" -ForegroundColor White
        Write-Host "   1. BOEDocument.GoodsDescription column (NULL in this case)" -ForegroundColor White
        Write-Host "   2. JSON fallback from RawJsonData (but RawJsonData is missing)" -ForegroundColor White
        Write-Host "   3. Aggregated from ManifestItems (but there are 0 items)" -ForegroundColor White
        Write-Host "   4. OR the API might be returning 'Not available' but UI shows something else" -ForegroundColor White
    }
    Write-Host ""
    
} catch {
    Write-Host "Error: $($_.Exception.Message)" -ForegroundColor Red
    Write-Host $_.ScriptStackTrace
} finally {
    if ($connection.State -eq 'Open') {
        $connection.Close()
        Write-Host "Connection closed." -ForegroundColor Gray
    }
}


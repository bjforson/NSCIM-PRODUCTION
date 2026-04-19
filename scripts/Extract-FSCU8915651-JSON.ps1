# Extract JSON sections for FSCU8915651 investigation
$connectionString = "Server=127.0.0.1;Database=ICUMS_Downloads;Integrated Security=True;"

try {
    $query = "SELECT RawJsonData FROM BOEDocuments WHERE Id = 45822"
    
    $connection = New-Object System.Data.SqlClient.SqlConnection($connectionString)
    $command = New-Object System.Data.SqlClient.SqlCommand($query, $connection)
    $connection.Open()
    
    $reader = $command.ExecuteReader()
    if ($reader.Read()) {
        $jsonText = $reader["RawJsonData"].ToString()
        $json = $jsonText | ConvertFrom-Json
        
        Write-Host "=== ManifestDetails Section ===" -ForegroundColor Cyan
        if ($json.ManifestDetails) {
            Write-Host "Description: $($json.ManifestDetails.Description)"
            Write-Host "GoodsDescription: $($json.ManifestDetails.GoodsDescription)"
            Write-Host "Goods_Description: $($json.ManifestDetails.Goods_Description)"
            Write-Host "GOODSDESCRIPTION: $($json.ManifestDetails.GOODSDESCRIPTION)"
            Write-Host ""
            Write-Host "All ManifestDetails Properties:" -ForegroundColor Yellow
            $json.ManifestDetails.PSObject.Properties | Where-Object { $_.Name -like "*Description*" -or $_.Name -like "*description*" } | ForEach-Object {
                Write-Host "  $($_.Name): $($_.Value)"
            }
        } else {
            Write-Host "ManifestDetails section not found"
        }
        
        Write-Host ""
        Write-Host "=== ManifestItems Array ===" -ForegroundColor Cyan
        if ($json.ManifestItems -and $json.ManifestItems.Count -gt 0) {
            Write-Host "Number of items: $($json.ManifestItems.Count)"
            $json.ManifestItems | ForEach-Object -Begin { $i = 1 } -Process {
                Write-Host "Item ${i}:" -ForegroundColor Yellow
                Write-Host "  Description: $($_.Description)"
                Write-Host "  HsCode: $($_.HsCode)"
                Write-Host "  Quantity: $($_.Quantity)"
                Write-Host ""
                $i++
            }
        } else {
            Write-Host "ManifestItems array is empty or null"
        }
        
        Write-Host ""
        Write-Host "=== ContainerDetails Section ===" -ForegroundColor Cyan
        if ($json.ContainerDetails) {
            Write-Host "Description: $($json.ContainerDetails.Description)"
        }
        
        Write-Host ""
        Write-Host "=== Search for all Description-related fields ===" -ForegroundColor Cyan
        $allDescFields = @()
        function Search-Object {
            param($obj, $prefix = "")
            foreach ($prop in $obj.PSObject.Properties) {
                if ($prop.Name -match "Description|description|DESCRIPTION") {
                    $allDescFields += "$prefix$($prop.Name): $($prop.Value)"
                }
                if ($prop.Value -is [PSCustomObject]) {
                    Search-Object $prop.Value "$prefix$($prop.Name)."
                }
                if ($prop.Value -is [Array]) {
                    for ($i = 0; $i -lt $prop.Value.Count; $i++) {
                        if ($prop.Value[$i] -is [PSCustomObject]) {
                            Search-Object $prop.Value[$i] "$prefix$($prop.Name)[$i]."
                        }
                    }
                }
            }
        }
        Search-Object $json
        $allDescFields | ForEach-Object { Write-Host $_ }
        
    } else {
        Write-Host "No data found for BOE ID 45822"
    }
    
    $reader.Close()
} catch {
    Write-Host "Error: $_" -ForegroundColor Red
} finally {
    if ($connection.State -eq 'Open') {
        $connection.Close()
    }
}


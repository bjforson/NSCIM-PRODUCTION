# Diagnostic script to investigate why some records don't display detailed summaries
# Usage: .\Diagnose-SummaryGeneration.ps1 -ContainerNumber "MRSU1234567"

param(
    [Parameter(Mandatory=$true)]
    [string]$ContainerNumber
)

# Import required modules
Add-Type -AssemblyName System.Web

Write-Host "============================================================" -ForegroundColor Cyan
Write-Host "SUMMARY GENERATION DIAGNOSTIC" -ForegroundColor Cyan
Write-Host "============================================================" -ForegroundColor Cyan
Write-Host ""

# Load connection string from appsettings.json
$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$projectRoot = Split-Path -Parent $scriptDir
$appsettingsPath = Join-Path $projectRoot "src\NickScanCentralImagingPortal.API\appsettings.json"
if (-not (Test-Path $appsettingsPath)) {
    Write-Host "ERROR: Cannot find appsettings.json at $appsettingsPath" -ForegroundColor Red
    exit 1
}

$appsettings = Get-Content $appsettingsPath -Raw | ConvertFrom-Json
# BOEDocuments is in the ICUMS_Downloads database
$connectionString = $appsettings.ConnectionStrings.ICUMS_Downloads_Connection
if ([string]::IsNullOrWhiteSpace($connectionString)) {
    # Fallback to ICUMS_Connection if ICUMS_Downloads_Connection not found
    $connectionString = $appsettings.ConnectionStrings.ICUMS_Connection
}

if ([string]::IsNullOrWhiteSpace($connectionString)) {
    Write-Host "ERROR: Connection string not found in appsettings.json" -ForegroundColor Red
    exit 1
}

Write-Host "Container: $ContainerNumber" -ForegroundColor Green
Write-Host ""

try {
    # Connect to database
    $connection = New-Object System.Data.SqlClient.SqlConnection($connectionString)
    $connection.Open()
    
    Write-Host "[OK] Database connection opened" -ForegroundColor Green
    Write-Host ""
    
    # Get BOE documents for this container
    $query = @"
    SELECT 
        Id,
        ContainerNumber,
        BlNumber,
        HouseBl,
        DeclarationNumber,
        ClearanceType,
        ConsigneeName,
        GoodsDescription,
        RawJsonData,
        IsConsolidated,
        TotalDutyPaid
    FROM BOEDocuments
    WHERE ContainerNumber = @ContainerNumber
"@
    
    $command = New-Object System.Data.SqlClient.SqlCommand($query, $connection)
    $command.Parameters.AddWithValue("@ContainerNumber", $ContainerNumber) | Out-Null
    $adapter = New-Object System.Data.SqlClient.SqlDataAdapter($command)
    $dataset = New-Object System.Data.DataSet
    $adapter.Fill($dataset) | Out-Null
    
    $boeDocuments = $dataset.Tables[0]
    
    if ($boeDocuments.Rows.Count -eq 0) {
        Write-Host "[ERROR] No BOE documents found for container $ContainerNumber" -ForegroundColor Red
        $connection.Close()
        exit 1
    }
    
    Write-Host "Found $($boeDocuments.Rows.Count) BOE document(s)" -ForegroundColor Green
    Write-Host ""
    
    # Analyze each BOE document
    foreach ($boeRow in $boeDocuments.Rows) {
        Write-Host "------------------------------------------------------------" -ForegroundColor Yellow
        Write-Host "BOE Document ID: $($boeRow.Id)" -ForegroundColor Yellow
        Write-Host "------------------------------------------------------------" -ForegroundColor Yellow
        Write-Host ""
        
        # Basic BOE info
        Write-Host "Basic Information:" -ForegroundColor Cyan
        Write-Host "  Container Number: $($boeRow.ContainerNumber)" -ForegroundColor White
        Write-Host "  Master BL: $(if ($boeRow.BlNumber) { $boeRow.BlNumber } else { 'NULL' })" -ForegroundColor White
        Write-Host "  House BL: $(if ($boeRow.HouseBl) { $boeRow.HouseBl } else { 'NULL' })" -ForegroundColor White
        Write-Host "  Declaration Number: $(if ($boeRow.DeclarationNumber) { $boeRow.DeclarationNumber } else { 'NULL' })" -ForegroundColor White
        Write-Host "  Clearance Type: $(if ($boeRow.ClearanceType) { $boeRow.ClearanceType } else { 'NULL' })" -ForegroundColor White
        Write-Host "  Is Consolidated: $($boeRow.IsConsolidated)" -ForegroundColor White
        Write-Host "  Consignee Name: $(if ($boeRow.ConsigneeName) { $boeRow.ConsigneeName } else { 'NULL' })" -ForegroundColor White
        Write-Host "  Goods Description: $(if ($boeRow.GoodsDescription) { $boeRow.GoodsDescription.Substring(0, [Math]::Min(50, $boeRow.GoodsDescription.Length)) } else { 'NULL' })" -ForegroundColor White
        Write-Host "  Total Duty Paid: $(if ($boeRow.TotalDutyPaid) { $boeRow.TotalDutyPaid } else { 'NULL' })" -ForegroundColor White
        Write-Host "  RawJsonData: $(if ($boeRow.RawJsonData) { 'Present (' + $boeRow.RawJsonData.Length + ' bytes)' } else { 'NULL' })" -ForegroundColor White
        Write-Host ""
        
        # Analyze RawJsonData if present
        if ($boeRow.RawJsonData) {
            Write-Host "RawJsonData Analysis:" -ForegroundColor Cyan
            try {
                $jsonData = $boeRow.RawJsonData | ConvertFrom-Json
                
                # Extract all field names and values from JSON
                $allFields = @()
                
                function ExtractFields {
                    param($obj, $prefix = "")
                    
                    if ($obj -is [PSCustomObject]) {
                        $obj.PSObject.Properties | ForEach-Object {
                            $fieldName = if ($prefix) { "$prefix.$($_.Name)" } else { $_.Name }
                            $fieldValue = $_.Value
                            
                            if ($fieldValue -is [PSCustomObject] -or $fieldValue -is [Array]) {
                                ExtractFields -obj $fieldValue -prefix $fieldName
                            } else {
                                $valueStr = if ($null -eq $fieldValue) { "NULL" } else { $fieldValue.ToString() }
                                $allFields += [PSCustomObject]@{
                                    Field = $fieldName
                                    Value = $valueStr
                                    IsEmpty = [string]::IsNullOrWhiteSpace($valueStr) -or $valueStr -eq "Not available" -or $valueStr -eq "N/A"
                                }
                            }
                        }
                    } elseif ($obj -is [Array]) {
                        $obj | ForEach-Object {
                            ExtractFields -obj $_ -prefix $prefix
                        }
                    }
                }
                
                ExtractFields -obj $jsonData
                
                Write-Host "  Total JSON fields found: $($allFields.Count)" -ForegroundColor White
                Write-Host ""
                
                # Check for summary-related fields
                $summaryFieldPatterns = @(
                    @{ Pattern = "Consignee"; Type = "Consignee" },
                    @{ Pattern = "Goods Description|Item Description|Description"; Type = "Goods Description" },
                    @{ Pattern = "HS Code|HSCode|HsCode"; Type = "HS Code" },
                    @{ Pattern = "Quantity"; Type = "Quantity" },
                    @{ Pattern = "Weight|Gross Weight"; Type = "Weight" },
                    @{ Pattern = "FOB|Fob"; Type = "FOB Value" },
                    @{ Pattern = "Duty"; Type = "Duty Paid" },
                    @{ Pattern = "Country of Origin|Origin"; Type = "Country of Origin" },
                    @{ Pattern = "Item.*Description"; Type = "Item Description" }
                )
                
                Write-Host "Summary Field Matching:" -ForegroundColor Cyan
                $foundFields = @{}
                
                foreach ($patternInfo in $summaryFieldPatterns) {
                    $matchedFields = $allFields | Where-Object { 
                        $_.Field -match $patternInfo.Pattern -and -not $_.IsEmpty 
                    }
                    
                    if ($matchedFields) {
                        $foundFields[$patternInfo.Type] = $matchedFields
                        Write-Host "  [OK] $($patternInfo.Type): Found $($matchedFields.Count) field(s)" -ForegroundColor Green
                        $matchedFields | ForEach-Object {
                            $valuePreview = if ($_.Value.Length -gt 50) { $_.Value.Substring(0, 50) + "..." } else { $_.Value }
                            Write-Host "    - $($_.Field): $valuePreview" -ForegroundColor Gray
                        }
                    } else {
                        Write-Host "  [MISSING] $($patternInfo.Type): NOT FOUND" -ForegroundColor Red
                    }
                }
                
                Write-Host ""
                
                # Check for empty values
                $emptyFields = $allFields | Where-Object { $_.IsEmpty }
                if ($emptyFields) {
                    Write-Host "Empty/Filtered Fields ($($emptyFields.Count)):" -ForegroundColor Yellow
                    $emptyFields | Select-Object -First 10 -Property Field, Value | ForEach-Object {
                        Write-Host "  - $($_.Field): '$($_.Value)'" -ForegroundColor Gray
                    }
                    if ($emptyFields.Count -gt 10) {
                        Write-Host "  ... and $($emptyFields.Count - 10) more" -ForegroundColor Gray
                    }
                    Write-Host ""
                }
                
                # Summary assessment
                Write-Host "Summary Generation Assessment:" -ForegroundColor Cyan
                $hasConsignee = $foundFields.ContainsKey("Consignee")
                $hasGoodsDescription = $foundFields.ContainsKey("Goods Description") -or $foundFields.ContainsKey("Item Description")
                $hasMetrics = $foundFields.ContainsKey("HS Code") -or $foundFields.ContainsKey("Quantity") -or $foundFields.ContainsKey("Weight") -or $foundFields.ContainsKey("FOB Value") -or $foundFields.ContainsKey("Duty Paid")
                
                if ($hasConsignee -or $hasGoodsDescription -or $hasMetrics) {
                    Write-Host "  [OK] Has data for summary generation" -ForegroundColor Green
                    Write-Host "    - Consignee: $(if ($hasConsignee) { 'YES' } else { 'NO' })" -ForegroundColor $(if ($hasConsignee) { 'Green' } else { 'Yellow' })
                    Write-Host "    - Goods Description: $(if ($hasGoodsDescription) { 'YES' } else { 'NO' })" -ForegroundColor $(if ($hasGoodsDescription) { 'Green' } else { 'Yellow' })
                    Write-Host "    - Metrics (HS Code, Quantity, Weight, FOB, Duty): $(if ($hasMetrics) { 'YES' } else { 'NO' })" -ForegroundColor $(if ($hasMetrics) { 'Green' } else { 'Yellow' })
                } else {
                    Write-Host "  [ERROR] Missing data required for summary generation" -ForegroundColor Red
                    Write-Host "    Issue: No consignee, goods description, or metrics found in extractable format" -ForegroundColor Red
                }
                
            } catch {
                Write-Host "  [WARNING] ERROR parsing RawJsonData: $($_.Exception.Message)" -ForegroundColor Red
            }
        } else {
            Write-Host "RawJsonData: NULL (only entity properties available)" -ForegroundColor Yellow
            Write-Host ""
            
            # Check entity properties
            $hasEntityData = $false
            if ($boeRow.ConsigneeName -and $boeRow.ConsigneeName -ne "Not available" -and $boeRow.ConsigneeName -ne "N/A") {
                Write-Host "  [OK] Consignee available from entity: $($boeRow.ConsigneeName)" -ForegroundColor Green
                $hasEntityData = $true
            }
            if ($boeRow.GoodsDescription -and $boeRow.GoodsDescription -ne "Not available" -and $boeRow.GoodsDescription -ne "N/A") {
                Write-Host "  [OK] Goods Description available from entity" -ForegroundColor Green
                $hasEntityData = $true
            }
            if (-not $hasEntityData) {
                Write-Host "  [ERROR] No extractable entity properties for summary" -ForegroundColor Red
            }
        }
        
        Write-Host ""
    }
    
    $connection.Close()
    
    Write-Host "============================================================" -ForegroundColor Cyan
    Write-Host "DIAGNOSTIC COMPLETE" -ForegroundColor Cyan
    Write-Host "============================================================" -ForegroundColor Cyan
    
} catch {
    Write-Host "ERROR: $($_.Exception.Message)" -ForegroundColor Red
    Write-Host $_.ScriptStackTrace -ForegroundColor Red
    if ($connection -and $connection.State -eq [System.Data.ConnectionState]::Open) {
        $connection.Close()
    }
    exit 1
}


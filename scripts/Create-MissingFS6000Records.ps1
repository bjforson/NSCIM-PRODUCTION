# Create Missing FS6000 Database Records from Archive
# Reads the missing scans CSV and creates database records by parsing XML files

param(
    [Parameter(Mandatory=$false)]
    [string]$CsvPath = "FS6000_MissingScans_December2025.csv",
    
    [Parameter(Mandatory=$false)]
    [string]$ConnectionString = "Server=127.0.0.1,1433;Database=NS_CIS;Trusted_Connection=true;MultipleActiveResultSets=true;Encrypt=true;TrustServerCertificate=true;",
    
    [Parameter(Mandatory=$false)]
    [switch]$DryRun = $false
)

$ErrorActionPreference = "Stop"

Write-Host "=====================================================" -ForegroundColor Cyan
Write-Host "CREATE MISSING FS6000 DATABASE RECORDS" -ForegroundColor Cyan
Write-Host "=====================================================" -ForegroundColor Cyan
Write-Host ""

if ($DryRun) {
    Write-Host "⚠️  DRY RUN MODE - No database changes will be made" -ForegroundColor Yellow
    Write-Host ""
}

# Find CSV file
$csvFile = Get-Item $CsvPath -ErrorAction SilentlyContinue
if (-not $csvFile) {
    # Try in current directory
    $csvFile = Get-Item ".\$CsvPath" -ErrorAction SilentlyContinue
}
if (-not $csvFile) {
    Write-Host "❌ CSV file not found: $CsvPath" -ForegroundColor Red
    exit 1
}

Write-Host "Reading CSV file: $($csvFile.FullName)" -ForegroundColor Yellow
$missingScans = Import-Csv $csvFile.FullName
Write-Host "Found $($missingScans.Count) missing scans to process" -ForegroundColor Green
Write-Host ""

# Function to parse XML and extract all scan data
function Parse-XmlFileComplete {
    param([string]$XmlFilePath)
    
    try {
        [xml]$xml = Get-Content $XmlFilePath -ErrorAction Stop
        
        $scans = @()
        
        # Handle IDR structure (FS6000 uses IDR/IDR_IMAGE/IDR_CHECK_UNIT)
        $idrImage = $xml.SelectSingleNode("//IDR_IMAGE")
        
        if ($idrImage) {
            # Extract scan time from IDR_IMAGE
            $scanTimeNode = $idrImage.SelectSingleNode("SCANTIME")
            $scanTimeStr = if ($scanTimeNode) { $scanTimeNode.InnerText } else { $null }
            
            $scanTime = [DateTime]::UtcNow
            if ($scanTimeStr) {
                if (-not [DateTime]::TryParse($scanTimeStr, [ref]$scanTime)) {
                    $scanTime = [DateTime]::UtcNow
                }
            }
            
            # Extract PicNumber
            $picNoNode = $idrImage.SelectSingleNode("PICNO")
            $picNumber = if ($picNoNode) { $picNoNode.InnerText } else { "" }
            
            # Extract other fields from IDR_IMAGE
            $fycoPresentNode = $idrImage.SelectSingleNode("fyco_present")
            if (-not $fycoPresentNode) { $fycoPresentNode = $idrImage.SelectSingleNode("FYCO_PRESENT") }
            $fycoPresent = if ($fycoPresentNode) { $fycoPresentNode.InnerText } else { "" }
            
            $vesselNameNode = $idrImage.SelectSingleNode("name_vessel")
            if (-not $vesselNameNode) { $vesselNameNode = $idrImage.SelectSingleNode("VesselName") }
            if (-not $vesselNameNode) { $vesselNameNode = $idrImage.SelectSingleNode("VESSEL_NAME") }
            $vesselName = if ($vesselNameNode) { $vesselNameNode.InnerText } else { "" }
            
            $operatorIdNode = $idrImage.SelectSingleNode("OPERATORID")
            if (-not $operatorIdNode) { $operatorIdNode = $idrImage.SelectSingleNode("OPERATOR_ID") }
            if (-not $operatorIdNode) { $operatorIdNode = $idrImage.SelectSingleNode("operator_id") }
            $operatorId = if ($operatorIdNode) { $operatorIdNode.InnerText } else { "" }
            
            $scanResultNode = $idrImage.SelectSingleNode("TYPE")
            if (-not $scanResultNode) { $scanResultNode = $idrImage.SelectSingleNode("SCAN_RESULT") }
            if (-not $scanResultNode) { $scanResultNode = $idrImage.SelectSingleNode("scan_result") }
            if (-not $scanResultNode) { $scanResultNode = $idrImage.SelectSingleNode("RESULT") }
            $scanResult = if ($scanResultNode) { $scanResultNode.InnerText } else { "" }
            
            $goodsDescriptionNode = $idrImage.SelectSingleNode("descripion_of_goods")
            if (-not $goodsDescriptionNode) { $goodsDescriptionNode = $idrImage.SelectSingleNode("GOODS_DESCRIPTION") }
            if (-not $goodsDescriptionNode) { $goodsDescriptionNode = $idrImage.SelectSingleNode("goods_description") }
            $goodsDescription = if ($goodsDescriptionNode) { $goodsDescriptionNode.InnerText } else { "" }
            
            $shippingCompanyNode = $idrImage.SelectSingleNode("shipping_company")
            if (-not $shippingCompanyNode) { $shippingCompanyNode = $idrImage.SelectSingleNode("SHIPPING_COMPANY") }
            if (-not $shippingCompanyNode) { $shippingCompanyNode = $idrImage.SelectSingleNode("SHIPPER") }
            $shippingCompany = if ($shippingCompanyNode) { $shippingCompanyNode.InnerText } else { "" }
            
            $consigneeNode = $idrImage.SelectSingleNode("consignee")
            if (-not $consigneeNode) { $consigneeNode = $idrImage.SelectSingleNode("CONSIGNEE") }
            if (-not $consigneeNode) { $consigneeNode = $idrImage.SelectSingleNode("CONSIGNEE_NAME") }
            $consignee = if ($consigneeNode) { $consigneeNode.InnerText } else { "" }
            
            # Extract container numbers from IDR_CHECK_UNIT
            $checkUnits = $idrImage.SelectNodes("IDR_CHECK_UNIT")
            
            foreach ($checkUnit in $checkUnits) {
                $unitIdNode = $checkUnit.SelectSingleNode("UNITID")
                $containerNoNode = $checkUnit.SelectSingleNode(".//container_no")
                
                $containerNumbers = @()
                if ($unitIdNode) {
                    $unitId = $unitIdNode.InnerText
                    if ($unitId) {
                        $containerNumbers += ($unitId -split ',') | ForEach-Object { $_.Trim() } | Where-Object { $_ }
                    }
                }
                if ($containerNoNode) {
                    $containerNo = $containerNoNode.InnerText
                    if ($containerNo) {
                        $containerNumbers += ($containerNo -split ',') | ForEach-Object { $_.Trim() } | Where-Object { $_ }
                    }
                }
                
                $containerNumbers = $containerNumbers | Select-Object -Unique
                
                # Get relative file path (archive path)
                $archiveBase = "C:\NickScan\FS6000\Archive"
                $relativePath = $XmlFilePath.Replace($archiveBase, "").TrimStart('\', '/')
                $filePath = "Archive\$relativePath"
                
                # Create record for each container number
                foreach ($container in $containerNumbers) {
                    if ([string]::IsNullOrWhiteSpace($container)) { continue }
                
                # Generate new GUID for Id
                $id = [Guid]::NewGuid().ToString()
                
                $scans += [PSCustomObject]@{
                    Id = $id
                    ContainerNumber = $container
                    ScanTime = $scanTime
                    PicNumber = $picNumber
                    FycoPresent = if ([string]::IsNullOrWhiteSpace($fycoPresent)) { $null } else { $fycoPresent }
                    VesselName = if ([string]::IsNullOrWhiteSpace($vesselName)) { $null } else { $vesselName }
                    OperatorId = if ([string]::IsNullOrWhiteSpace($operatorId)) { $null } else { $operatorId }
                    ScanResult = if ([string]::IsNullOrWhiteSpace($scanResult)) { $null } else { $scanResult }
                    GoodsDescription = if ([string]::IsNullOrWhiteSpace($goodsDescription)) { $null } else { $goodsDescription }
                    ShippingCompany = if ([string]::IsNullOrWhiteSpace($shippingCompany)) { $null } else { $shippingCompany }
                    Consignee = if ([string]::IsNullOrWhiteSpace($consignee)) { $null } else { $consignee }
                    FilePath = $filePath
                    SyncStatus = "Pending"
                    CreatedAt = [DateTime]::UtcNow
                    HasImage = $false
                    ImageCount = 0
                    XmlFilePath = $XmlFilePath
                }
                }
            }
        } else {
            Write-Warning "No IDR_IMAGE element found in XML: $XmlFilePath"
        }
        
        return $scans
    }
    catch {
        Write-Warning "Error parsing XML file $XmlFilePath : $($_.Exception.Message)"
        return @()
    }
}

# Process each missing scan
$connection = $null
$recordsCreated = 0
$recordsSkipped = 0
$errors = @()

try {
    if (-not $DryRun) {
        $connection = New-Object System.Data.SqlClient.SqlConnection($ConnectionString)
        $connection.Open()
        Write-Host "Database connection opened" -ForegroundColor Green
        Write-Host ""
    }
    
    Write-Host "Processing missing scans..." -ForegroundColor Yellow
    
    foreach ($scan in $missingScans) {
        $xmlPath = $scan.XmlFile
        $containerNumber = $scan.ContainerNumber
        
        if (-not (Test-Path $xmlPath)) {
            Write-Warning "XML file not found: $xmlPath"
            $recordsSkipped++
            $errors += "File not found: $xmlPath"
            continue
        }
        
        # Parse XML file to get complete scan data
        $scansFromXml = Parse-XmlFileComplete -XmlFilePath $xmlPath
        
        # Find the scan matching our container number
        $matchingScan = $scansFromXml | Where-Object { $_.ContainerNumber -eq $containerNumber } | Select-Object -First 1
        
        if (-not $matchingScan) {
            Write-Warning "Could not find container $containerNumber in XML file: $xmlPath"
            $recordsSkipped++
            continue
        }
        
        if ($DryRun) {
            Write-Host "  [DRY RUN] Would create: $containerNumber - $($matchingScan.ScanTime) - $($matchingScan.PicNumber)" -ForegroundColor Cyan
            $recordsCreated++
        } else {
            try {
                # Insert into database
                $insertSql = @"
INSERT INTO FS6000Scans (
    Id, ContainerNumber, ScanTime, PicNumber, FycoPresent, VesselName, 
    OperatorId, ScanResult, GoodsDescription, ShippingCompany, Consignee, 
    FilePath, SyncStatus, CreatedAt, HasImage, ImageCount
) VALUES (
    @Id, @ContainerNumber, @ScanTime, @PicNumber, @FycoPresent, @VesselName,
    @OperatorId, @ScanResult, @GoodsDescription, @ShippingCompany, @Consignee,
    @FilePath, @SyncStatus, @CreatedAt, @HasImage, @ImageCount
)
"@
                
                $command = $connection.CreateCommand()
                $command.CommandText = $insertSql
                
                # Add parameters
                $command.Parameters.AddWithValue("@Id", [Guid]::Parse($matchingScan.Id)) | Out-Null
                $command.Parameters.AddWithValue("@ContainerNumber", $matchingScan.ContainerNumber) | Out-Null
                $command.Parameters.AddWithValue("@ScanTime", $matchingScan.ScanTime) | Out-Null
                $command.Parameters.AddWithValue("@PicNumber", $matchingScan.PicNumber) | Out-Null
                $command.Parameters.AddWithValue("@FycoPresent", [DBNull]::Value) | Out-Null
                if ($matchingScan.FycoPresent) { $command.Parameters["@FycoPresent"].Value = $matchingScan.FycoPresent }
                
                $command.Parameters.AddWithValue("@VesselName", [DBNull]::Value) | Out-Null
                if ($matchingScan.VesselName) { $command.Parameters["@VesselName"].Value = $matchingScan.VesselName }
                
                $command.Parameters.AddWithValue("@OperatorId", [DBNull]::Value) | Out-Null
                if ($matchingScan.OperatorId) { $command.Parameters["@OperatorId"].Value = $matchingScan.OperatorId }
                
                $command.Parameters.AddWithValue("@ScanResult", [DBNull]::Value) | Out-Null
                if ($matchingScan.ScanResult) { $command.Parameters["@ScanResult"].Value = $matchingScan.ScanResult }
                
                $command.Parameters.AddWithValue("@GoodsDescription", [DBNull]::Value) | Out-Null
                if ($matchingScan.GoodsDescription) { $command.Parameters["@GoodsDescription"].Value = $matchingScan.GoodsDescription }
                
                $command.Parameters.AddWithValue("@ShippingCompany", [DBNull]::Value) | Out-Null
                if ($matchingScan.ShippingCompany) { $command.Parameters["@ShippingCompany"].Value = $matchingScan.ShippingCompany }
                
                $command.Parameters.AddWithValue("@Consignee", [DBNull]::Value) | Out-Null
                if ($matchingScan.Consignee) { $command.Parameters["@Consignee"].Value = $matchingScan.Consignee }
                
                $command.Parameters.AddWithValue("@FilePath", [DBNull]::Value) | Out-Null
                if ($matchingScan.FilePath) { $command.Parameters["@FilePath"].Value = $matchingScan.FilePath }
                
                $command.Parameters.AddWithValue("@SyncStatus", $matchingScan.SyncStatus) | Out-Null
                $command.Parameters.AddWithValue("@CreatedAt", $matchingScan.CreatedAt) | Out-Null
                $command.Parameters.AddWithValue("@HasImage", $matchingScan.HasImage) | Out-Null
                $command.Parameters.AddWithValue("@ImageCount", $matchingScan.ImageCount) | Out-Null
                
                $rowsAffected = $command.ExecuteNonQuery()
                
                if ($rowsAffected -eq 1) {
                    $recordsCreated++
                    if ($recordsCreated % 50 -eq 0) {
                        Write-Host "  Created $recordsCreated / $($missingScans.Count) records..." -ForegroundColor Gray
                    }
                } else {
                    Write-Warning "Insert returned $rowsAffected rows for $containerNumber"
                    $recordsSkipped++
                }
                
                $command.Dispose()
            }
            catch {
                Write-Warning "Error inserting $containerNumber : $($_.Exception.Message)"
                $recordsSkipped++
                $errors += "$containerNumber : $($_.Exception.Message)"
            }
        }
    }
    
    Write-Host ""
    Write-Host "=====================================================" -ForegroundColor Cyan
    Write-Host "PROCESSING COMPLETE" -ForegroundColor Cyan
    Write-Host "=====================================================" -ForegroundColor Cyan
    Write-Host ""
    Write-Host "Records Created: $recordsCreated" -ForegroundColor Green
    Write-Host "Records Skipped: $recordsSkipped" -ForegroundColor $(if ($recordsSkipped -gt 0) { "Yellow" } else { "Green" })
    
    if ($errors.Count -gt 0) {
        Write-Host ""
        Write-Host "Errors encountered: $($errors.Count)" -ForegroundColor Red
        $errors | Select-Object -First 10 | ForEach-Object { Write-Host "  - $_" -ForegroundColor Red }
    }
    
} catch {
    Write-Host "❌ Error: $($_.Exception.Message)" -ForegroundColor Red
    Write-Host $_.ScriptStackTrace -ForegroundColor Red
} finally {
    if ($connection) {
        $connection.Close()
        Write-Host ""
        Write-Host "Database connection closed" -ForegroundColor Green
    }
}

Write-Host ""
Write-Host "=====================================================" -ForegroundColor Cyan


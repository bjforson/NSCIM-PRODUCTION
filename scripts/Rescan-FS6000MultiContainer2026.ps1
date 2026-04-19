# Rescan FS6000 2026 Files for Multi-Container Records
# This script reprocesses XML files from 2026 to extract missing container numbers from comma-separated values
# Date: January 4, 2026

param(
    [switch]$DryRun = $false,
    [string]$Year = "2026",
    [string]$SourcePath = "C:\NickScan\FS6000\Staging",
    [string]$ProcessedPath = "C:\NickScan\FS6000\Processed"
)

Write-Host "========================================" -ForegroundColor Cyan
Write-Host "FS6000 Multi-Container Rescan Script" -ForegroundColor Cyan
Write-Host "Year: $Year" -ForegroundColor Cyan
if ($DryRun) {
    Write-Host "Mode: DRY RUN (no changes will be made)" -ForegroundColor Yellow
} else {
    Write-Host "Mode: LIVE (will update database)" -ForegroundColor Green
}
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""

# Database connection
$connectionString = "Server=localhost;Database=NS_CIS;Integrated Security=True;TrustServerCertificate=True;"

# Function to parse XML and extract all container numbers
function Get-AllContainerNumbersFromXml {
    param([string]$XmlFilePath)
    
    try {
        $xmlContent = Get-Content -Path $XmlFilePath -Raw -Encoding Unicode -ErrorAction SilentlyContinue
        if ($null -eq $xmlContent) {
            $xmlContent = Get-Content -Path $XmlFilePath -Raw -Encoding UTF8 -ErrorAction SilentlyContinue
        }
        
        if ([string]::IsNullOrEmpty($xmlContent)) {
            return @()
        }
        
        # Fix XML declaration if needed
        if (-not $xmlContent.Contains("<?xml")) {
            $xmlContent = "<?xml version=`"1.0`" encoding=`"UTF-16`"?>`n" + $xmlContent
        }
        
        [xml]$xmlDoc = $null
        try {
            $xmlDoc = [xml]$xmlContent
        } catch {
            Write-Warning "Failed to parse XML: $($_.Exception.Message)"
            return @()
        }
        
        $containerNumbers = @()
        
        # Try to find UNITID and container_no elements
        $unitIdNodes = $xmlDoc.SelectNodes("//UNITID") + $xmlDoc.SelectNodes("//unitid") + $xmlDoc.SelectNodes("//UnitId")
        $containerNoNodes = $xmlDoc.SelectNodes("//container_no") + $xmlDoc.SelectNodes("//CONTAINER_NO") + $xmlDoc.SelectNodes("//container_number") + $xmlDoc.SelectNodes("//ContainerNumber")
        
        $unitIdValues = @()
        $containerNoValues = @()
        
        foreach ($node in $unitIdNodes) {
            if ($node.InnerText) {
                $unitIdValues += $node.InnerText.Trim()
            }
        }
        
        foreach ($node in $containerNoNodes) {
            if ($node.InnerText) {
                $containerNoValues += $node.InnerText.Trim()
            }
        }
        
        # Split comma-separated values
        $unitIdNumbers = @()
        foreach ($value in $unitIdValues) {
            $unitIdNumbers += $value.Split(',') | ForEach-Object { $_.Trim() } | Where-Object { $_ -ne "" }
        }
        
        $containerNoNumbers = @()
        foreach ($value in $containerNoValues) {
            $containerNoNumbers += $value.Split(',') | ForEach-Object { $_.Trim() } | Where-Object { $_ -ne "" }
        }
        
        # Cross-validation: return matches between UNITID and container_no
        if ($unitIdNumbers.Count -gt 0 -and $containerNoNumbers.Count -gt 0) {
            $matches = $unitIdNumbers | Where-Object { $containerNoNumbers -contains $_ }
            if ($matches.Count -gt 0) {
                return $matches | Select-Object -Unique
            }
        }
        
        # Fallback: return all UNITID values if container_no is empty
        if ($unitIdNumbers.Count -gt 0 -and $containerNoNumbers.Count -eq 0) {
            return $unitIdNumbers | Select-Object -Unique
        }
        
        # Fallback: return all container_no values if UNITID is empty
        if ($containerNoNumbers.Count -gt 0 -and $unitIdNumbers.Count -eq 0) {
            return $containerNoNumbers | Select-Object -Unique
        }
        
        return @()
    }
    catch {
        Write-Warning "Error parsing XML file $XmlFilePath : $($_.Exception.Message)"
        return @()
    }
}

# Function to get PicNumber from XML
function Get-PicNumberFromXml {
    param([string]$XmlFilePath)
    
    try {
        $xmlContent = Get-Content -Path $XmlFilePath -Raw -Encoding Unicode -ErrorAction SilentlyContinue
        if ($null -eq $xmlContent) {
            $xmlContent = Get-Content -Path $XmlFilePath -Raw -Encoding UTF8 -ErrorAction SilentlyContinue
        }
        
        if ([string]::IsNullOrEmpty($xmlContent)) {
            return $null
        }
        
        [xml]$xmlDoc = [xml]$xmlContent
        
        $picNodes = $xmlDoc.SelectNodes("//PICNO") + $xmlDoc.SelectNodes("//picno") + $xmlDoc.SelectNodes("//PicNo") + $xmlDoc.SelectNodes("//PIC_NUMBER")
        
        foreach ($node in $picNodes) {
            if ($node.InnerText) {
                return $node.InnerText.Trim()
            }
        }
        
        return $null
    }
    catch {
        return $null
    }
}

# Find all XML files from the specified year
Write-Host "Searching for XML files from year $Year..." -ForegroundColor Yellow
$yearPath = Join-Path $SourcePath $Year
$processedYearPath = Join-Path $ProcessedPath $Year

$xmlFiles = @()
if (Test-Path $yearPath) {
    $xmlFiles += Get-ChildItem -Path $yearPath -Filter "*.xml" -Recurse -File
}
if (Test-Path $processedYearPath) {
    $xmlFiles += Get-ChildItem -Path $processedYearPath -Filter "*.xml" -Recurse -File
}

$xmlFiles = $xmlFiles | Select-Object -Unique

Write-Host "Found $($xmlFiles.Count) XML files from $Year" -ForegroundColor Green
Write-Host ""

if ($xmlFiles.Count -eq 0) {
    Write-Host "No XML files found. Exiting." -ForegroundColor Yellow
    exit 0
}

# Connect to database
Write-Host "Connecting to database..." -ForegroundColor Yellow
try {
    $connection = New-Object System.Data.SqlClient.SqlConnection($connectionString)
    $connection.Open()
    Write-Host "✅ Connected to database" -ForegroundColor Green
}
catch {
    Write-Host "❌ Failed to connect to database: $($_.Exception.Message)" -ForegroundColor Red
    exit 1
}

$stats = @{
    FilesProcessed = 0
    FilesWithMultiContainer = 0
    MissingContainersFound = 0
    RecordsUpdated = 0
    Errors = 0
}

Write-Host ""
Write-Host "Processing files..." -ForegroundColor Yellow
Write-Host ""

$batchSize = 100
$currentBatch = 0

foreach ($xmlFile in $xmlFiles) {
    $currentBatch++
    
    try {
        # Extract all container numbers from XML
        $allContainers = Get-AllContainerNumbersFromXml -XmlFilePath $xmlFile.FullName
        
        if ($allContainers.Count -le 1) {
            continue # Skip files with single or no containers
        }
        
        $stats.FilesWithMultiContainer++
        Write-Host "[$currentBatch/$($xmlFiles.Count)] Multi-container file: $($xmlFile.Name)" -ForegroundColor Cyan
        Write-Host "  Containers found: $($allContainers -join ', ')" -ForegroundColor White
        
        # Get PicNumber to match with database records
        $picNumber = Get-PicNumberFromXml -XmlFilePath $xmlFile.FullName
        
        if ([string]::IsNullOrEmpty($picNumber)) {
            Write-Host "  ⚠️  Warning: Could not extract PicNumber from XML" -ForegroundColor Yellow
            continue
        }
        
        # Check which containers already exist in database for this PicNumber
        $existingContainersQuery = @"
            SELECT DISTINCT ContainerNumber 
            FROM FS6000Scans 
            WHERE PicNumber = @PicNumber
"@
        
        $existingCmd = New-Object System.Data.SqlClient.SqlCommand($existingContainersQuery, $connection)
        $existingCmd.Parameters.AddWithValue("@PicNumber", $picNumber) | Out-Null
        $existingReader = $existingCmd.ExecuteReader()
        
        $existingContainers = @()
        while ($existingReader.Read()) {
            $existingContainers += $existingReader["ContainerNumber"].ToString()
        }
        $existingReader.Close()
        
        Write-Host "  Existing in DB: $($existingContainers -join ', ')" -ForegroundColor Gray
        
        # Find missing containers
        $missingContainers = $allContainers | Where-Object { $existingContainers -notcontains $_ }
        
        if ($missingContainers.Count -eq 0) {
            Write-Host "  ✅ All containers already in database" -ForegroundColor Green
            $stats.FilesProcessed++
            continue
        }
        
        Write-Host "  Missing containers: $($missingContainers -join ', ')" -ForegroundColor Yellow
        $stats.MissingContainersFound += $missingContainers.Count
        
        if ($DryRun) {
            Write-Host "  [DRY RUN] Would create $($missingContainers.Count) new scan record(s)" -ForegroundColor Yellow
            $stats.FilesProcessed++
            continue
        }
        
        # Get the first existing record to use as a template
        $templateQuery = @"
            SELECT TOP 1 
                ScanTime, FycoPresent, VesselName, OperatorId, ScanResult, 
                GoodsDescription, ShippingCompany, Consignee, FilePath, 
                SyncStatus, CreatedAt, ProcessedAt, HasImage, ImageCount
            FROM FS6000Scans 
            WHERE PicNumber = @PicNumber
"@
        
        $templateCmd = New-Object System.Data.SqlClient.SqlCommand($templateQuery, $connection)
        $templateCmd.Parameters.AddWithValue("@PicNumber", $picNumber) | Out-Null
        $templateReader = $templateCmd.ExecuteReader()
        
        if (-not $templateReader.Read()) {
            $templateReader.Close()
            Write-Host "  ⚠️  Warning: Could not find template record" -ForegroundColor Yellow
            $stats.Errors++
            continue
        }
        
        $scanTime = $templateReader["ScanTime"]
        $fycoPresent = if ($templateReader["FycoPresent"] -ne [DBNull]::Value) { $templateReader["FycoPresent"].ToString() } else { $null }
        $vesselName = if ($templateReader["VesselName"] -ne [DBNull]::Value) { $templateReader["VesselName"].ToString() } else { $null }
        $operatorId = if ($templateReader["OperatorId"] -ne [DBNull]::Value) { $templateReader["OperatorId"].ToString() } else { $null }
        $scanResult = if ($templateReader["ScanResult"] -ne [DBNull]::Value) { $templateReader["ScanResult"].ToString() } else { $null }
        $goodsDescription = if ($templateReader["GoodsDescription"] -ne [DBNull]::Value) { $templateReader["GoodsDescription"].ToString() } else { $null }
        $shippingCompany = if ($templateReader["ShippingCompany"] -ne [DBNull]::Value) { $templateReader["ShippingCompany"].ToString() } else { $null }
        $consignee = if ($templateReader["Consignee"] -ne [DBNull]::Value) { $templateReader["Consignee"].ToString() } else { $null }
        $filePath = if ($templateReader["FilePath"] -ne [DBNull]::Value) { $templateReader["FilePath"].ToString() } else { $null }
        $syncStatus = if ($templateReader["SyncStatus"] -ne [DBNull]::Value) { $templateReader["SyncStatus"].ToString() } else { "Pending" }
        $hasImage = if ($templateReader["HasImage"] -ne [DBNull]::Value) { [bool]$templateReader["HasImage"] } else { $false }
        $imageCount = if ($templateReader["ImageCount"] -ne [DBNull]::Value) { [int]$templateReader["ImageCount"] } else { 0 }
        
        $templateReader.Close()
        
        # Create new scan records for missing containers
        foreach ($missingContainer in $missingContainers) {
            $insertQuery = @"
                INSERT INTO FS6000Scans 
                (Id, ContainerNumber, ScanTime, PicNumber, FycoPresent, VesselName, OperatorId, 
                 ScanResult, GoodsDescription, ShippingCompany, Consignee, FilePath, SyncStatus, 
                 CreatedAt, ProcessedAt, HasImage, ImageCount)
                VALUES 
                (NEWID(), @ContainerNumber, @ScanTime, @PicNumber, @FycoPresent, @VesselName, @OperatorId,
                 @ScanResult, @GoodsDescription, @ShippingCompany, @Consignee, @FilePath, @SyncStatus,
                 GETUTCDATE(), @ProcessedAt, @HasImage, @ImageCount)
"@
            
            $insertCmd = New-Object System.Data.SqlClient.SqlCommand($insertQuery, $connection)
            $insertCmd.Parameters.AddWithValue("@ContainerNumber", $missingContainer) | Out-Null
            $insertCmd.Parameters.AddWithValue("@ScanTime", $scanTime) | Out-Null
            $insertCmd.Parameters.AddWithValue("@PicNumber", $picNumber) | Out-Null
            $insertCmd.Parameters.AddWithValue("@FycoPresent", [DBNull]::Value) | Out-Null
            if ($fycoPresent) { $insertCmd.Parameters["@FycoPresent"].Value = $fycoPresent }
            $insertCmd.Parameters.AddWithValue("@VesselName", [DBNull]::Value) | Out-Null
            if ($vesselName) { $insertCmd.Parameters["@VesselName"].Value = $vesselName }
            $insertCmd.Parameters.AddWithValue("@OperatorId", [DBNull]::Value) | Out-Null
            if ($operatorId) { $insertCmd.Parameters["@OperatorId"].Value = $operatorId }
            $insertCmd.Parameters.AddWithValue("@ScanResult", [DBNull]::Value) | Out-Null
            if ($scanResult) { $insertCmd.Parameters["@ScanResult"].Value = $scanResult }
            $insertCmd.Parameters.AddWithValue("@GoodsDescription", [DBNull]::Value) | Out-Null
            if ($goodsDescription) { $insertCmd.Parameters["@GoodsDescription"].Value = $goodsDescription }
            $insertCmd.Parameters.AddWithValue("@ShippingCompany", [DBNull]::Value) | Out-Null
            if ($shippingCompany) { $insertCmd.Parameters["@ShippingCompany"].Value = $shippingCompany }
            $insertCmd.Parameters.AddWithValue("@Consignee", [DBNull]::Value) | Out-Null
            if ($consignee) { $insertCmd.Parameters["@Consignee"].Value = $consignee }
            $insertCmd.Parameters.AddWithValue("@FilePath", [DBNull]::Value) | Out-Null
            if ($filePath) { $insertCmd.Parameters["@FilePath"].Value = $filePath }
            $insertCmd.Parameters.AddWithValue("@SyncStatus", $syncStatus) | Out-Null
            $insertCmd.Parameters.AddWithValue("@ProcessedAt", [DBNull]::Value) | Out-Null
            $insertCmd.Parameters.AddWithValue("@HasImage", $hasImage) | Out-Null
            $insertCmd.Parameters.AddWithValue("@ImageCount", $imageCount) | Out-Null
            
            try {
                $insertCmd.ExecuteNonQuery() | Out-Null
                Write-Host "  ✅ Created record for container: $missingContainer" -ForegroundColor Green
                $stats.RecordsUpdated++
            }
            catch {
                Write-Host "  ❌ Error creating record for $missingContainer : $($_.Exception.Message)" -ForegroundColor Red
                $stats.Errors++
            }
        }
        
        $stats.FilesProcessed++
    }
    catch {
        Write-Host "  ❌ Error processing file $($xmlFile.Name): $($_.Exception.Message)" -ForegroundColor Red
        $stats.Errors++
    }
    
    # Progress indicator
    if ($currentBatch % 10 -eq 0) {
        Write-Host "  Progress: $currentBatch/$($xmlFiles.Count) files processed..." -ForegroundColor Gray
    }
}

$connection.Close()

Write-Host ""
Write-Host "========================================" -ForegroundColor Cyan
Write-Host "Rescan Complete!" -ForegroundColor Green
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""
Write-Host "Statistics:" -ForegroundColor Yellow
Write-Host "  Files Processed: $($stats.FilesProcessed)" -ForegroundColor White
Write-Host "  Files with Multi-Container: $($stats.FilesWithMultiContainer)" -ForegroundColor White
Write-Host "  Missing Containers Found: $($stats.MissingContainersFound)" -ForegroundColor White
if (-not $DryRun) {
    Write-Host "  Records Updated: $($stats.RecordsUpdated)" -ForegroundColor Green
} else {
    Write-Host "  Records Would Be Updated: $($stats.MissingContainersFound)" -ForegroundColor Yellow
}
Write-Host "  Errors: $($stats.Errors)" -ForegroundColor $(if ($stats.Errors -eq 0) { "Green" } else { "Red" })
Write-Host ""

if ($DryRun) {
    Write-Host "This was a DRY RUN. No changes were made." -ForegroundColor Yellow
    Write-Host "Run without -DryRun to apply changes." -ForegroundColor Yellow
}


# Analyze FS6000 December 2025 Ingestion - Find Missing Scans
# Scans archive folder and compares with database records

param(
    [Parameter(Mandatory=$false)]
    [int]$Year = 2025,
    
    [Parameter(Mandatory=$false)]
    [int]$Month = 12,
    
    [Parameter(Mandatory=$false)]
    [string]$ArchivePath = "C:\NickScan\FS6000\Archive"
)

$ErrorActionPreference = "Stop"

Write-Host "=====================================================" -ForegroundColor Cyan
Write-Host "FS6000 DECEMBER INGESTION ANALYSIS" -ForegroundColor Cyan
Write-Host "=====================================================" -ForegroundColor Cyan
Write-Host ""
Write-Host "Archive Path: $ArchivePath" -ForegroundColor Yellow
Write-Host "Target: Year $Year, Month $Month" -ForegroundColor Yellow
Write-Host ""

# Database connection string (from appsettings.json format)
$connectionString = "Server=127.0.0.1,1433;Database=NS_CIS;Trusted_Connection=true;MultipleActiveResultSets=true;Encrypt=true;TrustServerCertificate=true;"

# Check archive path
if (-not (Test-Path $ArchivePath)) {
    Write-Host "❌ Archive path not found: $ArchivePath" -ForegroundColor Red
    exit 1
}

$yearPath = Join-Path $ArchivePath $Year
if (-not (Test-Path $yearPath)) {
    Write-Host "❌ Year folder not found: $yearPath" -ForegroundColor Red
    Write-Host "Available year folders:" -ForegroundColor Yellow
    Get-ChildItem $ArchivePath -Directory | Select-Object -ExpandProperty Name | ForEach-Object { Write-Host "  - $_" -ForegroundColor Gray }
    exit 1
}

# Find all December folders (1201, 1202, ..., 1231)
$monthPattern = $Month.ToString("00") + "\d{2}"
$monthFolders = Get-ChildItem $yearPath -Directory | Where-Object { $_.Name -match "^$monthPattern$" } | Sort-Object Name

if ($monthFolders.Count -eq 0) {
    Write-Host "⚠️  No folders found for month $Month" -ForegroundColor Yellow
    Write-Host "Available month-day folders:" -ForegroundColor Yellow
    Get-ChildItem $yearPath -Directory | Select-Object -First 10 -ExpandProperty Name | Sort-Object Name | ForEach-Object { Write-Host "  - $_" -ForegroundColor Gray }
    exit 0
}

Write-Host "Found $($monthFolders.Count) day folders for month $Month" -ForegroundColor Green
Write-Host ""

# Function to parse XML and extract scan data
function Parse-XmlFile {
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
                
                # Create record for each container number
                foreach ($container in $containerNumbers) {
                    if ([string]::IsNullOrWhiteSpace($container)) { continue }
                    
                    $scans += [PSCustomObject]@{
                        ContainerNumber = $container
                        ScanTime = $scanTime
                        PicNumber = $picNumber
                        XmlFile = $XmlFilePath
                    }
                }
            }
        } else {
            # Fallback: Try SCAN structure (if exists)
            $scanElements = $xml.SelectNodes("//SCAN") + $xml.SelectNodes("//Scan") + $xml.SelectNodes("//scan")
            
            foreach ($scan in $scanElements) {
                # Extract container numbers
                $unitId = if ($scan.UNITID) { ($scan.UNITID -split ',') | ForEach-Object { $_.Trim() } | Where-Object { $_ } } else { @() }
                $containerNo = if ($scan.container_no) { ($scan.container_no -split ',') | ForEach-Object { $_.Trim() } | Where-Object { $_ } } else { @() }
                
                $containerNumbers = @()
                if ($unitId) { $containerNumbers += $unitId }
                if ($containerNo) { $containerNumbers += $containerNo }
                $containerNumbers = $containerNumbers | Select-Object -Unique
                
                # Extract scan time
                $scanTimeStr = if ($scan.SCANTIME) { $scan.SCANTIME } elseif ($scan.scantime) { $scan.scantime } elseif ($scan.ScanTime) { $scan.ScanTime } elseif ($scan.TIMESTAMP) { $scan.TIMESTAMP } else { $null }
                $scanTime = [DateTime]::UtcNow
                if ($scanTimeStr) {
                    if (-not [DateTime]::TryParse($scanTimeStr, [ref]$scanTime)) {
                        $scanTime = [DateTime]::UtcNow
                    }
                }
                
                # Extract other fields
                $picNumber = if ($scan.PICNO) { $scan.PICNO } elseif ($scan.picno) { $scan.picno } else { "" }
                
                # Create record for each container number
                foreach ($container in $containerNumbers) {
                    if ([string]::IsNullOrWhiteSpace($container)) { continue }
                    
                    $scans += [PSCustomObject]@{
                        ContainerNumber = $container
                        ScanTime = $scanTime
                        PicNumber = $picNumber
                        XmlFile = $XmlFilePath
                    }
                }
            }
        }
        
        return $scans
    }
    catch {
        Write-Warning "Error parsing XML file $XmlFilePath : $($_.Exception.Message)"
        return @()
    }
}

# Scan all folders and collect archive data
Write-Host "Scanning archive folders..." -ForegroundColor Yellow
$archiveScans = @()
$folderCount = 0
$xmlFileCount = 0

foreach ($dayFolder in $monthFolders) {
    $folderCount++
    $dayPath = $dayFolder.FullName
    
    # Get all serial number subfolders
    $serialFolders = Get-ChildItem $dayPath -Directory | Sort-Object Name
    
    foreach ($serialFolder in $serialFolders) {
        $serialPath = $serialFolder.FullName
        
        # Find XML files
        $xmlFiles = Get-ChildItem $serialPath -Filter "*.xml" -File
        
        foreach ($xmlFile in $xmlFiles) {
            $xmlFileCount++
            $scans = Parse-XmlFile -XmlFilePath $xmlFile.FullName
            $archiveScans += $scans
        }
    }
    
    if ($folderCount % 5 -eq 0) {
        Write-Host "  Processed $folderCount / $($monthFolders.Count) day folders, found $xmlFileCount XML files..." -ForegroundColor Gray
    }
}

Write-Host ""
Write-Host "Archive Scan Complete:" -ForegroundColor Green
Write-Host "  - Day folders scanned: $folderCount" -ForegroundColor Cyan
Write-Host "  - XML files found: $xmlFileCount" -ForegroundColor Cyan
Write-Host "  - Scan records extracted: $($archiveScans.Count)" -ForegroundColor Cyan
Write-Host ""

# Query database for December 2025 records
Write-Host "Querying database for December $Year records..." -ForegroundColor Yellow

try {
    $startDate = Get-Date -Year $Year -Month $Month -Day 1
    $endDate = $startDate.AddMonths(1)
    
    $query = @"
SELECT 
    ContainerNumber,
    ScanTime,
    PicNumber,
    Id
FROM FS6000Scans
WHERE ScanTime >= '$($startDate.ToString("yyyy-MM-dd"))' 
  AND ScanTime < '$($endDate.ToString("yyyy-MM-dd"))'
"@
    
    # Use SqlClient for database queries
    $connection = New-Object System.Data.SqlClient.SqlConnection($connectionString)
    $command = $connection.CreateCommand()
    $command.CommandText = $query
    
    $connection.Open()
    $reader = $command.ExecuteReader()
    
    $dbScans = @()
    while ($reader.Read()) {
        $dbScans += [PSCustomObject]@{
            ContainerNumber = $reader["ContainerNumber"].ToString()
            ScanTime = [DateTime]$reader["ScanTime"]
            PicNumber = $reader["PicNumber"].ToString()
            Id = $reader["Id"].ToString()
        }
    }
    
    $connection.Close()
    
    Write-Host "Database records found: $($dbScans.Count)" -ForegroundColor Green
    Write-Host ""
    
    # Compare: Find scans in archive but not in database
    Write-Host "Comparing archive vs database..." -ForegroundColor Yellow
    
    $missingScans = @()
    
    foreach ($archiveScan in $archiveScans) {
        # Try to match by container number + scan time (within 1 hour tolerance)
        $matched = $dbScans | Where-Object {
            $_.ContainerNumber -eq $archiveScan.ContainerNumber -and
            [Math]::Abs(($_.ScanTime - $archiveScan.ScanTime).TotalHours) -lt 1
        }
        
        if (-not $matched) {
            $missingScans += $archiveScan
        }
    }
    
    # Generate report
    Write-Host ""
    Write-Host "=====================================================" -ForegroundColor Cyan
    Write-Host "ANALYSIS RESULTS" -ForegroundColor Cyan
    Write-Host "=====================================================" -ForegroundColor Cyan
    Write-Host ""
    Write-Host "Archive Records: $($archiveScans.Count)" -ForegroundColor Cyan
    Write-Host "Database Records: $($dbScans.Count)" -ForegroundColor Cyan
    Write-Host "Missing Records: $($missingScans.Count)" -ForegroundColor $(if ($missingScans.Count -gt 0) { "Red" } else { "Green" })
    Write-Host ""
    
    if ($missingScans.Count -gt 0) {
        Write-Host "Missing Scans Summary:" -ForegroundColor Yellow
        $missingScans | Group-Object { $_.ScanTime.ToString("yyyy-MM-dd") } | 
            Sort-Object Name | 
            ForEach-Object {
                Write-Host "  $($_.Name): $($_.Count) missing scans" -ForegroundColor Red
            }
        
        Write-Host ""
        Write-Host "First 20 Missing Scans:" -ForegroundColor Yellow
        $missingScans | Select-Object -First 20 ContainerNumber, ScanTime, PicNumber, XmlFile | 
            Format-Table -AutoSize
        
        # Save detailed report to file
        $reportPath = "FS6000_MissingScans_December$Year.csv"
        $missingScans | Select-Object ContainerNumber, ScanTime, PicNumber, XmlFile | 
            Export-Csv -Path $reportPath -NoTypeInformation
        
        Write-Host ""
        Write-Host "✅ Detailed report saved to: $reportPath" -ForegroundColor Green
    } else {
        Write-Host "✅ No missing scans found! All archive records exist in database." -ForegroundColor Green
    }
    
} catch {
    Write-Host "❌ Error querying database: $($_.Exception.Message)" -ForegroundColor Red
    Write-Host "Stack trace: $($_.ScriptStackTrace)" -ForegroundColor Red
}

Write-Host ""
Write-Host "=====================================================" -ForegroundColor Cyan


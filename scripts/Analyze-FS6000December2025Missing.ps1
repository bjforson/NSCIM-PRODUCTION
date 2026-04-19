# Analyze FS6000 December 2025 Missing Ingestions
# Scans archive folder and compares with database to find missing records

param(
    [Parameter(Mandatory=$false)]
    [string]$ArchivePath = "C:\NickScan\FS6000\Archive",
    [Parameter(Mandatory=$false)]
    [string]$ApiBaseUrl = "http://10.0.1.254:5205",
    [Parameter(Mandatory=$false)]
    [switch]$DryRun = $true
)

$username = "admin"
$password = $env:NICKSCAN_SUPERADMIN_PASSWORD

Write-Host "=====================================================" -ForegroundColor Cyan
Write-Host "FS6000 DECEMBER 2025 MISSING INGESTIONS ANALYSIS" -ForegroundColor Cyan
Write-Host "=====================================================" -ForegroundColor Cyan
Write-Host ""
Write-Host "Archive Path: $ArchivePath" -ForegroundColor Yellow
Write-Host "Target Month: December 2025 (2025\12XX\)" -ForegroundColor Yellow
Write-Host "Mode: $(if ($DryRun) { 'DRY RUN (Report Only)' } else { 'FULL ANALYSIS (Will create missing records)' })" -ForegroundColor $(if ($DryRun) { "Yellow" } else { "Green" })
Write-Host ""

# Step 1: Login to API
Write-Host "Step 1: Authenticating..." -ForegroundColor Cyan
try {
    $loginUrl = "$apiBaseUrl/api/Authentication/login"
    $loginBody = @{ username = $username; password = $password } | ConvertTo-Json
    $loginResponse = Invoke-RestMethod -Uri $loginUrl -Method Post -Body $loginBody -ContentType "application/json"
    $token = $loginResponse.token
    $headers = @{ Authorization = "Bearer $token" }
    Write-Host "✅ Authenticated as: $($loginResponse.user.username)" -ForegroundColor Green
} catch {
    Write-Host "❌ Authentication failed: $($_.Exception.Message)" -ForegroundColor Red
    exit 1
}

Write-Host ""

# Step 2: Scan December 2025 folders in archive
Write-Host "Step 2: Scanning archive folder for December 2025..." -ForegroundColor Cyan
$december2025Path = Join-Path $ArchivePath "2025"
$decemberFolders = @()

if (Test-Path $december2025Path) {
    $allMonthFolders = Get-ChildItem $december2025Path -Directory | Where-Object { 
        $_.Name -match '^12\d{2}$' -and $_.Name -ge '1201' -and $_.Name -le '1231'
    } | Sort-Object Name
    
    Write-Host "  Found $($allMonthFolders.Count) December day folders (1201-1231)" -ForegroundColor Gray
    
    foreach ($monthDayFolder in $allMonthFolders) {
        $serialFolders = Get-ChildItem $monthDayFolder.FullName -Directory | Where-Object {
            $_.Name -match '^\d{4}$'
        } | Sort-Object Name
        
        foreach ($serialFolder in $serialFolders) {
            $xmlFiles = Get-ChildItem $serialFolder.FullName -Filter "*.xml" -File
            if ($xmlFiles.Count -gt 0) {
                $decemberFolders += [PSCustomObject]@{
                    FolderPath = $serialFolder.FullName
                    RelativePath = "2025\$($monthDayFolder.Name)\$($serialFolder.Name)"
                    MonthDay = $monthDayFolder.Name
                    Serial = $serialFolder.Name
                    XmlFiles = $xmlFiles
                    ImageFiles = (Get-ChildItem $serialFolder.FullName -Filter "*.jpg" -File)
                }
            }
        }
    }
    
    Write-Host "  ✅ Found $($decemberFolders.Count) folders with XML files" -ForegroundColor Green
} else {
    Write-Host "  ❌ Archive path does not exist: $december2025Path" -ForegroundColor Red
    exit 1
}

Write-Host ""

# Step 3: Query database for existing December 2025 records
Write-Host "Step 3: Querying database for existing December 2025 records..." -ForegroundColor Cyan
try {
    $startDate = "2025-12-01T00:00:00Z"
    $endDate = "2025-12-31T23:59:59Z"
    $statsUrl = "$apiBaseUrl/api/FS6000/statistics?startDate=$startDate&endDate=$endDate"
    $stats = Invoke-RestMethod -Uri $statsUrl -Method Get -Headers $headers
    
    Write-Host "  Database records for December 2025: $($stats.scans.total)" -ForegroundColor Gray
    
    # Get detailed records (paginated)
    $allDbRecords = @()
    $page = 1
    $pageSize = 100
    
    do {
        $recordsUrl = "$apiBaseUrl/api/FS6000/scans?page=$page&pageSize=$pageSize&startDate=$startDate&endDate=$endDate"
        $response = Invoke-RestMethod -Uri $recordsUrl -Method Get -Headers $headers
        $allDbRecords += $response.data
        $page++
    } while ($response.data.Count -eq $pageSize)
    
    Write-Host "  ✅ Retrieved $($allDbRecords.Count) records from database" -ForegroundColor Green
} catch {
    Write-Host "  ❌ Error querying database: $($_.Exception.Message)" -ForegroundColor Red
    Write-Host "  Continuing with archive analysis only..." -ForegroundColor Yellow
    $allDbRecords = @()
}

Write-Host ""

# Step 4: Parse XML files and extract metadata
Write-Host "Step 4: Parsing XML files from archive..." -ForegroundColor Cyan
$archiveScans = @()
$parseErrors = @()

foreach ($folder in $decemberFolders) {
    foreach ($xmlFile in $folder.XmlFiles) {
        try {
            [xml]$xmlContent = Get-Content $xmlFile.FullName -Raw
            
            # Extract container numbers (handle multiple containers)
            $containerNumbers = @()
            
            # Try UNITID attribute
            $unitIdNode = $xmlContent.SelectSingleNode("//UNITID")
            $unitId = $null
            if ($unitIdNode -ne $null -and $unitIdNode.InnerText) {
                $unitId = $unitIdNode.InnerText.Trim()
                if ($unitId) {
                    $containerNumbers += $unitId -split ',' | ForEach-Object { $_.Trim() } | Where-Object { $_ -match '^[A-Z]{4}\d{7}$' }
                }
            }
            
            # Try container_no element
            $containerNoNode = $xmlContent.SelectSingleNode("//container_no")
            $containerNo = $null
            if ($containerNoNode -ne $null -and $containerNoNode.InnerText) {
                $containerNo = $containerNoNode.InnerText.Trim()
                if ($containerNo) {
                    $containerNumbers += $containerNo -split ',' | ForEach-Object { $_.Trim() } | Where-Object { $_ -match '^[A-Z]{4}\d{7}$' }
                }
            }
            
            # Remove duplicates
            $containerNumbers = $containerNumbers | Select-Object -Unique
            
            # Extract scan time
            $scanTimeNode = $xmlContent.SelectSingleNode("//SCANTIME")
            $scanTimeStr = $null
            if ($scanTimeNode -ne $null -and $scanTimeNode.InnerText) {
                $scanTimeStr = $scanTimeNode.InnerText.Trim()
            }
            if (-not $scanTimeStr) {
                $scanTimeNode = $xmlContent.SelectSingleNode("//scantime")
                if ($scanTimeNode -ne $null -and $scanTimeNode.InnerText) {
                    $scanTimeStr = $scanTimeNode.InnerText.Trim()
                }
            }
            if (-not $scanTimeStr) {
                $scanTimeNode = $xmlContent.SelectSingleNode("//ScanTime")
                if ($scanTimeNode -ne $null -and $scanTimeNode.InnerText) {
                    $scanTimeStr = $scanTimeNode.InnerText.Trim()
                }
            }
            
            $scanTime = $null
            if ($scanTimeStr) {
                if ([DateTime]::TryParse($scanTimeStr, [ref]$scanTime)) {
                    # Success
                } else {
                    # Try to infer from folder structure (2025\MMDD\SSSS)
                    $scanTime = [DateTime]::new(2025, [int]$folder.MonthDay.Substring(0,2), [int]$folder.MonthDay.Substring(2,2))
                }
            } else {
                # Infer from folder structure
                $scanTime = [DateTime]::new(2025, [int]$folder.MonthDay.Substring(0,2), [int]$folder.MonthDay.Substring(2,2))
            }
            
            # Extract other metadata
            $picNoNode = $xmlContent.SelectSingleNode("//PICNO")
            $picNumber = ""
            if ($picNoNode -ne $null -and $picNoNode.InnerText) {
                $picNumber = $picNoNode.InnerText.Trim()
            }
            if (-not $picNumber) {
                $picNoNode = $xmlContent.SelectSingleNode("//picno")
                if ($picNoNode -ne $null -and $picNoNode.InnerText) {
                    $picNumber = $picNoNode.InnerText.Trim()
                }
            }
            
            foreach ($containerNumber in $containerNumbers) {
                $archiveScans += [PSCustomObject]@{
                    ContainerNumber = $containerNumber
                    ScanTime = $scanTime
                    PicNumber = $picNumber
                    XmlFilePath = $xmlFile.FullName
                    ImageFilePath = ($folder.ImageFiles | Select-Object -First 1).FullName
                    RelativePath = $folder.RelativePath
                    MonthDay = $folder.MonthDay
                    Serial = $folder.Serial
                }
            }
        } catch {
            $parseErrors += [PSCustomObject]@{
                XmlFile = $xmlFile.FullName
                Error = $_.Exception.Message
            }
            Write-Host "  ⚠️  Error parsing $($xmlFile.Name): $($_.Exception.Message)" -ForegroundColor Yellow
        }
    }
}

Write-Host "  ✅ Parsed $($archiveScans.Count) container scans from XML files" -ForegroundColor Green
if ($parseErrors.Count -gt 0) {
    Write-Host "  ⚠️  $($parseErrors.Count) XML files had parse errors" -ForegroundColor Yellow
}

Write-Host ""

# Step 5: Compare archive scans with database records
Write-Host "Step 5: Comparing archive scans with database records..." -ForegroundColor Cyan

# Create lookup of database records (by container number + scan time within 1 hour tolerance)
$dbLookup = @{}
foreach ($dbRecord in $allDbRecords) {
    $key = "$($dbRecord.containerNumber)|$($dbRecord.scanTime)"
    $dbLookup[$key] = $dbRecord
}

$missingScans = @()
foreach ($archiveScan in $archiveScans) {
    # Try exact match first
    $exactKey = "$($archiveScan.ContainerNumber)|$($archiveScan.ScanTime.ToString('yyyy-MM-ddTHH:mm:ss'))"
    
    # Try with 1-hour tolerance
    $found = $false
    foreach ($dbRecord in $allDbRecords) {
        if ($dbRecord.containerNumber -eq $archiveScan.ContainerNumber) {
            $dbScanTime = [DateTime]::Parse($dbRecord.scanTime)
            $timeDiff = [Math]::Abs(($dbScanTime - $archiveScan.ScanTime).TotalHours)
            if ($timeDiff -le 1) {
                $found = $true
                break
            }
        }
    }
    
    if (-not $found) {
        $missingScans += $archiveScan
    }
}

Write-Host "  Archive scans found: $($archiveScans.Count)" -ForegroundColor Gray
Write-Host "  Database records: $($allDbRecords.Count)" -ForegroundColor Gray
Write-Host "  Missing scans: $($missingScans.Count)" -ForegroundColor $(if ($missingScans.Count -gt 0) { "Red" } else { "Green" })

Write-Host ""

# Step 6: Generate report
Write-Host "Step 6: Generating report..." -ForegroundColor Cyan
Write-Host ""
Write-Host "=====================================================" -ForegroundColor Cyan
Write-Host "ANALYSIS REPORT" -ForegroundColor Cyan
Write-Host "=====================================================" -ForegroundColor Cyan
Write-Host ""

if ($missingScans.Count -gt 0) {
    Write-Host "❌ MISSING SCANS FOUND: $($missingScans.Count)" -ForegroundColor Red
    Write-Host ""
    
    # Group by day
    $byDay = $missingScans | Group-Object MonthDay | Sort-Object Name
    Write-Host "Breakdown by day:" -ForegroundColor Yellow
    foreach ($dayGroup in $byDay) {
        $day = $dayGroup.Name
        $month = $day.Substring(0,2)
        $dayOfMonth = $day.Substring(2,2)
        Write-Host "  December $dayOfMonth ($day): $($dayGroup.Count) missing scans" -ForegroundColor Gray
    }
    
    Write-Host ""
    Write-Host "Sample missing scans (first 10):" -ForegroundColor Yellow
    $missingScans | Select-Object -First 10 | Format-Table ContainerNumber, ScanTime, MonthDay, Serial, RelativePath -AutoSize
    
    if ($DryRun) {
        Write-Host ""
        Write-Host "⚠️  DRY RUN MODE: No records will be created." -ForegroundColor Yellow
        Write-Host "   Run with -DryRun:`$false to create missing records." -ForegroundColor Yellow
    } else {
        Write-Host ""
        Write-Host "⚠️  FULL MODE: Ready to create missing records." -ForegroundColor Green
        Write-Host "   This will create $($missingScans.Count) new FS6000Scan records." -ForegroundColor Green
        Write-Host "   Press Ctrl+C to cancel, or wait 5 seconds to continue..." -ForegroundColor Yellow
        Start-Sleep -Seconds 5
        
        # TODO: Create missing records via API
        Write-Host "   [RECORD CREATION NOT YET IMPLEMENTED - Would create $($missingScans.Count) records]" -ForegroundColor Yellow
    }
} else {
    Write-Host "✅ NO MISSING SCANS FOUND!" -ForegroundColor Green
    Write-Host "   All archive scans have corresponding database records." -ForegroundColor Green
}

Write-Host ""
Write-Host "=====================================================" -ForegroundColor Cyan

# Save detailed report to file
$reportPath = "FS6000_December2025_Analysis_$(Get-Date -Format 'yyyyMMdd_HHmmss').txt"
$reportContent = @"
FS6000 DECEMBER 2025 MISSING INGESTIONS ANALYSIS
================================================
Date: $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')
Archive Path: $ArchivePath
Mode: $(if ($DryRun) { 'DRY RUN' } else { 'FULL' })

SUMMARY:
- Archive folders scanned: $($decemberFolders.Count)
- Archive scans found: $($archiveScans.Count)
- Database records: $($allDbRecords.Count)
- Missing scans: $($missingScans.Count)
- Parse errors: $($parseErrors.Count)

MISSING SCANS BREAKDOWN BY DAY:
$($byDay | ForEach-Object { "$($_.Name): $($_.Count) scans" } | Out-String)

DETAILED MISSING SCANS:
$($missingScans | Format-Table -AutoSize | Out-String)
"@

$reportContent | Out-File $reportPath -Encoding UTF8
Write-Host "Detailed report saved to: $reportPath" -ForegroundColor Cyan


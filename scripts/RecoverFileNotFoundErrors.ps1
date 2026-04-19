# Recovery script to attempt to recover "File not found" errors
# This script checks if files exist in archive directories and updates database paths

param(
    [string]$ConnectionStringName = "ICUMS_Downloads_Connection",
    [switch]$DryRun = $false
)

Write-Host "========================================" -ForegroundColor Cyan
Write-Host "RECOVER FILE NOT FOUND ERRORS" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""

if ($DryRun) {
    Write-Host "DRY RUN MODE - No changes will be made" -ForegroundColor Yellow
    Write-Host ""
}

# Load connection string from appsettings.json
$appsettingsPath = "src\NickScanCentralImagingPortal.API\appsettings.json"
if (-not (Test-Path $appsettingsPath)) {
    Write-Host "ERROR: appsettings.json not found at $appsettingsPath" -ForegroundColor Red
    exit 1
}

$appsettings = Get-Content $appsettingsPath | ConvertFrom-Json
$connectionString = $appsettings.ConnectionStrings.$ConnectionStringName

if (-not $connectionString) {
    Write-Host "ERROR: Connection string '$ConnectionStringName' not found in appsettings.json" -ForegroundColor Red
    exit 1
}

# Get downloads path from configuration
$downloadsPath = "C:\ICUMS Downloads"
if (-not (Test-Path $downloadsPath)) {
    Write-Host "WARNING: Downloads path not found: $downloadsPath" -ForegroundColor Yellow
    Write-Host "Please verify the path is correct" -ForegroundColor Yellow
    exit 1
}

Write-Host "Downloads path: $downloadsPath" -ForegroundColor Gray
Write-Host "Connecting to database..." -ForegroundColor Yellow
Write-Host ""

try {
    $connection = New-Object System.Data.SqlClient.SqlConnection($connectionString)
    $connection.Open()
    Write-Host "OK: Connected to database" -ForegroundColor Green
    Write-Host ""
}
catch {
    Write-Host "ERROR: Could not connect to database: $_" -ForegroundColor Red
    exit 1
}

# Get all files with "File not found" errors
Write-Host "========================================" -ForegroundColor Cyan
Write-Host "STEP 1: Finding files with 'File not found' errors" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""

$query1 = "SELECT Id, FileName, FilePath, ErrorMessage FROM DownloadedFiles WHERE ProcessingStatus = 'Failed' AND (ErrorMessage LIKE '%File not found%' OR ErrorMessage LIKE '%does not exist%' OR ErrorMessage LIKE '%FileNotFoundException%')"

try {
    $command = New-Object System.Data.SqlClient.SqlCommand($query1, $connection)
    $reader = $command.ExecuteReader()
    
    $filesToRecover = @()
    while ($reader.Read()) {
        $filesToRecover += @{
            Id = $reader["Id"]
            FileName = $reader["FileName"]
            FilePath = $reader["FilePath"]
            ErrorMessage = $reader["ErrorMessage"]
        }
    }
    $reader.Close()
    
    Write-Host "Found $($filesToRecover.Count) files with 'File not found' errors" -ForegroundColor White
    Write-Host ""
}
catch {
    Write-Host "ERROR executing query: $_" -ForegroundColor Red
    $connection.Close()
    exit 1
}

if ($filesToRecover.Count -eq 0) {
    Write-Host "No files to recover" -ForegroundColor Green
    $connection.Close()
    exit 0
}

# Check archive directories for each file
Write-Host "========================================" -ForegroundColor Cyan
Write-Host "STEP 2: Checking archive directories" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""

$archivePaths = @(
    (Join-Path $downloadsPath "Archive\BatchData"),
    (Join-Path $downloadsPath "Archive\ContainerData"),
    (Join-Path $downloadsPath "Archive\Other")
)

$recoveredFiles = @()
$notFoundFiles = @()
$alreadyProcessedFiles = @()

foreach ($file in $filesToRecover) {
    $found = $false
    $foundPath = $null
    
    # Check each archive directory
    foreach ($archivePath in $archivePaths) {
        $archiveFile = Join-Path $archivePath $file.FileName
        if (Test-Path $archiveFile) {
            $found = $true
            $foundPath = $archiveFile
            break
        }
    }
    
    if ($found) {
        Write-Host "✓ Found: $($file.FileName) -> $foundPath" -ForegroundColor Green
        $recoveredFiles += @{
            Id = $file.Id
            FileName = $file.FileName
            OldPath = $file.FilePath
            NewPath = $foundPath
        }
    }
    else {
        # Check if file has BOE documents (was already processed)
        $checkQuery = "SELECT COUNT(*) AS DocCount FROM BOEDocuments WHERE DownloadedFileId = @fileId"
        $checkCmd = New-Object System.Data.SqlClient.SqlCommand($checkQuery, $connection)
        $checkCmd.Parameters.AddWithValue("@fileId", $file.Id) | Out-Null
        $docCount = $checkCmd.ExecuteScalar()
        
        if ($docCount -gt 0) {
            Write-Host "✓ Already processed: $($file.FileName) (has $docCount BOE documents)" -ForegroundColor Yellow
            $alreadyProcessedFiles += @{
                Id = $file.Id
                FileName = $file.FileName
                DocCount = $docCount
            }
        }
        else {
            Write-Host "✗ Not found: $($file.FileName)" -ForegroundColor Red
            $notFoundFiles += $file
        }
    }
}

Write-Host ""
Write-Host "========================================" -ForegroundColor Cyan
Write-Host "RECOVERY SUMMARY" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""
Write-Host "Files found in archive: $($recoveredFiles.Count)" -ForegroundColor Green
Write-Host "Files already processed: $($alreadyProcessedFiles.Count)" -ForegroundColor Yellow
Write-Host "Files not found: $($notFoundFiles.Count)" -ForegroundColor Red
Write-Host ""

# Update database for recovered files
if ($recoveredFiles.Count -gt 0) {
    Write-Host "========================================" -ForegroundColor Cyan
    Write-Host "STEP 3: Updating database" -ForegroundColor Cyan
    Write-Host "========================================" -ForegroundColor Cyan
    Write-Host ""
    
    if ($DryRun) {
        Write-Host "DRY RUN - Would update the following files:" -ForegroundColor Yellow
        foreach ($file in $recoveredFiles) {
            Write-Host "  ID $($file.Id): $($file.FileName)" -ForegroundColor Gray
            Write-Host "    Old: $($file.OldPath)" -ForegroundColor Gray
            Write-Host "    New: $($file.NewPath)" -ForegroundColor Gray
        }
    }
    else {
        $updateCount = 0
        foreach ($file in $recoveredFiles) {
            try {
                $updateQuery = "UPDATE DownloadedFiles SET FilePath = @newPath, ProcessingStatus = 'Pending', ErrorMessage = NULL, UpdatedAt = GETUTCDATE() WHERE Id = @fileId"
                $updateCmd = New-Object System.Data.SqlClient.SqlCommand($updateQuery, $connection)
                $updateCmd.Parameters.AddWithValue("@newPath", $file.NewPath) | Out-Null
                $updateCmd.Parameters.AddWithValue("@fileId", $file.Id) | Out-Null
                $updateCmd.ExecuteNonQuery() | Out-Null
                $updateCount++
                Write-Host "✓ Updated: $($file.FileName)" -ForegroundColor Green
            }
            catch {
                Write-Host "✗ Error updating $($file.FileName): $_" -ForegroundColor Red
            }
        }
        Write-Host ""
        Write-Host "Updated $updateCount files" -ForegroundColor Green
    }
    Write-Host ""
}

# Mark already processed files as Archived
if ($alreadyProcessedFiles.Count -gt 0) {
    Write-Host "========================================" -ForegroundColor Cyan
    Write-Host "STEP 4: Marking already processed files as Archived" -ForegroundColor Cyan
    Write-Host "========================================" -ForegroundColor Cyan
    Write-Host ""
    
    if ($DryRun) {
        Write-Host "DRY RUN - Would mark the following files as Archived:" -ForegroundColor Yellow
        foreach ($file in $alreadyProcessedFiles) {
            Write-Host "  ID $($file.Id): $($file.FileName) ($($file.DocCount) BOE documents)" -ForegroundColor Gray
        }
    }
    else {
        $archivedCount = 0
        foreach ($file in $alreadyProcessedFiles) {
            try {
                $archiveQuery = "UPDATE DownloadedFiles SET ProcessingStatus = 'Archived', ErrorMessage = 'Successfully processed and archived (file removed from disk)', UpdatedAt = GETUTCDATE() WHERE Id = @fileId"
                $archiveCmd = New-Object System.Data.SqlClient.SqlCommand($archiveQuery, $connection)
                $archiveCmd.Parameters.AddWithValue("@fileId", $file.Id) | Out-Null
                $archiveCmd.ExecuteNonQuery() | Out-Null
                $archivedCount++
                Write-Host "✓ Archived: $($file.FileName)" -ForegroundColor Green
            }
            catch {
                Write-Host "✗ Error archiving $($file.FileName): $_" -ForegroundColor Red
            }
        }
        Write-Host ""
        Write-Host "Archived $archivedCount files" -ForegroundColor Green
    }
    Write-Host ""
}

$connection.Close()

Write-Host "========================================" -ForegroundColor Cyan
Write-Host "RECOVERY COMPLETE" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""
Write-Host "Summary:" -ForegroundColor White
Write-Host "  - Files recovered (path updated): $($recoveredFiles.Count)" -ForegroundColor Green
Write-Host "  - Files archived (already processed): $($alreadyProcessedFiles.Count)" -ForegroundColor Yellow
Write-Host "  - Files not found: $($notFoundFiles.Count)" -ForegroundColor Red
Write-Host ""

if ($notFoundFiles.Count -gt 0) {
    Write-Host "Files that could not be recovered:" -ForegroundColor Yellow
    foreach ($file in $notFoundFiles | Select-Object -First 10) {
        Write-Host "  - $($file.FileName)" -ForegroundColor Gray
    }
    if ($notFoundFiles.Count -gt 10) {
        Write-Host "  ... and $($notFoundFiles.Count - 10) more" -ForegroundColor Gray
    }
    Write-Host ""
}

